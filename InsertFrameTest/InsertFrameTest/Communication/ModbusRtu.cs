using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;

namespace InsertFrameTest.Communication
{
    public class ModbusRtu : IDisposable
    {
        private SerialPort _port;
        private readonly object _lock = new object();
        private const int Timeout = 3000; // 3秒超时
        private const int DefaultCommAttempts = 3; // 通信失败最多重试次数（含首次）
        private const int CommRetryDelayMs = 100;

        public bool IsOpen => _port != null && _port.IsOpen;
        
        /// <summary>
        /// 端口类型，用于数据流监控
        /// </summary>
        public ModbusDataLogger.PortType PortType { get; set; } = ModbusDataLogger.PortType.All;
        
        /// <summary>
        /// 通信使能标志，false时禁止发送数据
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// 暂停标志，true时立即停止新的发送并尽快中断等待响应
        /// </summary>
        public bool Paused { get; private set; } = false;
        
        /// <summary>
        /// 使用计数，用于多工序并发控制
        /// </summary>
        private int _usageCount = 0;
        private readonly object _usageLock = new object();
        
        /// <summary>
        /// 增加使用计数，启用通信
        /// </summary>
        public void Acquire()
        {
            lock (_usageLock)
            {
                _usageCount++;
                Enabled = true;
                Paused = false;
            }
        }
        
        /// <summary>
        /// 减少使用计数，当计数为0时禁用通信
        /// </summary>
        public void Release()
        {
            lock (_usageLock)
            {
                _usageCount--;
                if (_usageCount <= 0)
                {
                    _usageCount = 0;
                    Enabled = false;
                }
            }
        }

        /// <summary>
        /// 暂停通信，立即阻止新的收发
        /// </summary>
        public void Pause()
        {
            Paused = true;
        }

        /// <summary>
        /// 恢复通信
        /// </summary>
        public void Resume()
        {
            Paused = false;
        }

        public void Open(string portName, int baudRate = 9600)
        {
            _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout  = Timeout,
                WriteTimeout = Timeout
            };
            _port.Open();
        }

        public void Close()
        {
            _port?.Close();
        }

        public byte[] SendRawFrame(byte[] request, int responseTimeoutMs = Timeout)
        {
            if (request == null || request.Length == 0)
                throw new ArgumentException("request不能为空", nameof(request));

            if (!Enabled || Paused)
                throw new InvalidOperationException($"{PortType}通信未启用，请先开始测试 (Enabled={Enabled}, Paused={Paused})");

            Exception lastError = null;
            for (int attempt = 1; attempt <= DefaultCommAttempts; attempt++)
            {
                try
                {
                    return SendRawFrameOnce(request, responseTimeoutMs);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    ModbusDataLogger.Log(
                        ModbusDataLogger.Direction.Info,
                        null,
                        $"原始帧通信第{attempt}/{DefaultCommAttempts}次失败: {ex.Message}",
                        PortType);

                    if (attempt >= DefaultCommAttempts)
                        break;

                    if (!Enabled || Paused)
                        throw new OperationCanceledException($"{PortType}通信已暂停。");

                    Thread.Sleep(CommRetryDelayMs);
                }
            }

            string detail = lastError != null ? lastError.Message : "未知错误";
            ModbusUserAlert.TryShowCommAbnormal(PortType.ToString(), DefaultCommAttempts, detail);
            if (lastError != null)
                throw lastError;
            throw new TimeoutException($"{PortType}连续{DefaultCommAttempts}次通信失败。");
        }

        private byte[] SendRawFrameOnce(byte[] request, int responseTimeoutMs)
        {
            lock (_lock)
            {
                if (_port == null || !_port.IsOpen)
                    throw new InvalidOperationException($"{PortType}端口未打开或已关闭，请检查串口连接。");

                if (!Enabled || Paused)
                    throw new OperationCanceledException($"{PortType}通信已暂停。");

                _port.DiscardInBuffer();
                ModbusDataLogger.Log(ModbusDataLogger.Direction.Send, request, "发送原始ASCII指令", PortType);
                _port.Write(request, 0, request.Length);

                var response = new List<byte>(64);
                DateTime deadline = DateTime.Now.AddMilliseconds(responseTimeoutMs);

                while (DateTime.Now < deadline)
                {
                    if (!Enabled || Paused)
                        throw new OperationCanceledException($"{PortType}通信已暂停。");

                    int available = _port.BytesToRead;
                    if (available > 0)
                    {
                        byte[] chunk = new byte[available];
                        int read = _port.Read(chunk, 0, chunk.Length);
                        for (int i = 0; i < read; i++)
                            response.Add(chunk[i]);

                        int count = response.Count;
                        if (count >= 2 && response[count - 2] == 0x0D && response[count - 1] == 0x0A)
                        {
                            byte[] result = response.ToArray();
                            ModbusDataLogger.Log(ModbusDataLogger.Direction.Receive, result, "收到原始ASCII响应", PortType);
                            return result;
                        }
                    }
                    else
                    {
                        Thread.Sleep(5);
                    }
                }

                string errMsg = $"{PortType}通信超时: 未在{responseTimeoutMs}ms内收到完整ASCII响应。";
                ModbusDataLogger.Log(ModbusDataLogger.Direction.Info, null, errMsg, PortType);
                throw new TimeoutException(errMsg);
            }
        }

        // FC03 读保持寄存器，返回寄存器值数组
        public ushort[] ReadHoldingRegisters(byte addr, ushort startReg, ushort count)
        {
            if (!Enabled || Paused) throw new InvalidOperationException($"{PortType}通信未启用，请先开始测试 (Enabled={Enabled}, Paused={Paused})");
            
            byte[] req = BuildRequest(addr, 0x03, startReg, count);
            ModbusDataLogger.Log(ModbusDataLogger.Direction.Send, req, $"读保持寄存器 0x{startReg:X4} 数量:{count}", PortType);
            
            byte[] rsp = SendReceive(req, 3 + count * 2 + 2);
            ModbusDataLogger.Log(ModbusDataLogger.Direction.Receive, rsp, $"读保持寄存器响应", PortType);
            
            ushort[] result = new ushort[count];
            for (int i = 0; i < count; i++)
                result[i] = (ushort)((rsp[3 + i * 2] << 8) | rsp[4 + i * 2]);
            return result;
        }

        // FC01 读线圈
        public bool[] ReadCoils(byte addr, ushort startCoil, ushort count)
        {
            if (!Enabled || Paused) throw new InvalidOperationException($"{PortType}通信未启用，请先开始测试 (Enabled={Enabled}, Paused={Paused})");
            
            byte[] req = BuildRequest(addr, 0x01, startCoil, count);
            int byteCount = (count + 7) / 8;
            byte[] rsp = SendReceive(req, 3 + byteCount + 2);
            bool[] result = new bool[count];
            for (int i = 0; i < count; i++)
                result[i] = (rsp[3 + i / 8] & (1 << (i % 8))) != 0;
            return result;
        }

        // FC06 写单个寄存器
        public void WriteSingleRegister(byte addr, ushort reg, ushort value)
        {
            ModbusDataLogger.LogInfo($"[ModbusRtu] WriteSingleRegister准备: addr={addr}, reg=0x{reg:X4}, Enabled={Enabled}, PortType={PortType}");
            
            if (!Enabled || Paused) 
            {
                string err = $"{PortType}通信未启用，请先开始测试 (Enabled={Enabled}, Paused={Paused})";
                ModbusDataLogger.LogInfo($"[ModbusRtu] 错误: {err}");
                throw new InvalidOperationException(err);
            }
            
            byte[] req = new byte[8];
            req[0] = addr;
            req[1] = 0x06;
            req[2] = (byte)(reg >> 8);
            req[3] = (byte)(reg & 0xFF);
            req[4] = (byte)(value >> 8);
            req[5] = (byte)(value & 0xFF);
            ushort crc = CalcCrc(req, 6);
            req[6] = (byte)(crc & 0xFF);
            req[7] = (byte)(crc >> 8);
            
            // 构建十六进制字符串用于调试
            string hexStr = BitConverter.ToString(req).Replace("-", " ");
            ModbusDataLogger.Log(ModbusDataLogger.Direction.Send, req, $"写寄存器 0x{reg:X4}={value} [{hexStr}]", PortType);
            byte[] rsp = SendReceive(req, 8);
            string rspHex = BitConverter.ToString(rsp).Replace("-", " ");
            ModbusDataLogger.Log(ModbusDataLogger.Direction.Receive, rsp, $"写寄存器响应 [{rspHex}]", PortType);
        }

        // FC05 写单个线圈
        public void WriteCoil(byte addr, ushort coil, bool value)
        {
            if (!Enabled || Paused) throw new InvalidOperationException($"{PortType}通信未启用，请先开始测试 (Enabled={Enabled}, Paused={Paused})");
            
            byte[] req = new byte[8];
            req[0] = addr;
            req[1] = 0x05;
            req[2] = (byte)(coil >> 8);
            req[3] = (byte)(coil & 0xFF);
            req[4] = value ? (byte)0xFF : (byte)0x00;
            req[5] = 0x00;
            ushort crc = CalcCrc(req, 6);
            req[6] = (byte)(crc & 0xFF);
            req[7] = (byte)(crc >> 8);
            SendReceive(req, 8);
        }

        // FC05 写单个线圈，允许发送设备协议定义的原始值（如0x0000/0x0001）
        public void WriteCoilValue(byte addr, ushort coil, ushort value)
        {
            if (!Enabled || Paused) throw new InvalidOperationException($"{PortType}通信未启用，请先开始测试 (Enabled={Enabled}, Paused={Paused})");

            byte[] req = new byte[8];
            req[0] = addr;
            req[1] = 0x05;
            req[2] = (byte)(coil >> 8);
            req[3] = (byte)(coil & 0xFF);
            req[4] = (byte)(value >> 8);
            req[5] = (byte)(value & 0xFF);
            ushort crc = CalcCrc(req, 6);
            req[6] = (byte)(crc & 0xFF);
            req[7] = (byte)(crc >> 8);

            string hexStr = BitConverter.ToString(req).Replace("-", " ");
            ModbusDataLogger.Log(ModbusDataLogger.Direction.Send, req, $"写线圈 0x{coil:X4}=0x{value:X4} [{hexStr}]", PortType);
            byte[] rsp = SendReceive(req, 8);
            string rspHex = BitConverter.ToString(rsp).Replace("-", " ");
            ModbusDataLogger.Log(ModbusDataLogger.Direction.Receive, rsp, $"写线圈响应 [{rspHex}]", PortType);

            for (int i = 0; i < req.Length; i++)
            {
                if (rsp[i] != req[i])
                    throw new InvalidOperationException($"{PortType}写线圈确认回包不匹配，请求=[{hexStr}] 响应=[{rspHex}]");
            }
        }

        // FC10 写多个寄存器
        public void WriteMultipleRegisters(byte addr, ushort startReg, ushort[] values)
        {
            if (!Enabled || Paused) throw new InvalidOperationException($"{PortType}通信未启用，请先开始测试 (Enabled={Enabled}, Paused={Paused})");
            
            int n = values.Length;
            byte[] req = new byte[9 + n * 2];
            req[0] = addr;
            req[1] = 0x10;
            req[2] = (byte)(startReg >> 8);
            req[3] = (byte)(startReg & 0xFF);
            req[4] = (byte)(n >> 8);
            req[5] = (byte)(n & 0xFF);
            req[6] = (byte)(n * 2);
            for (int i = 0; i < n; i++)
            {
                req[7 + i * 2] = (byte)(values[i] >> 8);
                req[8 + i * 2] = (byte)(values[i] & 0xFF);
            }
            ushort crc = CalcCrc(req, req.Length - 2);
            req[req.Length - 2] = (byte)(crc & 0xFF);
            req[req.Length - 1] = (byte)(crc >> 8);
            
            ModbusDataLogger.Log(ModbusDataLogger.Direction.Send, req, $"写多寄存器 0x{startReg:X4} 数量:{n}", PortType);
            byte[] rsp = SendReceive(req, 8);
            ModbusDataLogger.Log(ModbusDataLogger.Direction.Receive, rsp, "写多寄存器响应", PortType);
        }

        private byte[] BuildRequest(byte addr, byte fc, ushort startReg, ushort count)
        {
            byte[] req = new byte[8];
            req[0] = addr;
            req[1] = fc;
            req[2] = (byte)(startReg >> 8);
            req[3] = (byte)(startReg & 0xFF);
            req[4] = (byte)(count >> 8);
            req[5] = (byte)(count & 0xFF);
            ushort crc = CalcCrc(req, 6);
            req[6] = (byte)(crc & 0xFF);
            req[7] = (byte)(crc >> 8);
            return req;
        }

        private byte[] SendReceive(byte[] request, int expectedLen, int maxAttempts = DefaultCommAttempts)
        {
            if (maxAttempts < 1)
                maxAttempts = 1;

            Exception lastError = null;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    return SendReceiveOnce(request, expectedLen);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    ModbusDataLogger.Log(
                        ModbusDataLogger.Direction.Info,
                        null,
                        $"通信第{attempt}/{maxAttempts}次失败: {ex.Message}",
                        PortType);

                    if (attempt >= maxAttempts)
                        break;

                    if (!Enabled || Paused)
                        throw new OperationCanceledException($"{PortType}通信已暂停。");

                    Thread.Sleep(CommRetryDelayMs);
                }
            }

            string detail = lastError != null ? lastError.Message : "未知错误";
            ModbusUserAlert.TryShowCommAbnormal(PortType.ToString(), maxAttempts, detail);
            if (lastError != null)
                throw lastError;
            throw new TimeoutException($"{PortType}连续{maxAttempts}次通信失败。");
        }

        private byte[] SendReceiveOnce(byte[] request, int expectedLen)
        {
            lock (_lock)
            {
                if (_port == null || !_port.IsOpen)
                    throw new InvalidOperationException($"{PortType}端口未打开或已关闭，请检查串口连接。");

                if (!Enabled || Paused)
                    throw new OperationCanceledException($"{PortType}通信已暂停。");

                // 清空接收缓冲区
                _port.DiscardInBuffer();

                // 记录发送
                ModbusDataLogger.Log(ModbusDataLogger.Direction.Send, request, "发送请求", PortType);

                // 发送数据包
                _port.Write(request, 0, request.Length);

                Thread.Sleep(50);

                byte[] buf = new byte[expectedLen + 4];
                int received = 0;
                DateTime deadline = DateTime.Now.AddMilliseconds(Timeout);

                // 等待响应，3秒超时
                while (received < expectedLen && DateTime.Now < deadline)
                {
                    if (!Enabled || Paused)
                        throw new OperationCanceledException($"{PortType}通信已暂停。");

                    int avail = _port.BytesToRead;
                    if (avail > 0)
                    {
                        int n = _port.Read(buf, received, Math.Min(avail, buf.Length - received));
                        received += n;
                    }
                    else
                    {
                        Thread.Sleep(5);
                    }
                }

                // 检查是否收到响应
                if (received < 3)
                {
                    string errMsg = $"{PortType}通信超时: 3秒内未收到设备响应。" +
                                    $"请检查: 1)设备是否开机 2)接线是否正确 3)波特率设置是否匹配";
                    ModbusDataLogger.Log(ModbusDataLogger.Direction.Info, null, errMsg, PortType);
                    throw new TimeoutException(errMsg);
                }

                // 检查是否错误帧
                if ((buf[1] & 0x80) != 0)
                {
                    byte excFc = buf[1];
                    byte errCode = received > 2 ? buf[2] : (byte)0;
                    string errMsg = $"{PortType}设备返回错误: 功能码=0x{excFc:X2}, 错误码=0x{errCode:X2}" +
                                    $"({GetErrorCodeDescription(errCode)})";
                    ModbusDataLogger.Log(ModbusDataLogger.Direction.Info, null, errMsg, PortType);
                    throw new ModbusSlaveException(errMsg, excFc, errCode);
                }

                // 校验CRC
                ushort crcCalc = CalcCrc(buf, received - 2);
                ushort crcRecv = (ushort)(buf[received - 2] | (buf[received - 1] << 8));
                if (crcCalc != crcRecv)
                {
                    string errMsg = $"{PortType}数据校验失败: CRC错误。" +
                                    $"计算值=0x{crcCalc:X4}, 接收值=0x{crcRecv:X4}。" +
                                    $"请检查通信线路是否有干扰。";
                    ModbusDataLogger.Log(ModbusDataLogger.Direction.Info, null, errMsg, PortType);
                    throw new ModbusCrcException(errMsg, crcCalc, crcRecv);
                }

                // 成功收到响应
                byte[] result = new byte[received];
                Array.Copy(buf, result, received);

                ModbusDataLogger.Log(ModbusDataLogger.Direction.Receive, result, "收到响应", PortType);
                return result;
            }
        }

        /// <summary>
        /// 获取Modbus错误码描述
        /// </summary>
        private string GetErrorCodeDescription(byte errorCode)
        {
            switch (errorCode)
            {
                case 0x01: return "非法功能码";
                case 0x02: return "非法数据地址";
                case 0x03: return "非法数据值";
                case 0x04: return "从机设备故障";
                case 0x05: return "确认";
                case 0x06: return "从机设备忙";
                case 0x08: return "存储奇偶性差错";
                case 0x0A: return "不可用网关路径";
                case 0x0B: return "网关目标设备响应失败";
                default: return "未知错误";
            }
        }

        public static ushort CalcCrc(byte[] data, int length)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x0001) != 0)
                        crc = (ushort)((crc >> 1) ^ 0xA001);
                    else
                        crc >>= 1;
                }
            }
            return crc;
        }

        public void Dispose() => Close();
    }

    /// <summary>
    /// Modbus 从机异常响应（功能码最高位置 1）。
    /// </summary>
    public sealed class ModbusSlaveException : Exception
    {
        public byte ExceptionFunctionCode { get; }
        public byte ErrorCode { get; }

        public ModbusSlaveException(string message, byte exceptionFunctionCode, byte errorCode)
            : base(message)
        {
            ExceptionFunctionCode = exceptionFunctionCode;
            ErrorCode = errorCode;
        }

        /// <summary>0x04 从机故障 / 0x06 从机忙 —— 宜软重试。</summary>
        public bool IsTransientDeviceFault
        {
            get { return ErrorCode == 0x04 || ErrorCode == 0x06; }
        }
    }

    /// <summary>
    /// Modbus RTU CRC 校验失败（多为线路干扰、粘包或接线问题）。
    /// </summary>
    public sealed class ModbusCrcException : Exception
    {
        public ushort CalculatedCrc { get; }
        public ushort ReceivedCrc { get; }

        public ModbusCrcException(string message, ushort calculatedCrc, ushort receivedCrc)
            : base(message)
        {
            CalculatedCrc = calculatedCrc;
            ReceivedCrc = receivedCrc;
        }
    }
}

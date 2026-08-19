using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace InsertFrameTest.Communication
{
    /// <summary>
    /// Modbus数据流记录器 - 用于监控和记录Modbus通信数据
    /// </summary>
    public static class ModbusDataLogger
    {
        public enum Direction
        {
            Send,    // 发送
            Receive, // 接收
            Info     // 信息
        }

        public enum PortType
        {
            All,     // 全部
            Safety,  // 安规测试仪
            MC2900,  // MC2900
            SNMP     // SNMP OID 查询
        }

        public class DataFrame
        {
            public long Sequence { get; set; }
            public DateTime Timestamp { get; set; }
            public Direction Direction { get; set; }
            public PortType Port { get; set; }
            public byte[] Data { get; set; }
            public string Description { get; set; }
            public int SlaveId { get; set; }
            public string FunctionCode { get; set; }
        }

        // 数据帧缓存（最多保留1000帧）
        private static readonly List<DataFrame> _frames = new List<DataFrame>();
        private static readonly object _lock = new object();
        private const int MaxFrames = 1000;
        private static long _nextSequence;

        // 数据接收事件
        public static event Action<DataFrame> OnDataLogged;

        /// <summary>
        /// 记录数据帧
        /// </summary>
        public static void Log(Direction direction, byte[] data, string description = "", PortType port = PortType.All, string functionCode = null)
        {
            // 信息类日志允许data为null
            if (direction != Direction.Info && (data == null || data.Length == 0))
                return;

            // 统一处理null，避免Info日志传入null时出现空引用异常
            byte[] safeData = data == null ? new byte[0] : (byte[])data.Clone();

            var frame = new DataFrame
            {
                Sequence = Interlocked.Increment(ref _nextSequence),
                Timestamp = DateTime.Now,
                Direction = direction,
                Port = port,
                Data = safeData,
                Description = description,
                SlaveId = port == PortType.SNMP ? 0 : (safeData.Length > 0 ? safeData[0] : 0),
                FunctionCode = !string.IsNullOrEmpty(functionCode)
                    ? functionCode
                    : (safeData.Length > 1 ? GetFunctionCodeName(safeData[1]) : "")
            };

            lock (_lock)
            {
                _frames.Add(frame);
                // 限制缓存数量
                if (_frames.Count > MaxFrames)
                {
                    _frames.RemoveAt(0);
                }
            }

            // 触发事件
            OnDataLogged?.Invoke(frame);
        }

        /// <summary>
        /// 记录信息文本
        /// </summary>
        public static void LogInfo(string message, PortType port = PortType.All)
        {
            var frame = new DataFrame
            {
                Sequence = Interlocked.Increment(ref _nextSequence),
                Timestamp = DateTime.Now,
                Direction = Direction.Info,
                Port = port,
                Data = Encoding.UTF8.GetBytes(message ?? string.Empty),
                Description = message,
                SlaveId = 0,
                FunctionCode = "INFO"
            };

            lock (_lock)
            {
                _frames.Add(frame);
                if (_frames.Count > MaxFrames)
                {
                    _frames.RemoveAt(0);
                }
            }

            OnDataLogged?.Invoke(frame);
        }

        /// <summary>
        /// 获取所有数据帧
        /// </summary>
        public static List<DataFrame> GetAllFrames()
        {
            lock (_lock)
            {
                return CloneFrames(_frames);
            }
        }

        public static long GetLatestSequence()
        {
            return Volatile.Read(ref _nextSequence);
        }

        public static List<DataFrame> GetFramesAfter(long sequence, PortType? port = null)
        {
            lock (_lock)
            {
                var result = new List<DataFrame>();
                foreach (var frame in _frames)
                {
                    if (frame.Sequence <= sequence)
                        continue;

                    if (port.HasValue && frame.Port != port.Value)
                        continue;

                    result.Add(CloneFrame(frame));
                }

                return result;
            }
        }

        /// <summary>
        /// 清空数据
        /// </summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _frames.Clear();
            }
        }

        public static string FramesToText(IEnumerable<DataFrame> frames)
        {
            if (frames == null)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var frame in frames)
            {
                if (sb.Length > 0)
                    sb.AppendLine();

                sb.Append(FrameToString(frame));
            }

            return sb.ToString();
        }

        /// <summary>
        /// 将数据帧格式化为字符串
        /// </summary>
        public static string FrameToString(DataFrame frame)
        {
            var sb = new StringBuilder();
            bool hasAsciiText = TryDecodeAsciiFrame(frame.Data, out string asciiText);
            
            // 时间戳
            sb.Append(frame.Timestamp.ToString("HH:mm:ss.fff"));
            sb.Append(" ");
            
            // 端口标识
            string portName;
            switch (frame.Port)
            {
                case PortType.Safety:
                    portName = "[安规]";
                    break;
                case PortType.MC2900:
                    portName = "[MC2900]";
                    break;
                case PortType.SNMP:
                    portName = "[SNMP]";
                    break;
                default:
                    portName = "[全部]";
                    break;
            }
            sb.Append(portName);
            sb.Append(" ");
            
            // 方向指示
            switch (frame.Direction)
            {
                case Direction.Send:
                    sb.Append("[发送] ");
                    break;
                case Direction.Receive:
                    sb.Append("[接收] ");
                    break;
                case Direction.Info:
                    sb.Append("[信息] ");
                    break;
            }
            
            // 从机地址和功能码 / SNMP 描述
            if (frame.Direction != Direction.Info)
            {
                if (frame.Port == PortType.SNMP)
                {
                    if (!string.IsNullOrWhiteSpace(frame.FunctionCode))
                        sb.Append($"[{frame.FunctionCode}] ");
                }
                else if (hasAsciiText)
                    sb.Append("[ASCII] ");
                else
                    sb.Append($"从机{frame.SlaveId:X2} {frame.FunctionCode} ");
            }

            // 信息日志优先按UTF-8文本显示，避免把错误消息显示成十六进制字节流。
            if (frame.Direction == Direction.Info)
            {
                string infoText = string.IsNullOrWhiteSpace(frame.Description)
                    ? TryDecodeUtf8(frame.Data)
                    : frame.Description;

                if (!string.IsNullOrWhiteSpace(infoText))
                {
                    sb.Append(infoText);
                    return sb.ToString();
                }
            }

            // 数据内容（十六进制）
            if (frame.Data != null && frame.Data.Length > 0)
            {
                sb.Append("数据: ");
                foreach (var b in frame.Data)
                {
                    sb.Append($"{b:X2} ");
                }
            }

            if (hasAsciiText)
            {
                sb.Append(" | ASCII: ");
                sb.Append(asciiText);
            }
            
            // 描述
            if (!string.IsNullOrEmpty(frame.Description))
            {
                sb.Append($" | {frame.Description}");
            }
            
            return sb.ToString();
        }

        private static string TryDecodeUtf8(byte[] data)
        {
            if (data == null || data.Length == 0)
                return string.Empty;

            try
            {
                return Encoding.UTF8.GetString(data).TrimEnd('\0');
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool TryDecodeAsciiFrame(byte[] data, out string asciiText)
        {
            asciiText = string.Empty;
            if (data == null || data.Length < 4)
                return false;

            int payloadLength;
            if (data[data.Length - 2] == 0x0D && data[data.Length - 1] == 0x0A)
                payloadLength = data.Length - 3;
            else
                payloadLength = data.Length;

            if (payloadLength <= 0)
                return false;

            for (int i = 0; i < payloadLength; i++)
            {
                byte value = data[i];
                if (value < 0x20 || value > 0x7E)
                    return false;
            }

            asciiText = Encoding.ASCII.GetString(data, 0, payloadLength);
            return !string.IsNullOrWhiteSpace(asciiText);
        }

        private static List<DataFrame> CloneFrames(IEnumerable<DataFrame> frames)
        {
            var result = new List<DataFrame>();
            foreach (var frame in frames)
                result.Add(CloneFrame(frame));

            return result;
        }

        private static DataFrame CloneFrame(DataFrame frame)
        {
            return new DataFrame
            {
                Sequence = frame.Sequence,
                Timestamp = frame.Timestamp,
                Direction = frame.Direction,
                Port = frame.Port,
                Data = frame.Data == null ? Array.Empty<byte>() : (byte[])frame.Data.Clone(),
                Description = frame.Description,
                SlaveId = frame.SlaveId,
                FunctionCode = frame.FunctionCode,
            };
        }

        /// <summary>
        /// 解析功能码
        /// </summary>
        private static string GetFunctionCodeName(byte code)
        {
            // 处理错误码（最高位为1）
            if ((code & 0x80) != 0)
            {
                var baseCode = (byte)(code & 0x7F);
                return $"错误响应(0x{baseCode:X2})";
            }

            switch (code)
            {
                case 0x01: return "读线圈(01)";
                case 0x02: return "读离散输入(02)";
                case 0x03: return "读保持寄存器(03)";
                case 0x04: return "读输入寄存器(04)";
                case 0x05: return "写单线圈(05)";
                case 0x06: return "写单寄存器(06)";
                case 0x0F: return "写多线圈(0F)";
                case 0x10: return "写多寄存器(10)";
                case 0x17: return "读/写多寄存器(17)";
                default: return $"未知(0x{code:X2})";
            }
        }

        /// <summary>
        /// 导出为文本
        /// </summary>
        public static string ExportToText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Modbus通信数据流记录");
            sb.AppendLine($"导出时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("=".PadRight(80, '='));
            sb.AppendLine();

            lock (_lock)
            {
                foreach (var frame in _frames)
                {
                    sb.AppendLine(FrameToString(frame));
                }
            }

            return sb.ToString();
        }
    }
}

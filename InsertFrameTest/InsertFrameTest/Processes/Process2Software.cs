using System;
using System.Threading;
using InsertFrameTest.Communication;
using InsertFrameTest.Models;

namespace InsertFrameTest.Processes
{
    // 工序2: 软件升级 / 温度传感器 / 湿度传感器 / 时间校准
    // 注意：模块测试已改为手动控制，通过UI上的开始/停止按钮单独测试
    public class Process2Software : ProcessBase
    {
        private const int PollIntervalMs = 1000;
        private bool[] _sensorLogged = new bool[3];
        private bool _humidityLogged;
        private bool _deviceTimeLogged;
        protected override bool RequiresProgramFile => false;

        public Process2Software()
        {
            ProcessNo   = 2;
            ProcessName = "/温湿度/时间同步";
        }

        protected override void Execute(string barcode, byte[] programFile, ProcessResult result)
        {
            if (MC2900 == null)
                throw new InvalidOperationException("MC2900 未初始化，无法执行工序2。 ");

            var bus = MC2900.GetBus();
            if (bus == null || !bus.IsOpen)
                throw new InvalidOperationException("MC2900 通信未连接，无法执行工序2。 ");

            bool sensorReadPassed = false;
            bool timeReadPassed = false;
            int consecutiveCommErrors = 0;
            _sensorLogged = new bool[3];
            _humidityLogged = false;
            _deviceTimeLogged = false;

            OnDataUpdate(1, "--");
            OnDataUpdate(2, "--");
            OnDataUpdate(3, "--");
            OnDataUpdate(4, "--");
            OnDataUpdate(10, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            OnDataUpdate(11, "--");

            Log("工序2开始：循环读取3路温度、1路湿度和设备时间");

            while (bus.IsOpen && bus.Enabled && !bus.Paused && !IsStopRequested())
            {
                try
                {
                    if (MC2900.IsQueryPollingSuspended)
                    {
                        if (!DelayWithStopCheck(20))
                            break;
                        continue;
                    }

                    OnDataUpdate(10, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                    long sensorStart = ModbusDataLogger.GetLatestSequence();
                    ushort[] sensorRegisters = MC2900.ReadTemperatureRegisters();
                    PublishSensorValues(sensorRegisters);
                    LogSensorRawRecords(sensorRegisters, sensorStart);

                    if (MC2900.IsQueryPollingSuspended)
                    {
                        if (!DelayWithStopCheck(20))
                            break;
                        continue;
                    }

                    long humidityStart = ModbusDataLogger.GetLatestSequence();
                    ushort humidityRegister = MC2900.ReadHumidityRegister();
                    PublishHumidityValue(humidityRegister);
                    LogHumidityRawRecord(humidityRegister, humidityStart);
                    sensorReadPassed = true;
                    consecutiveCommErrors = 0;

                    if (MC2900.IsQueryPollingSuspended)
                    {
                        if (!DelayWithStopCheck(20))
                            break;
                        continue;
                    }

                    long timeStart = ModbusDataLogger.GetLatestSequence();
                    DateTime deviceTime = MC2900.GetSystemTime();
                    OnDataUpdate(11, deviceTime.ToString("yyyy-MM-dd HH:mm:ss"));
                    LogDeviceTimeRawRecord(deviceTime, timeStart);
                    timeReadPassed = true;
                    consecutiveCommErrors = 0;

                    result.Pass = sensorReadPassed && timeReadPassed;
                    if (!DelayWithStopCheck(PollIntervalMs))
                        break;
                }
                catch (OperationCanceledException)
                {
                    Log("工序2通信已停止");
                    break;
                }
                catch (Exception ex)
                {
                    consecutiveCommErrors++;
                    Log($"工序2轮询异常({consecutiveCommErrors})，继续尝试通信: {ex.Message}");
                    // 底层已对单次请求重试3次并弹窗；此处不中断，确认弹窗后仍持续发查询
                    if (!DelayWithStopCheck(PollIntervalMs))
                        break;
                }
            }

            AddBoolItem(result, 1, "温度/湿度传感器读取", sensorReadPassed);
            AddBoolItem(result, 2, "设备时间读取", timeReadPassed);
            result.Pass = sensorReadPassed && timeReadPassed;
        }

        private void PublishSensorValues(ushort[] registers)
        {
            if (registers == null || registers.Length < 3)
                throw new InvalidOperationException("温度返回数据长度不足，无法解析传感器1/2/3数据。");

            for (int i = 0; i < 3; i++)
                OnDataUpdate(i + 1, FormatTemperature(registers[i]));
        }

        private void PublishHumidityValue(ushort register)
        {
            OnDataUpdate(4, FormatHumidity(register));
        }

        private void LogSensorRawRecords(ushort[] registers, long startSequence)
        {
            if (registers == null)
                return;

            var frames = ModbusDataLogger.GetFramesAfter(startSequence, ModbusDataLogger.PortType.MC2900);
            string frameText = ModbusDataLogger.FramesToText(frames);
            for (int i = 0; i < 3 && i < registers.Length; i++)
            {
                if (_sensorLogged[i])
                    continue;

                // 仅在传感器出现有效温度时抓包记录（0xFFFF 视为无效/-1）
                if (registers[i] == ushort.MaxValue)
                    continue;

                string displayValue = FormatTemperature(registers[i]);
                string sensorName = $"温度传感器{i + 1}";
                string status = $"显示温度={displayValue}°C 原始寄存器=0x{registers[i]:X4}";
                string raw = $"{status}{Environment.NewLine}{frameText}";
                AppendRuntimeRawRecord(20 + i, $"工序2-{sensorName}原始记录", raw);
                AppendSensorCaptureToMes(sensorName, status, frameText);
                _sensorLogged[i] = true;
            }
        }

        private void LogHumidityRawRecord(ushort register, long startSequence)
        {
            if (_humidityLogged)
                return;

            // 0xFFFF 视为无效/-1，不抓包
            if (register == ushort.MaxValue)
                return;

            var frames = ModbusDataLogger.GetFramesAfter(startSequence, ModbusDataLogger.PortType.MC2900);
            string frameText = ModbusDataLogger.FramesToText(frames);
            string displayValue = FormatHumidity(register);
            string sensorName = "湿度传感器4";
            string status = $"显示湿度={displayValue}%RH 原始寄存器=0x{register:X4}";
            string raw = $"{status}{Environment.NewLine}{frameText}";
            AppendRuntimeRawRecord(24, $"工序2-{sensorName}原始记录", raw);
            AppendSensorCaptureToMes(sensorName, status, frameText);
            _humidityLogged = true;
        }

        private void LogDeviceTimeRawRecord(DateTime deviceTime, long startSequence)
        {
            if (_deviceTimeLogged)
                return;

            var frames = ModbusDataLogger.GetFramesAfter(startSequence, ModbusDataLogger.PortType.MC2900);
            string frameText = ModbusDataLogger.FramesToText(frames);
            AppendRuntimeRawRecord(23,
                "工序2-设备时间查询原始记录",
                $"设备时间={deviceTime:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{frameText}");
            _deviceTimeLogged = true;
        }

        private static string FormatTemperature(ushort rawValue)
        {
            if (rawValue == ushort.MaxValue)
                return "-1";

            int showTempRaw = rawValue % 1000;
            decimal showTemp = showTempRaw / 10m;
            return showTemp.ToString("0.0");
        }

        /// <summary>
        /// 湿度按有符号16位显示：0xFFFF→-1，0xFFFE→-2。
        /// </summary>
        private static string FormatHumidity(ushort rawValue)
        {
            return ((short)rawValue).ToString();
        }
    }
}

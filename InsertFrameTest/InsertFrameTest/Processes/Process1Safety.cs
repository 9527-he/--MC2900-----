using System;
using System.Globalization;
using System.Threading;
using InsertFrameTest.Communication;
using InsertFrameTest.Mes;
using InsertFrameTest.Models;

namespace InsertFrameTest.Processes
{
    /// <summary>
    /// 工序1：安规测试
    /// 按长盛安规仪 ASCII 指令流程执行三段自动测试：
    /// 01 绝缘测试 -> 02 交流对地 -> 03 接地电阻
    /// </summary>
    public class Process1Safety : ProcessBase
    {
        private const int StageQueryCount = 8;
        private const int StageDelayMs = 3000;
        protected override bool RequiresProgramFile => false;

        public Process1Safety()
        {
            ProcessNo = 1;
            ProcessName = "安规测试";
        }

        protected override void Execute(string barcode, byte[] programFile, ProcessResult result)
        {
            var bus = EnsureSafetyBusReady(result);
            if (bus == null)
                return;

            try
            {
                SafetyAsciiProtocol.TryResetTwice(bus, Log, IsStopRequested);
                if (IsStopRequested())
                {
                    Log("收到停止请求，工序1提前结束");
                    result.Pass = false;
                    return;
                }

                SafetyAsciiProtocol.SendRemoteMode(bus, Log, IsStopRequested);
                if (IsStopRequested())
                {
                    Log("收到停止请求，工序1提前结束");
                    result.Pass = false;
                    return;
                }

                long insulationStart = ModbusDataLogger.GetLatestSequence();
                var insulation = RunAutoStage(bus, "绝缘测试", "01", StageQueryCount, 101, 102);
                AddMeasuredItem(result, 1, "绝缘电压(KV)", insulation?.LeftDisplayValue, insulation != null);
                AddMeasuredItem(result, 2, "阻值(mΩ)", insulation?.RightDisplayValue, insulation != null);
                CaptureSafetyFrames(
                    18,
                    "工序1-绝缘测试原始记录",
                    $"步骤01 左值={insulation?.LeftDisplayValue ?? "--"} 右值={insulation?.RightDisplayValue ?? "--"}",
                    insulationStart);

                if (!DelayWithStopCheck(StageDelayMs))
                {
                    Log("收到停止请求，工序1提前结束");
                    result.Pass = false;
                    return;
                }
                long acGroundStart = ModbusDataLogger.GetLatestSequence();
                var acGround = RunAutoStage(bus, "交流对地测试", "02", StageQueryCount, 111, 112);
                AddMeasuredItem(result, 3, "交流对地(KV)", acGround?.LeftDisplayValue, acGround != null);
                AddMeasuredItem(result, 4, "漏电流(mA)", acGround?.RightDisplayValue, acGround != null);
                CaptureSafetyFrames(
                    19,
                    "工序1-交流对地测试原始记录",
                    $"步骤02 左值={acGround?.LeftDisplayValue ?? "--"} 右值={acGround?.RightDisplayValue ?? "--"}",
                    acGroundStart);

                if (!DelayWithStopCheck(StageDelayMs))
                {
                    Log("收到停止请求，工序1提前结束");
                    result.Pass = false;
                    return;
                }
                long groundStart = ModbusDataLogger.GetLatestSequence();
                var groundResistance = RunAutoStage(bus, "接地电阻测试", "03", StageQueryCount, 141, 142);
                AddMeasuredItem(result, 5, "接地电流(A)", groundResistance?.LeftDisplayValue, groundResistance != null);
                AddMeasuredItem(result, 6, "电阻(mΩ)", groundResistance?.RightDisplayValue, groundResistance != null);
                CaptureSafetyFrames(
                    20,
                    "工序1-接地电阻测试原始记录",
                    $"步骤03 左值={groundResistance?.LeftDisplayValue ?? "--"} 右值={groundResistance?.RightDisplayValue ?? "--"}",
                    groundStart);

                string stageDetails = BuildRawUploadDetails(insulation, acGround, groundResistance);
                result.ExtraDetails = string.IsNullOrWhiteSpace(result.ExtraDetails)
                    ? stageDetails
                    : result.ExtraDetails + stageDetails;

                string stageSummary = BuildRawUploadSummary(insulation, acGround, groundResistance);
                result.UploadRawData = string.IsNullOrWhiteSpace(result.UploadRawData)
                    ? stageSummary
                    : result.UploadRawData + Environment.NewLine + stageSummary;

                result.Pass = insulation != null && acGround != null && groundResistance != null;
            }
            catch (Exception ex)
            {
                Log($"测试异常: {ex.Message}");
                result.Pass = false;
            }
            finally
            {
                // 用户已点停止时，由界面统一发停止/退出远程，避免与 Pause 抢总线
                if (!IsStopRequested())
                {
                    SafetyAsciiProtocol.TryExitRemote(bus, Log);
                    Log("自动流程完成后已发送一次解除远程命令");
                }
            }
        }

        private void CaptureSafetyFrames(int step, string title, string status, long startSequence)
        {
            var frames = ModbusDataLogger.GetFramesAfter(startSequence, ModbusDataLogger.PortType.Safety);
            string frameText = ModbusDataLogger.FramesToText(frames);
            string raw = $"{status}{Environment.NewLine}{frameText}";
            AppendRuntimeRawRecord(step, title, raw);
        }

        private ModbusRtu EnsureSafetyBusReady(ProcessResult result)
        {
            if (Safety == null)
            {
                Log("错误：安规测试仪未连接");
                result.Pass = false;
                return null;
            }

            var bus = Safety.GetBus();
            if (bus == null)
            {
                Log("错误：安规测试仪串口对象为空");
                result.Pass = false;
                return null;
            }

            if (!bus.Enabled)
            {
                Log("[警告] 安规测试仪通信未启用，已临时启用");
                bus.Enabled = true;
            }

            if (!bus.IsOpen)
            {
                Log("错误：安规测试仪串口未打开");
                result.Pass = false;
                return null;
            }

            return bus;
        }

        private SafetyAsciiProtocol.SafetyAsciiSample RunAutoStage(ModbusRtu bus, string stageName, string expectedStepCode, int queryCount, int leftCode, int rightCode)
        {
            if (IsStopRequested())
                throw new OperationCanceledException("工序1收到停止请求");

            SafetyAsciiProtocol.SendStart(bus, Log);
            var latest = SafetyAsciiProtocol.QueryLatestSample(bus, expectedStepCode, queryCount, Log, sample =>
            {
                OnDataUpdate(leftCode, sample.LeftDisplayValue);
                OnDataUpdate(rightCode, sample.RightDisplayValue);
            }, IsStopRequested);
            Log(stageName + $"完成: 左值={latest.LeftDisplayValue}, 右值={latest.RightDisplayValue}");
            return latest;
        }

        private void AddMeasuredItem(ProcessResult result, int step, string name, string rawValue, bool pass)
        {
            result.Items.Add(new TestItem
            {
                Step = step,
                Name = name,
                MaxValue = 0,
                MinValue = 0,
                Value = ParseDetailValue(rawValue),
                Pass = pass,
                Time = 0,
            });

            Log($"  [{(pass ? "PASS" : "FAIL")}] {name}: {rawValue ?? "--"}");
        }

        private float ParseDetailValue(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                return 0f;

            string text = rawValue.Trim();
            if (text.StartsWith(">", StringComparison.Ordinal) || text.StartsWith("<", StringComparison.Ordinal))
                text = text.Substring(1);

            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                return 0f;

            return value;
        }

        private string BuildRawUploadDetails(SafetyAsciiProtocol.SafetyAsciiSample insulation, SafetyAsciiProtocol.SafetyAsciiSample acGround, SafetyAsciiProtocol.SafetyAsciiSample groundResistance)
        {
            // 仅上传阶段结果摘要，原始 HEX 放在结果文件中，不塞进 MES details。
            return MesClient.BuildDetail(11, 0, 0, 0, insulation != null, "绝缘测试", 0) +
                   MesClient.BuildDetail(12, 0, 0, 0, acGround != null, "交流对地测试", 0) +
                   MesClient.BuildDetail(13, 0, 0, 0, groundResistance != null, "接地电阻测试", 0);
        }

        private string BuildRawUploadSummary(SafetyAsciiProtocol.SafetyAsciiSample insulation, SafetyAsciiProtocol.SafetyAsciiSample acGround, SafetyAsciiProtocol.SafetyAsciiSample groundResistance)
        {
            return "01=" + (insulation?.RawResponseHex ?? string.Empty) + Environment.NewLine +
                   "02=" + (acGround?.RawResponseHex ?? string.Empty) + Environment.NewLine +
                   "03=" + (groundResistance?.RawResponseHex ?? string.Empty);
        }
    }

    internal static class SafetyAsciiProtocol
    {
        public const int ResponseTimeoutMs = 3000;
        public const int QueryIntervalMs = 1000;
        private const int RemoteSettleDelayMs = 300;
        private const int ResetIntervalMs = 1000;
        private const string OkPayload = "+0,\"No error\"";

        public static readonly byte[] CmdRemote = HexToBytes("43 4F 4D 4D 3A 52 45 4D CA 0D 0A");
        public static readonly byte[] CmdLocal = HexToBytes("43 4F 4D 4D 3A 4C 4F 43 C4 0D 0A");
        public static readonly byte[] CmdStart = HexToBytes("53 4F 55 52 3A 54 45 53 54 3A 53 54 41 52 B7 0D 0A");
        public static readonly byte[] CmdReset = HexToBytes("2A 52 53 54 A3 0D 0A");
        public static readonly byte[] CmdStop = HexToBytes("53 4F 55 52 3A 54 45 53 54 3A 53 54 4F 50 C3 0D 0A");
        public static readonly byte[] CmdFetch = HexToBytes("53 4F 55 52 3A 54 45 53 54 3A 46 45 54 43 3F DE 0D 0A");

        public static void SendRemoteMode(ModbusRtu bus, Action<string> log, Func<bool> shouldStop = null)
        {
            if (shouldStop != null && shouldStop())
                return;
            SendAndAssertOk(bus, CmdRemote);
            log?.Invoke("远程模式ok");
            DelayWithStopCheck(RemoteSettleDelayMs, shouldStop);
        }

        public static void SendStart(ModbusRtu bus, Action<string> log)
        {
            SendAndAssertOk(bus, CmdStart);
            log?.Invoke("启动成功");
        }

        public static SafetyAsciiSample QueryLatestSample(ModbusRtu bus, string expectedStepCode, int queryCount, Action<string> log, Action<SafetyAsciiSample> onUpdate, Func<bool> shouldStop = null)
        {
            SafetyAsciiSample latest = null;

            for (int i = 0; i < queryCount; i++)
            {
                if (!DelayWithStopCheck(QueryIntervalMs, shouldStop))
                    break;

                try
                {
                    var sample = ParseFetchResponse(bus.SendRawFrame(CmdFetch, ResponseTimeoutMs));
                    log?.Invoke($"查询{i + 1}/{queryCount}: {sample.Payload}");

                    if (!string.Equals(sample.StepCode, expectedStepCode, StringComparison.Ordinal))
                    {
                        log?.Invoke($"查询{i + 1}/{queryCount}: 收到步骤{sample.StepCode}，期望步骤{expectedStepCode}");
                        continue;
                    }

                    latest = sample;
                    onUpdate?.Invoke(sample);
                    log?.Invoke($"步骤{expectedStepCode}更新: 左值={sample.LeftDisplayValue}, 右值={sample.RightDisplayValue}");
                }
                catch (Exception ex)
                {
                    log?.Invoke($"查询{i + 1}/{queryCount}异常: {ex.Message}");
                }
            }

            if (latest == null)
                throw new InvalidOperationException($"连续{queryCount}次查询中未收到步骤{expectedStepCode}的有效数据");

            return latest;
        }

        public static void TryResetTwice(ModbusRtu bus, Action<string> log, Func<bool> shouldStop = null)
        {
            for (int i = 0; i < 2; i++)
            {
                if (shouldStop != null && shouldStop())
                    break;

                try
                {
                    SendAndAssertOk(bus, CmdReset);
                    log?.Invoke("复位成功");
                }
                catch (Exception ex)
                {
                    log?.Invoke("复位异常: " + ex.Message);
                }

                if (i == 0)
                    DelayWithStopCheck(ResetIntervalMs, shouldStop);
            }
        }

        public static void TryExitRemote(ModbusRtu bus, Action<string> log)
        {
            try
            {
                SendAndAssertOk(bus, CmdLocal);
                log?.Invoke("退出远程成功");
            }
            catch (Exception ex)
            {
                log?.Invoke("退出远程异常: " + ex.Message);
            }
        }

        public static void TryStopAndExitRemote(ModbusRtu bus, Action<string> log)
        {
            if (bus == null)
                return;

            try
            {
                SendAndAssertOk(bus, CmdStop);
                log?.Invoke("停止成功");
            }
            catch (Exception ex)
            {
                log?.Invoke("停止异常: " + ex.Message);
            }

            TryExitRemote(bus, log);
        }

        public static SafetyAsciiSample ParseFetchResponse(byte[] response)
        {
            string payload = ExtractPayload(response);
            string[] fields = payload.Split(',');
            if (fields.Length < 5)
                throw new FormatException("查询响应字段不足: " + payload);

            string stepCode = fields[0].Trim();
            string leftValue;
            string rightValue;

            switch (stepCode)
            {
                case "01":
                    leftValue = GetField(fields, 2, payload);
                    rightValue = GetField(fields, 4, payload);
                    break;
                case "02":
                case "04":
                case "05":
                    leftValue = GetField(fields, 2, payload);
                    rightValue = GetField(fields, 4, payload);
                    break;
                case "03":
                    leftValue = GetField(fields, 2, payload);
                    rightValue = GetField(fields, 3, payload);
                    break;
                default:
                    throw new FormatException("未知步骤响应: " + payload);
            }

            return new SafetyAsciiSample(stepCode, payload, leftValue, rightValue, ToHex(response));
        }

        public static string ExtractPayload(byte[] response)
        {
            if (response == null || response.Length < 4)
                throw new FormatException("响应数据长度不足");

            int length = response.Length;
            if (response[length - 2] != 0x0D || response[length - 1] != 0x0A)
                throw new FormatException("响应未以CRLF结束");

            int payloadLength = length - 3;
            if (payloadLength <= 0)
                throw new FormatException("响应载荷为空");

            return System.Text.Encoding.ASCII.GetString(response, 0, payloadLength).Trim();
        }

        private static void SendAndAssertOk(ModbusRtu bus, byte[] command)
        {
            string payload = ExtractPayload(bus.SendRawFrame(command, ResponseTimeoutMs));
            if (!string.Equals(payload, OkPayload, StringComparison.Ordinal))
                throw new InvalidOperationException("响应异常: " + payload);
        }

        private static string GetField(string[] fields, int index, string payload)
        {
            if (index < 0 || index >= fields.Length)
                throw new FormatException("查询响应字段不足: " + payload);

            return fields[index].Trim();
        }

        private static byte[] HexToBytes(string hex)
        {
            string[] parts = hex.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            byte[] bytes = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                bytes[i] = byte.Parse(parts[i], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);

            return bytes;
        }

        private static string ToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;

            return BitConverter.ToString(bytes).Replace("-", " ");
        }

        private static bool DelayWithStopCheck(int milliseconds, Func<bool> shouldStop)
        {
            if (milliseconds <= 0)
                return shouldStop == null || !shouldStop();

            int remain = milliseconds;
            while (remain > 0)
            {
                if (shouldStop != null && shouldStop())
                    return false;

                int slice = remain < 50 ? remain : 50;
                Thread.Sleep(slice);
                remain -= slice;
            }

            return shouldStop == null || !shouldStop();
        }

        internal sealed class SafetyAsciiSample
        {
            public SafetyAsciiSample(string stepCode, string payload, string leftDisplayValue, string rightDisplayValue, string rawResponseHex)
            {
                StepCode = stepCode;
                Payload = payload;
                LeftDisplayValue = leftDisplayValue;
                RightDisplayValue = rightDisplayValue;
                RawResponseHex = rawResponseHex;
            }

            public string StepCode { get; }
            public string Payload { get; }
            public string LeftDisplayValue { get; }
            public string RightDisplayValue { get; }
            public string RawResponseHex { get; }
        }
    }
}

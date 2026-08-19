using System;
using System.Collections.Generic;
using System.Threading;
using InsertFrameTest.Communication;
using InsertFrameTest.Models;

namespace InsertFrameTest.Processes
{
    // 当前挂接到工序3页签的告警测试流程
    public class Process3Alarm : ProcessBase
    {
        // 组间间隔：给设备处理时间，降低 0x04 从机故障概率
        private const int CycleSettleMs = 120;
        private const int SoftRetryDelayMs = 80;
        private const int DeviceFaultBackoffMs = 300;
        private const int SpdAlarmBaseCode = 201;      // 201~202: AC/DC SPD（0x0565 / 0x0566）
        private const int BatFuseBaseCode = 240;       // 240~247: BAT1~8（0x001C×2 + 0x10AF×6）
        private const int LoadFuseBaseCode = 250;      // 250~253: LLVD1~4（0x0002 数量4）
        private const int DiAlarmBaseCode = 210;
        private const int DoAlarmBaseCode = 220;
        private const int LvdAlarmBaseCode = 230;
        private const int BlvdAlarmCode = 234;
        private const int LvdStateBaseCode = 330;
        private const int BlvdStateCode = 334;
        private const string UnknownState = "unknown";
        // 主告警分项：0=AC SPD, 1=DC SPD, 2=负载熔丝(任一路), 3=蓄电池熔丝(任一路)
        private readonly bool[] _mainAlarmRawLogged = new bool[4];
        private readonly bool[] _batFuseRawLogged = new bool[8];
        private readonly bool[] _loadFuseRawLogged = new bool[4];
        private readonly bool[] _diAlarmRawLogged = new bool[8];
        private readonly bool[] _lvdBlvdAlarmRawLogged = new bool[5];
        // 告警落盘延后到本轮通信完成后，避免 Excel IO 拖慢检测
        private readonly List<PendingMesCapture> _pendingMesCaptures = new List<PendingMesCapture>();
        protected override bool RequiresProgramFile => false;

        private struct PendingMesCapture
        {
            public string AlarmName;
            public string StatusSummary;
            public string FrameText;
        }

        public Process3Alarm()
        {
            ProcessNo   = 3;
            ProcessName = "告警测试";
        }

        protected override void Execute(string barcode, byte[] programFile, ProcessResult result)
        {
            if (MC2900 == null)
                throw new InvalidOperationException("MC2900 未初始化，无法执行告警测试。");

            var bus = MC2900.GetBus();
            if (bus == null || !bus.IsOpen)
                throw new InvalidOperationException("MC2900 通信未连接，无法执行告警测试。");

            ResetIndicators();
            Log("工序3告警测试开始：按协议分项查询+分组轮询(DI/SPD优先)，遇从机故障自动退避重试");

            bool diReadPassed = false;
            bool spdReadPassed = false;
            bool loadFuseReadPassed = false;
            bool batFuseReadPassed = false;
            bool doReadPassed = false;
            bool lvdBlvdAlarmReadPassed = false;
            int consecutiveCommErrors = 0;
            int rotatePhase = 0;
            Array.Clear(_mainAlarmRawLogged, 0, _mainAlarmRawLogged.Length);
            Array.Clear(_batFuseRawLogged, 0, _batFuseRawLogged.Length);
            Array.Clear(_loadFuseRawLogged, 0, _loadFuseRawLogged.Length);
            Array.Clear(_diAlarmRawLogged, 0, _diAlarmRawLogged.Length);
            Array.Clear(_lvdBlvdAlarmRawLogged, 0, _lvdBlvdAlarmRawLogged.Length);
            _pendingMesCaptures.Clear();

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

                    // ── 每轮必检：DI + AC/DC SPD（分项 FC01，与协议报文一致）──
                    long diSpdStart = ModbusDataLogger.GetLatestSequence();
                    bool[] diAlarms;
                    bool acSpdAlarm;
                    bool dcSpdAlarm;
                    ReadDiAndSpdWithRetry(out diAlarms, out acSpdAlarm, out dcSpdAlarm);

                    PublishDiStates(diAlarms);
                    PublishSpdAlarmStates(acSpdAlarm, dcSpdAlarm);
                    QueueDiAlarmRawRecords(diAlarms, diSpdStart);
                    string acSpdFrameText = null;
                    string dcSpdFrameText = null;
                    QueueCaptureMainAlarm(0, "AC SPD 告警", acSpdAlarm, diSpdStart, ref acSpdFrameText);
                    QueueCaptureMainAlarm(1, "DC SPD 告警", dcSpdAlarm, diSpdStart, ref dcSpdFrameText);
                    diReadPassed = true;
                    spdReadPassed = true;

                    if (MC2900.IsQueryPollingSuspended)
                    {
                        FlushPendingMesCaptures();
                        if (!DelayWithStopCheck(20))
                            break;
                        continue;
                    }

                    // ── 其余项稳定轮转：负载熔丝+LVD / BAT / DO ──
                    int phase = rotatePhase % 3;
                    rotatePhase++;

                    if (phase == 0)
                    {
                        // 负载熔丝 + LVD/BLVD（分项查询）
                        long loadLvdStart = ModbusDataLogger.GetLatestSequence();
                        bool[] loadFuseAlarms;
                        bool[] lvdBlvdAlarms;
                        ReadLoadAndLvdWithRetry(out loadFuseAlarms, out lvdBlvdAlarms);

                        PublishLoadFuseStates(loadFuseAlarms);
                        PublishLvdBlvdAlarmStates(lvdBlvdAlarms);
                        PublishLvdBlvdCurrentStates(lvdBlvdAlarms);

                        QueueLoadFuseRawRecords(loadFuseAlarms, loadLvdStart);
                        QueueLvdBlvdRawRecords(lvdBlvdAlarms, loadLvdStart);
                        bool anyLoadFuseAlarm = HasAnyAlarm(loadFuseAlarms);
                        string loadFuseFrameText = null;
                        QueueCaptureMainAlarm(2, "负载熔丝告警(LLVD1-4)", anyLoadFuseAlarm, loadLvdStart, ref loadFuseFrameText);

                        loadFuseReadPassed = true;
                        lvdBlvdAlarmReadPassed = true;
                    }
                    else if (phase == 1)
                    {
                        long batFuseStart = ModbusDataLogger.GetLatestSequence();
                        bool[] batFuseAlarms = ReadBatFuseWithRetry();

                        PublishBatFuseStates(batFuseAlarms);
                        QueueBatFuseRawRecords(batFuseAlarms, batFuseStart);
                        bool anyBatFuseAlarm = HasAnyAlarm(batFuseAlarms);
                        string batFuseFrameText = null;
                        QueueCaptureMainAlarm(3, "蓄电池熔丝告警(BAT1-8)", anyBatFuseAlarm, batFuseStart, ref batFuseFrameText);
                        batFuseReadPassed = true;
                    }
                    else
                    {
                        // DO：FC03 0x1937×8；设备忙/故障时软退避，不中断整轮
                        try
                        {
                            bool[] doStates = ReadDoWithRetry();
                            PublishDoStates(doStates);
                            PublishDoCurrentStates(doStates);
                            doReadPassed = true;
                        }
                        catch (ModbusSlaveException mex) when (mex.IsTransientDeviceFault)
                        {
                            Log($"DO状态读取遇从机故障(0x{mex.ErrorCode:X2})，本轮跳过并退避: {mex.Message}");
                            if (!DelayWithStopCheck(DeviceFaultBackoffMs))
                                break;
                        }
                    }

                    bool mainAlarmReadPassed = spdReadPassed && loadFuseReadPassed && batFuseReadPassed;
                    FlushPendingMesCaptures();

                    result.Pass = diReadPassed && mainAlarmReadPassed && doReadPassed && lvdBlvdAlarmReadPassed;
                    consecutiveCommErrors = 0;

                    if (!DelayWithStopCheck(CycleSettleMs))
                        break;
                }
                catch (OperationCanceledException)
                {
                    FlushPendingMesCaptures();
                    if (!bus.IsOpen || !bus.Enabled || bus.Paused)
                    {
                        Log("告警测试通信已停止");
                        break;
                    }

                    if (!DelayWithStopCheck(20))
                        break;
                }
                catch (ModbusSlaveException mex) when (mex.IsTransientDeviceFault)
                {
                    FlushPendingMesCaptures();
                    consecutiveCommErrors++;
                    Log($"告警轮询从机故障({consecutiveCommErrors}): 功能码=0x{mex.ExceptionFunctionCode:X2}, 错误码=0x{mex.ErrorCode:X2}，继续尝试通信");
                    if (!DelayWithStopCheck(DeviceFaultBackoffMs))
                        break;
                }
                catch (ModbusCrcException cex)
                {
                    FlushPendingMesCaptures();
                    consecutiveCommErrors++;
                    Log($"告警轮询CRC错误({consecutiveCommErrors}): {cex.Message}，继续尝试通信");
                    if (!DelayWithStopCheck(DeviceFaultBackoffMs))
                        break;
                }
                catch (Exception ex)
                {
                    FlushPendingMesCaptures();
                    consecutiveCommErrors++;
                    Log($"告警轮询异常({consecutiveCommErrors}): {ex.Message}，继续尝试通信");
                    if (!DelayWithStopCheck(CycleSettleMs))
                        break;
                }
            }

            FlushPendingMesCaptures();

            AddBoolItem(result, 1, "DI口告警读取", diReadPassed);
            AddBoolItem(result, 2, "防雷器/蓄电池熔丝/负载熔丝告警读取", spdReadPassed && loadFuseReadPassed && batFuseReadPassed);
            AddBoolItem(result, 3, "DO口状态读取", doReadPassed);
            AddBoolItem(result, 4, "LLVD/BLVD告警读取", lvdBlvdAlarmReadPassed);
            result.Pass = diReadPassed && spdReadPassed && loadFuseReadPassed && batFuseReadPassed && doReadPassed && lvdBlvdAlarmReadPassed;
        }

        private void ReadDiAndSpdWithRetry(out bool[] diAlarms, out bool acSpdAlarm, out bool dcSpdAlarm)
        {
            try
            {
                MC2900.ReadProcess3DiAndSpdAlarms(out diAlarms, out acSpdAlarm, out dcSpdAlarm);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                if (!DelayWithStopCheck(SoftRetryDelayMs))
                    throw new OperationCanceledException();
                MC2900.ReadProcess3DiAndSpdAlarms(out diAlarms, out acSpdAlarm, out dcSpdAlarm);
            }
        }

        private void ReadLoadAndLvdWithRetry(out bool[] loadFuseAlarms, out bool[] lvdBlvdAlarms)
        {
            try
            {
                MC2900.ReadProcess3LoadFuseAndLvdBlvdAlarms(out loadFuseAlarms, out lvdBlvdAlarms);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                if (!DelayWithStopCheck(SoftRetryDelayMs))
                    throw new OperationCanceledException();
                MC2900.ReadProcess3LoadFuseAndLvdBlvdAlarms(out loadFuseAlarms, out lvdBlvdAlarms);
            }
        }

        private bool[] ReadBatFuseWithRetry()
        {
            try
            {
                return MC2900.ReadProcess3BatFuseAlarms();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                if (!DelayWithStopCheck(SoftRetryDelayMs))
                    throw new OperationCanceledException();
                return MC2900.ReadProcess3BatFuseAlarms();
            }
        }

        private bool[] ReadDoWithRetry()
        {
            try
            {
                return MC2900.ReadProcess3DoStates();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                if (!DelayWithStopCheck(SoftRetryDelayMs))
                    throw new OperationCanceledException();
                return MC2900.ReadProcess3DoStates();
            }
        }

        private void ResetIndicators()
        {
            PublishMainAlarmPlaceholders();

            for (int i = 0; i < 8; i++)
            {
                OnDataUpdate(DiAlarmBaseCode + i, UnknownState);
                OnDataUpdate(DoAlarmBaseCode + i, bool.FalseString);
                OnDataUpdate(320 + i, bool.FalseString);
            }

            for (int i = 0; i < 4; i++)
                OnDataUpdate(LvdAlarmBaseCode + i, UnknownState);

            OnDataUpdate(BlvdAlarmCode, UnknownState);
        }

        private void PublishMainAlarmPlaceholders()
        {
            for (int i = 0; i < 2; i++)
                OnDataUpdate(SpdAlarmBaseCode + i, UnknownState);
            for (int i = 0; i < 8; i++)
                OnDataUpdate(BatFuseBaseCode + i, UnknownState);
            for (int i = 0; i < 4; i++)
                OnDataUpdate(LoadFuseBaseCode + i, UnknownState);
        }

        private void PublishSpdAlarmStates(bool acSpdAlarm, bool dcSpdAlarm)
        {
            OnDataUpdate(SpdAlarmBaseCode + 0, acSpdAlarm.ToString());
            OnDataUpdate(SpdAlarmBaseCode + 1, dcSpdAlarm.ToString());
        }

        private void PublishBatFuseStates(bool[] batFuseAlarms)
        {
            if (batFuseAlarms == null || batFuseAlarms.Length < 8)
                throw new InvalidOperationException("蓄电池熔丝告警线圈返回数量不足，无法解析BAT1-8状态。");

            for (int i = 0; i < 8; i++)
                OnDataUpdate(BatFuseBaseCode + i, batFuseAlarms[i].ToString());
        }

        private void PublishLoadFuseStates(bool[] loadFuseAlarms)
        {
            if (loadFuseAlarms == null || loadFuseAlarms.Length < 4)
                throw new InvalidOperationException("负载熔丝告警线圈返回数量不足，无法解析LLVD1-4状态。");

            for (int i = 0; i < 4; i++)
                OnDataUpdate(LoadFuseBaseCode + i, loadFuseAlarms[i].ToString());
        }

        private void PublishDiStates(bool[] alarms)
        {
            if (alarms == null || alarms.Length < 8)
                throw new InvalidOperationException("工序3 DI告警线圈返回数量不足，无法解析DI1-DI8状态。");

            for (int i = 0; i < 8; i++)
                OnDataUpdate(DiAlarmBaseCode + i, alarms[i].ToString());
        }

        private void PublishDoStates(bool[] doStates)
        {
            if (doStates == null || doStates.Length < 8)
                throw new InvalidOperationException("DO状态返回数量不足，无法更新DO口测试显示。");

            for (int i = 0; i < 8; i++)
                OnDataUpdate(DoAlarmBaseCode + i, doStates[i].ToString());
        }

        private void PublishDoCurrentStates(bool[] doStates)
        {
            if (doStates == null || doStates.Length < 8)
                throw new InvalidOperationException("DO状态返回数量不足，无法更新DO当前状态显示。");

            for (int i = 0; i < 8; i++)
                OnDataUpdate(320 + i, doStates[i].ToString());
        }

        private void PublishLvdBlvdAlarmStates(bool[] alarms)
        {
            if (alarms == null || alarms.Length < 5)
                throw new InvalidOperationException("LLVD/BLVD告警线圈返回数量不足，无法更新告警灯显示。");

            for (int i = 0; i < 4; i++)
                OnDataUpdate(LvdAlarmBaseCode + i, alarms[i].ToString());

            OnDataUpdate(BlvdAlarmCode, alarms[4].ToString());
        }

        private void PublishLvdBlvdCurrentStates(bool[] states)
        {
            if (states == null || states.Length < 5)
                throw new InvalidOperationException("LLVD/BLVD状态线圈返回数量不足，无法更新当前状态显示。");

            for (int i = 0; i < 4; i++)
                OnDataUpdate(LvdStateBaseCode + i, states[i].ToString());

            OnDataUpdate(BlvdStateCode, states[4].ToString());
        }

        private void QueuePendingMes(string alarmName, string statusSummary, string frameText)
        {
            _pendingMesCaptures.Add(new PendingMesCapture
            {
                AlarmName = alarmName,
                StatusSummary = statusSummary,
                FrameText = frameText
            });
        }

        private void FlushPendingMesCaptures()
        {
            if (_pendingMesCaptures.Count == 0)
                return;

            // 停止中不再写 Excel，避免“停止中”长时间卡住
            if (IsStopRequested())
            {
                _pendingMesCaptures.Clear();
                return;
            }

            for (int i = 0; i < _pendingMesCaptures.Count; i++)
            {
                var item = _pendingMesCaptures[i];
                AppendAlarmCaptureToMes(item.AlarmName, item.StatusSummary, item.FrameText);
            }
            _pendingMesCaptures.Clear();
        }

        private void QueueDiAlarmRawRecords(bool[] alarms, long startSequence)
        {
            if (alarms == null)
                return;

            string frameText = null;
            for (int i = 0; i < 8 && i < alarms.Length; i++)
            {
                if (_diAlarmRawLogged[i] || !alarms[i])
                    continue;

                frameText = frameText ?? ModbusDataLogger.FramesToText(ModbusDataLogger.GetFramesAfter(startSequence, ModbusDataLogger.PortType.MC2900));
                string status = $"DI{i + 1}=True(告警红灯)";
                string raw = $"{status}{Environment.NewLine}{frameText}";
                AppendRuntimeRawRecord(31 + i, $"工序3-DI{i + 1}告警触发原始记录", raw);
                QueuePendingMes($"DI{i + 1}告警", status, frameText);
                _diAlarmRawLogged[i] = true;
            }
        }

        private void QueueLoadFuseRawRecords(bool[] alarms, long startSequence)
        {
            if (alarms == null)
                return;

            string frameText = null;
            for (int i = 0; i < 4 && i < alarms.Length; i++)
            {
                if (_loadFuseRawLogged[i] || !alarms[i])
                    continue;

                frameText = frameText ?? ModbusDataLogger.FramesToText(ModbusDataLogger.GetFramesAfter(startSequence, ModbusDataLogger.PortType.MC2900));
                string name = $"LLVD{i + 1}";
                string status = $"{name}=True(告警红灯)";
                string raw = $"{status}{Environment.NewLine}{frameText}";
                AppendRuntimeRawRecord(50 + i, $"工序3-{name}告警触发原始记录", raw);
                QueuePendingMes($"{name}告警", status, frameText);
                _loadFuseRawLogged[i] = true;
            }
        }

        private void QueueBatFuseRawRecords(bool[] alarms, long startSequence)
        {
            if (alarms == null)
                return;

            string frameText = null;
            for (int i = 0; i < 8 && i < alarms.Length; i++)
            {
                if (_batFuseRawLogged[i] || !alarms[i])
                    continue;

                frameText = frameText ?? ModbusDataLogger.FramesToText(ModbusDataLogger.GetFramesAfter(startSequence, ModbusDataLogger.PortType.MC2900));
                string name = $"BAT{i + 1}";
                string status = $"{name}=True(告警红灯)";
                string raw = $"{status}{Environment.NewLine}{frameText}";
                AppendRuntimeRawRecord(60 + i, $"工序3-{name}告警触发原始记录", raw);
                QueuePendingMes($"{name}告警", status, frameText);
                _batFuseRawLogged[i] = true;
            }
        }

        private static bool HasAnyAlarm(bool[] alarms)
        {
            if (alarms == null)
                return false;
            for (int i = 0; i < alarms.Length; i++)
            {
                if (alarms[i])
                    return true;
            }
            return false;
        }

        private void QueueCaptureMainAlarm(int index, string name, bool isAlarm, long startSequence, ref string frameText)
        {
            if (_mainAlarmRawLogged[index] || !isAlarm)
                return;

            frameText = frameText ?? ModbusDataLogger.FramesToText(ModbusDataLogger.GetFramesAfter(startSequence, ModbusDataLogger.PortType.MC2900));
            string status = $"{name}=True(告警红灯)";
            string raw = $"{status}{Environment.NewLine}{frameText}";
            AppendRuntimeRawRecord(30 + index, $"工序3-{name}触发原始记录", raw);
            QueuePendingMes(name, status, frameText);
            _mainAlarmRawLogged[index] = true;
        }

        private void QueueLvdBlvdRawRecords(bool[] alarms, long startSequence)
        {
            if (alarms == null)
                return;

            string frameText = null;
            for (int i = 0; i < 5 && i < alarms.Length; i++)
            {
                if (_lvdBlvdAlarmRawLogged[i] || !alarms[i])
                    continue;

                frameText = frameText ?? ModbusDataLogger.FramesToText(ModbusDataLogger.GetFramesAfter(startSequence, ModbusDataLogger.PortType.MC2900));
                string name = i < 4 ? $"LVD{i + 1}" : "BLVD";
                string status = $"{name}=True(告警红灯)";
                string raw = $"{status}{Environment.NewLine}{frameText}";
                AppendRuntimeRawRecord(40 + i, $"工序3-{name}告警触发原始记录", raw);
                QueuePendingMes($"{name}告警", status, frameText);
                _lvdBlvdAlarmRawLogged[i] = true;
            }
        }
    }
}

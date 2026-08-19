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
        private const int PollIntervalMs = 500;
        private const int MainAlarmBaseCode = 201;
        private const int DiAlarmBaseCode = 210;
        private const int DoAlarmBaseCode = 220;
        private const int LvdAlarmBaseCode = 230;
        private const int BlvdAlarmCode = 234;
        private const int LvdStateBaseCode = 330;
        private const int BlvdStateCode = 334;
        private const string UnknownState = "unknown";
        // 主告警分项：0=防雷器故障, 1=蓄电池熔丝断, 2=负载熔丝断
        private readonly bool[] _mainAlarmRawLogged = new bool[3];
        private readonly bool[] _diAlarmRawLogged = new bool[8];
        private readonly bool[] _lvdBlvdAlarmRawLogged = new bool[5];
        protected override bool RequiresProgramFile => false;

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
            Log("工序3告警测试开始：通过FC01持续读取0x055D~0x0568告警位并更新告警灯");

            bool diReadPassed = false;
            bool mainAlarmReadPassed = false;
            bool doReadPassed = false;
            bool lvdBlvdAlarmReadPassed = false;
            Array.Clear(_mainAlarmRawLogged, 0, _mainAlarmRawLogged.Length);
            Array.Clear(_diAlarmRawLogged, 0, _diAlarmRawLogged.Length);
            Array.Clear(_lvdBlvdAlarmRawLogged, 0, _lvdBlvdAlarmRawLogged.Length);

            while (bus.IsOpen && bus.Enabled && !bus.Paused)
            {
                try
                {
                    if (MC2900.IsQueryPollingSuspended)
                    {
                        Thread.Sleep(20);
                        continue;
                    }

                    long alarmStart = ModbusDataLogger.GetLatestSequence();
                    bool[] alarms = MC2900.ReadProcess3AlarmCoils();
                    TryLogAlarmRawRecords(alarms, alarmStart);

                    if (MC2900.IsQueryPollingSuspended)
                    {
                        Thread.Sleep(20);
                        continue;
                    }

                    bool[] doStates = MC2900.ReadProcess3DoStates();

                    if (MC2900.IsQueryPollingSuspended)
                    {
                        Thread.Sleep(20);
                        continue;
                    }

                    long lvdBlvdStart = ModbusDataLogger.GetLatestSequence();
                    bool[] lvdBlvdAlarms = MC2900.ReadProcess3LvdBlvdAlarmCoils();
                    TryLogLvdBlvdRawRecords(lvdBlvdAlarms, lvdBlvdStart);

                    if (MC2900.IsQueryPollingSuspended)
                    {
                        Thread.Sleep(20);
                        continue;
                    }

                    PublishMainAlarmStates(alarms);
                    PublishDiStates(alarms);
                    PublishDoStates(doStates);
                    PublishDoCurrentStates(doStates);
                    PublishLvdBlvdAlarmStates(lvdBlvdAlarms);
                    PublishLvdBlvdCurrentStates(lvdBlvdAlarms);

                    diReadPassed = true;
                    mainAlarmReadPassed = true;
                    doReadPassed = true;
                    lvdBlvdAlarmReadPassed = true;
                    result.Pass = diReadPassed && mainAlarmReadPassed && doReadPassed && lvdBlvdAlarmReadPassed;

                    Thread.Sleep(PollIntervalMs);
                }
                catch (OperationCanceledException)
                {
                    if (!bus.IsOpen || !bus.Enabled || bus.Paused)
                    {
                        Log("告警测试通信已停止");
                        break;
                    }

                    Thread.Sleep(20);
                }
                catch (Exception ex)
                {
                    Log($"告警轮询异常，下一周期继续查询: {ex.Message}");
                    Thread.Sleep(PollIntervalMs);
                }
            }

            AddBoolItem(result, 1, "DI口告警读取", diReadPassed);
            AddBoolItem(result, 2, "防雷/蓄电池熔丝/负载熔丝告警读取", mainAlarmReadPassed);
            AddBoolItem(result, 3, "DO口状态读取", doReadPassed);
            AddBoolItem(result, 4, "LLVD/BLVD告警读取", lvdBlvdAlarmReadPassed);
            result.Pass = diReadPassed && mainAlarmReadPassed && doReadPassed && lvdBlvdAlarmReadPassed;
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
            for (int i = 0; i < 3; i++)
                OnDataUpdate(MainAlarmBaseCode + i, UnknownState);
        }

        private void PublishMainAlarmStates(bool[] alarms)
        {
            if (alarms == null || alarms.Length < 11)
                throw new InvalidOperationException("工序3告警线圈返回数量不足，无法解析总告警状态。");

            OnDataUpdate(MainAlarmBaseCode + 0, alarms[9].ToString());
            OnDataUpdate(MainAlarmBaseCode + 1, alarms[8].ToString());
            OnDataUpdate(MainAlarmBaseCode + 2, alarms[10].ToString());
        }

        private void PublishDiStates(bool[] alarms)
        {
            if (alarms == null || alarms.Length < 8)
                throw new InvalidOperationException("工序3告警线圈返回数量不足，无法解析DI1-DI8状态。");

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

        private void TryLogAlarmRawRecords(bool[] alarms, long startSequence)
        {
            if (alarms == null)
                return;

            string frameText = null;

            // 主告警分项捕捉：指示灯变红(True)时写入原始记录 + MES上传汇总
            // alarms[9]=防雷器故障, alarms[8]=蓄电池熔丝断, alarms[10]=负载熔丝断
            if (alarms.Length >= 11)
            {
                TryCaptureMainAlarm(0, "防雷器故障告警", alarms[9], startSequence, ref frameText);
                TryCaptureMainAlarm(1, "蓄电池熔丝断告警", alarms[8], startSequence, ref frameText);
                TryCaptureMainAlarm(2, "负载熔丝断告警", alarms[10], startSequence, ref frameText);
            }

            for (int i = 0; i < 8 && i < alarms.Length; i++)
            {
                if (_diAlarmRawLogged[i] || !alarms[i])
                    continue;

                frameText = frameText ?? ModbusDataLogger.FramesToText(ModbusDataLogger.GetFramesAfter(startSequence, ModbusDataLogger.PortType.MC2900));
                string status = $"DI{i + 1}=True(告警红灯)";
                string raw = $"{status}{Environment.NewLine}{frameText}";
                AppendRuntimeRawRecord(31 + i, $"工序3-DI{i + 1}告警触发原始记录", raw);
                AppendAlarmCaptureToMes($"DI{i + 1}告警", status, frameText);
                _diAlarmRawLogged[i] = true;
            }
        }

        private void TryCaptureMainAlarm(int index, string name, bool isAlarm, long startSequence, ref string frameText)
        {
            if (_mainAlarmRawLogged[index] || !isAlarm)
                return;

            frameText = frameText ?? ModbusDataLogger.FramesToText(ModbusDataLogger.GetFramesAfter(startSequence, ModbusDataLogger.PortType.MC2900));
            string status = $"{name}=True(告警红灯)";
            string raw = $"{status}{Environment.NewLine}{frameText}";
            AppendRuntimeRawRecord(30 + index, $"工序3-{name}触发原始记录", raw);
            AppendAlarmCaptureToMes(name, status, frameText);
            _mainAlarmRawLogged[index] = true;
        }

        private void TryLogLvdBlvdRawRecords(bool[] alarms, long startSequence)
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
                AppendAlarmCaptureToMes($"{name}告警", status, frameText);
                _lvdBlvdAlarmRawLogged[i] = true;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;

namespace InsertFrameTest.Communication
{
    // 麦米MC2900监控模块 Modbus RTU驱动
    public class MC2900Driver
    {
        private readonly ModbusRtu _bus;
        private readonly byte _addr;
        private int _querySuspendCount;

        public MC2900Driver(ModbusRtu bus, byte addr = 1)
        {
            _bus = bus;
            _addr = addr;
        }

        /// <summary>
        /// 获取Modbus总线实例
        /// </summary>
        public ModbusRtu GetBus()
        {
            return _bus;
        }

        public bool IsQueryPollingSuspended => Volatile.Read(ref _querySuspendCount) > 0;

        public IDisposable SuspendQueryPolling()
        {
            Interlocked.Increment(ref _querySuspendCount);
            return new QueryPollingScope(this);
        }

        private void ResumeQueryPolling()
        {
            if (Interlocked.Decrement(ref _querySuspendCount) < 0)
                Interlocked.Exchange(ref _querySuspendCount, 0);
        }

        private sealed class QueryPollingScope : IDisposable
        {
            private MC2900Driver _owner;

            public QueryPollingScope(MC2900Driver owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                var owner = Interlocked.Exchange(ref _owner, null);
                if (owner != null)
                    owner.ResumeQueryPolling();
            }
        }

        // ── 基础通信测试 ──────────────────────────────────────────
        public bool Ping()
        {
            try { _bus.ReadHoldingRegisters(_addr, 0, 1); return true; }
            catch { return false; }
        }

        // ── 工序2: 时间校准 ───────────────────────────────────────
        // 寄存器 0x190C~0x1911 (6412~6417): 年月日时分秒
        public void SetSystemTime(DateTime t)
        {
            ushort startReg = 0x190C;

            try
            {
                ushort[] values = new ushort[]
                {
                    (ushort)t.Year, (ushort)t.Month,  (ushort)t.Day,
                    (ushort)t.Hour, (ushort)t.Minute, (ushort)t.Second
                };
                
                ModbusDataLogger.LogInfo($"[MC2900] 设置时间: {t:yyyy-MM-dd HH:mm:ss} -> 优先使用FC10写入寄存器0x190C~0x1911: [{string.Join(",", values)}]");

                try
                {
                    _bus.WriteMultipleRegisters(_addr, startReg, values);
                }
                catch (Exception ex) when (IsIllegalAddressOnWriteMultiple(ex))
                {
                    ModbusDataLogger.LogInfo("[MC2900] FC10写时间寄存器被设备拒绝，回退为逐寄存器FC06写入");

                    for (int i = 0; i < values.Length; i++)
                    {
                        ushort reg = (ushort)(startReg + i);
                        _bus.WriteSingleRegister(_addr, reg, values[i]);
                    }
                }
            }
            catch (Exception ex)
            {
                ModbusDataLogger.LogInfo($"[MC2900] SetSystemTime异常: {ex.Message}");
                throw;
            }
        }

        public DateTime SyncSystemTime(int maxAllowedDiffSeconds = 2, int maxAttempts = 2)
        {
            if (maxAllowedDiffSeconds < 0)
                maxAllowedDiffSeconds = 0;

            if (maxAttempts < 1)
                maxAttempts = 1;

            DateTime deviceTime = DateTime.MinValue;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                DateTime targetTime = BuildSyncTargetTime(attempt, deviceTime, maxAllowedDiffSeconds);
                SetSystemTime(targetTime);

                deviceTime = GetSystemTime();
                int diffSeconds = GetTimeDifferenceSeconds(deviceTime, DateTime.Now);
                ModbusDataLogger.LogInfo($"[MC2900] 校时尝试{attempt}/{maxAttempts}: 目标={targetTime:yyyy-MM-dd HH:mm:ss}, 设备={deviceTime:yyyy-MM-dd HH:mm:ss}, 差值={diffSeconds}秒");

                if (diffSeconds <= maxAllowedDiffSeconds)
                    return deviceTime;
            }

            return deviceTime;
        }

        private DateTime BuildSyncTargetTime(int attempt, DateTime lastDeviceTime, int maxAllowedDiffSeconds)
        {
            if (attempt <= 1 || lastDeviceTime == DateTime.MinValue)
                return TrimMilliseconds(DateTime.Now.AddSeconds(1));

            DateTime now = DateTime.Now;
            int diffSeconds = GetTimeDifferenceSeconds(lastDeviceTime, now);
            int compensationSeconds = Math.Max(1, diffSeconds - maxAllowedDiffSeconds + 1);
            compensationSeconds = Math.Min(compensationSeconds, 8);
            return TrimMilliseconds(now.AddSeconds(compensationSeconds));
        }

        private static int GetTimeDifferenceSeconds(DateTime left, DateTime right)
        {
            return (int)Math.Abs(Math.Round((right - left).TotalSeconds, MidpointRounding.AwayFromZero));
        }

        private static DateTime TrimMilliseconds(DateTime value)
        {
            return new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second);
        }

        private static bool IsIllegalAddressOnWriteMultiple(Exception ex)
        {
            if (ex == null || string.IsNullOrEmpty(ex.Message))
                return false;

            return ex.Message.Contains("功能码=0x90") && ex.Message.Contains("错误码=0x02");
        }

        public ushort[] ReadSystemTimeRegisters()
        {
            ModbusDataLogger.LogInfo("[MC2900] 读取时间寄存器: 0x190C起6个寄存器");
            return _bus.ReadHoldingRegisters(_addr, 0x190C, 6);
        }

        public DateTime GetSystemTime()
        {
            try
            {
                var r = ReadSystemTimeRegisters();
                
                int year = r[0];
                if (year < 100) year += 2000;
                
                var result = new DateTime(year, r[1], r[2], r[3], r[4], r[5]);
                ModbusDataLogger.LogInfo($"[MC2900] 读取结果: {result:yyyy-MM-dd HH:mm:ss} (原始数据: [{string.Join(",", r)}])");
                return result;
            }
            catch (Exception ex)
            {
                ModbusDataLogger.LogInfo($"[MC2900] GetSystemTime异常: {ex.Message}");
                throw;
            }
        }

        // ── 工序2: 温度传感器1/2/3、湿度传感器4 ─────────────────
        // 寄存器 0x11F3/0x11F4/0x11F5: 传感器1/2/3温度
        // 寄存器 0x11F6: 传感器4湿度(%RH)，有符号显示（0xFFFF→-1，0xFFFE→-2）
        public ushort[] ReadTemperatureRegisters()
        {
            var result = new ushort[3];
            for (int i = 0; i < result.Length; i++)
            {
                ushort register = (ushort)(0x11F3 + i);
                ModbusDataLogger.LogInfo($"[MC2900] 读取传感器{i + 1}温度寄存器: 0x{register:X4}");
                result[i] = _bus.ReadHoldingRegisters(_addr, register, 1)[0];
            }

            return result;
        }

        public ushort ReadHumidityRegister()
        {
            const ushort register = 0x11F6;
            ModbusDataLogger.LogInfo($"[MC2900] 读取传感器4湿度寄存器: 0x{register:X4}");
            return _bus.ReadHoldingRegisters(_addr, register, 1)[0];
        }

        public void SetSystemManualMode()
        {
            ModbusDataLogger.LogInfo("[MC2900] 设置系统手动模式: FC05写线圈0x0015=0x0000");
            _bus.WriteCoilValue(_addr, 0x0015, 0x0000);
        }

        public void SetSystemStopMode()
        {
            ModbusDataLogger.LogInfo("[MC2900] 停止测试并退出手动模式: FC05写线圈0x0015=0x0001");
            _bus.WriteCoilValue(_addr, 0x0015, 0x0001);
        }

        public void ClearModuleAlarmRecords()
        {
            ModbusDataLogger.LogInfo("[MC2900] 清除模块告警记录: FC05写线圈0x0000=0x0000");
            _bus.WriteCoilValue(_addr, 0x0000, 0x0000);
        }

        public void ClearAllRecords()
        {
            ModbusDataLogger.LogInfo("[MC2900] 清除所有记录: FC05写线圈0x0001=0x0000");
            _bus.WriteCoilValue(_addr, 0x0001, 0x0000);
        }

        public ushort ReadOnlineModuleCount()
        {
            ModbusDataLogger.LogInfo("[MC2900] 读取在线模块数量: 0x11EA起1个寄存器");
            return _bus.ReadHoldingRegisters(_addr, 0x11EA, 1)[0];
        }

        public void SetModulePowerState(int moduleIndex, bool powerOn)
        {
            if (moduleIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(moduleIndex));

            ushort coil = (ushort)(0x0016 + moduleIndex);
            ushort value = powerOn ? (ushort)0x0000 : (ushort)0x0001;
            ModbusDataLogger.LogInfo($"[MC2900] 模块{moduleIndex + 1}电源控制: FC05写线圈0x{coil:X4}=0x{value:X4}");
            _bus.WriteCoilValue(_addr, coil, value);
        }

        // ── 工序3: 模块功能测试 ───────────────────────────────────
        // 在线模块数量（4572）
        public ushort GetOnlineModuleCount()
        {
            return _bus.ReadHoldingRegisters(_addr, 4572, 1)[0];
        }

        // 读模块在线状态（x=模块索引0~39）
        // 寄存器 2731+(x*20): 0=在线，1=离线
        public bool IsModuleOnline(int x)
        {
            ushort reg = (ushort)(2731 + x * 20);
            return _bus.ReadHoldingRegisters(_addr, reg, 1)[0] == 0;
        }

        // 读模块输出电流 2732+(x*20)
        public float GetModuleOutputCurrent(int x)
        {
            ushort reg = (ushort)(2732 + x * 20);
            return _bus.ReadHoldingRegisters(_addr, reg, 1)[0] * 0.01f;
        }

        // ── 工序4: 告警测试 (DI状态) ─────────────────────────────
        // 寄存器 4603~4614: DI1~DI12 状态 (0=断开, 1=闭合)
        public bool[] ReadDIStatus()
        {
            var r = _bus.ReadHoldingRegisters(_addr, 4603, 12);
            var result = new bool[12];
            for (int i = 0; i < 12; i++) result[i] = r[i] == 1;
            return result;
        }

        // 寄存器 1373~1384: DI1~DI12 告警 (0=无告警, 1=有告警)
        public bool[] ReadDIAlarmStatus()
        {
            var registers = _bus.ReadHoldingRegisters(_addr, 1373, 12);
            var result = new bool[12];
            for (int i = 0; i < 12; i++)
                result[i] = registers[i] == 1;
            return result;
        }

        // 线圈 14~18: BLVD / LVD1~LVD4 控制状态 (false=ON, true=OFF)
        public bool[] ReadLvdBlvdControlOffStatus()
        {
            return _bus.ReadCoils(_addr, 14, 5);
        }

        public void SetBLVDPowerState(bool powerOn)
        {
            _bus.WriteCoilValue(_addr, 14, powerOn ? (ushort)0x0001 : (ushort)0x0000);
        }

        public void SetLVDPowerState(int lvdIndex, bool powerOn)
        {
            if (lvdIndex < 1 || lvdIndex > 4)
                throw new ArgumentOutOfRangeException(nameof(lvdIndex));

            ushort coil = (ushort)(14 + lvdIndex);
            _bus.WriteCoilValue(_addr, coil, powerOn ? (ushort)0x0000 : (ushort)0x0001);
        }

        // 线圈 0x055D~0x0564: DI1~DI8 告警位
        public bool[] ReadProcess3DiAlarmCoils()
        {
            ThrowIfQueryPollingSuspended();
            return _bus.ReadCoils(_addr, 0x055D, 8);
        }

        /// <summary>
        /// 按协议分项读取 DI + AC/DC SPD（不做跨地址合并，避免部分固件回 0x04 从机故障）。
        /// </summary>
        public void ReadProcess3DiAndSpdAlarms(out bool[] diAlarms, out bool acSpdAlarm, out bool dcSpdAlarm)
        {
            diAlarms = ReadProcess3DiAlarmCoils();
            acSpdAlarm = ReadProcess3AcSpdAlarm();
            dcSpdAlarm = ReadProcess3DcSpdAlarm();
        }

        // AC SPD 告警: FC01 0x0565 数量1（01 01 05 65 00 01）
        public bool ReadProcess3AcSpdAlarm()
        {
            ThrowIfQueryPollingSuspended();
            bool[] coils = _bus.ReadCoils(_addr, 0x0565, 1);
            return coils != null && coils.Length > 0 && coils[0];
        }

        // DC SPD 告警: FC01 0x0566 数量1（01 01 05 66 00 01）
        public bool ReadProcess3DcSpdAlarm()
        {
            ThrowIfQueryPollingSuspended();
            bool[] coils = _bus.ReadCoils(_addr, 0x0566, 1);
            return coils != null && coils.Length > 0 && coils[0];
        }

        // 负载熔丝告警 LLVD1~4: FC01 0x0002 数量4（01 01 00 02 00 04）
        public bool[] ReadProcess3LoadFuseAlarms()
        {
            ThrowIfQueryPollingSuspended();
            return _bus.ReadCoils(_addr, 0x0002, 4);
        }

        /// <summary>
        /// 按协议分项读取负载熔丝与 LVD/BLVD（避免跨 0x0006 合并查询触发设备故障）。
        /// </summary>
        public void ReadProcess3LoadFuseAndLvdBlvdAlarms(out bool[] loadFuseAlarms, out bool[] lvdBlvdAlarms)
        {
            loadFuseAlarms = ReadProcess3LoadFuseAlarms();
            lvdBlvdAlarms = ReadProcess3LvdBlvdAlarmCoils();
        }

        // 蓄电池熔丝 BAT1~2: FC01 0x001C 数量2（01 01 00 1C 00 02）
        // 蓄电池熔丝 BAT3~8: FC01 0x10AF 数量6（01 01 10 AF 00 06）
        public bool[] ReadProcess3BatFuseAlarms()
        {
            ThrowIfQueryPollingSuspended();
            bool[] bat12 = _bus.ReadCoils(_addr, 0x001C, 2);
            ThrowIfQueryPollingSuspended();
            bool[] bat38 = _bus.ReadCoils(_addr, 0x10AF, 6);

            if (bat12 == null || bat12.Length < 2)
                throw new InvalidOperationException("蓄电池熔丝 BAT1-2 线圈返回数量不足。");
            if (bat38 == null || bat38.Length < 6)
                throw new InvalidOperationException("蓄电池熔丝 BAT3-8 线圈返回数量不足。");

            return new bool[]
            {
                bat12[0], bat12[1],
                bat38[0], bat38[1], bat38[2], bat38[3], bat38[4], bat38[5]
            };
        }

        // 兼容旧调用：线圈 0x055D~0x0568（含 DI 与原告警位）
        public bool[] ReadProcess3AlarmCoils()
        {
            ThrowIfQueryPollingSuspended();
            return _bus.ReadCoils(_addr, 0x055D, 12);
        }

        // 线圈 0x0007~0x000B: LVD1~LVD4、BLVD 告警位
        public bool[] ReadProcess3LvdBlvdAlarmCoils(bool allowWhenQueryPollingSuspended = false)
        {
            if (!allowWhenQueryPollingSuspended)
                ThrowIfQueryPollingSuspended();
            return _bus.ReadCoils(_addr, 0x0007, 5);
        }

        public IDictionary<string, bool> ReadAlarmSummarySnapshot()
        {
            var snapshot = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            ThrowIfQueryPollingSuspended();

            var diAlarms = ReadDIAlarmStatus();
            for (int i = 0; i < Math.Min(8, diAlarms.Length); i++)
                snapshot["DI" + (i + 1).ToString()] = diAlarms[i];

            ThrowIfQueryPollingSuspended();

            for (int i = 0; i < 8; i++)
            {
                ThrowIfQueryPollingSuspended();
                snapshot["DO" + (i + 1).ToString()] = GetDO(i + 1);
            }

            ThrowIfQueryPollingSuspended();

            var lvdBlvdOffStates = ReadLvdBlvdControlOffStatus();
            for (int i = 0; i < 4; i++)
                snapshot["LVD" + (i + 1).ToString()] = lvdBlvdOffStates.Length > i + 1 && lvdBlvdOffStates[i + 1];

            snapshot["BLVD"] = lvdBlvdOffStates.Length > 0 && lvdBlvdOffStates[0];
            return snapshot;
        }

        private void ThrowIfQueryPollingSuspended()
        {
            if (IsQueryPollingSuspended)
                throw new OperationCanceledException("查询轮询已暂停，当前跳过工序3告警查询");
        }

        // ── 工序5: 上下电测试 ─────────────────────────────────────
        // 寄存器52: BLVD1 State (1=LVD_OFF / 0=LVD_ON)
        public bool GetBLVDState()
        {
            return _bus.ReadHoldingRegisters(_addr, 52, 1)[0] == 1;
        }

        // DO控制 (6455~6462): 0=常开, 1=常闭
        public void SetDO(int doIndex, bool open)
        {
            // doIndex 1~8 对应寄存器 6455~6462
            ushort reg = (ushort)(6454 + doIndex);
            _bus.WriteSingleRegister(_addr, reg, (ushort)(open ? 0 : 1));
        }

        // 读DO状态
        public bool GetDO(int doIndex)
        {
            ushort reg = (ushort)(6454 + doIndex);
            return _bus.ReadHoldingRegisters(_addr, reg, 1)[0] == 0;
        }

        // 批量读取工序3 DO1~DO8状态（true=常开，false=常闭）
        public bool[] ReadProcess3DoStates(bool allowWhenQueryPollingSuspended = false)
        {
            if (!allowWhenQueryPollingSuspended)
                ThrowIfQueryPollingSuspended();

            ushort[] values = _bus.ReadHoldingRegisters(_addr, 6455, 8);
            var result = new bool[values.Length];
            for (int i = 0; i < values.Length; i++)
                result[i] = values[i] == 0;

            return result;
        }

        // 母排电压 = DC Voltage 寄存器28, 比例0.1
        public float GetDCVoltage()
        {
            return _bus.ReadHoldingRegisters(_addr, 28, 1)[0] * 0.1f;
        }

        // 电池电流 寄存器45（Batt Total Curr Reserve），比例0.01，偏移10000
        public float GetBattCurrent()
        {
            int raw = _bus.ReadHoldingRegisters(_addr, 45, 1)[0];
            return (raw - 10000) * 0.01f;
        }

        // 负载电流 寄存器29，比例0.01
        public float GetLoadCurrent()
        {
            return _bus.ReadHoldingRegisters(_addr, 29, 1)[0] * 0.01f;
        }

        // Battery SOC 寄存器48，比例0.1
        public float GetBattSOC()
        {
            return _bus.ReadHoldingRegisters(_addr, 48, 1)[0] * 0.1f;
        }

        // 蜂鸣器控制 寄存器6419 (1=OFF / 0=ON)
        public void SetBuzzer(bool on)
        {
            _bus.WriteSingleRegister(_addr, 6419, (ushort)(on ? 0 : 1));
        }
    }
}

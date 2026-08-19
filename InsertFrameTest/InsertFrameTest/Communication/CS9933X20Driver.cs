using System;
using System.Threading;

namespace InsertFrameTest.Communication
{
    // 长盛CS9933X-20安规综合测试仪 Modbus RTU驱动
    public class CS9933X20Driver
    {
        private readonly ModbusRtu _bus;
        private readonly byte _addr;

        // 测试状态代码
        public enum TestState
        {
            VoltageRising  = 0,
            Testing        = 1,
            VoltageFalling = 2,
            Interval       = 3,
            Waiting        = 4,
            Pass           = 5,
            Stop           = 6,
            UpperAlarm     = 7,
            LowerAlarm     = 8,
            ShortAlarm     = 9,
            VoltageAbnormal= 10,
            ArcAlarm       = 11,
            GFIAlarm       = 12,
            Fail           = 13,
            RealAlarm      = 14,
            ChargeAlarm    = 15,
            RangeAlarm     = 16,
            AmpAlarm       = 17,
            OutputDelay    = 18,
        }

        public CS9933X20Driver(ModbusRtu bus, byte addr = 1)
        {
            _bus = bus;
            _addr = addr;
        }

        /// <summary>
        /// 获取Modbus总线实例，用于自定义操作
        /// </summary>
        public ModbusRtu GetBus()
        {
            return _bus;
        }

        // 切换远程模式（0=本地,1=远程）
        public void SetRemote(bool remote)
        {
            _bus.WriteSingleRegister(_addr, 0x1000, (ushort)(remote ? 1 : 0));
        }

        // 启动测试
        public void StartTest()
        {
            _bus.WriteSingleRegister(_addr, 0x1001, 1);
        }

        // 停止测试
        public void StopTest()
        {
            _bus.WriteSingleRegister(_addr, 0x1002, 1);
        }

        // 加载文件编号(1-30)
        public void LoadFile(ushort fileNo)
        {
            _bus.WriteSingleRegister(_addr, 0x1010, fileNo);
        }

        // 读取当前测试状态
        public TestState GetTestState()
        {
            var regs = _bus.ReadHoldingRegisters(_addr, 0x1502, 1);
            return (TestState)regs[0];
        }

        // 读当前测试步编号
        public ushort GetTestStep()
        {
            return _bus.ReadHoldingRegisters(_addr, 0x1500, 1)[0];
        }

        // 读实时测试数据（输出值+测量值，float格式）
        public float GetOutputValue()
        {
            var regs = _bus.ReadHoldingRegisters(_addr, 0x1504, 2);
            return RegsToFloat(regs[0], regs[1]);
        }

        public float GetMeasuredValue()
        {
            var regs = _bus.ReadHoldingRegisters(_addr, 0x1506, 2);
            return RegsToFloat(regs[0], regs[1]);
        }

        // 等待测试完成（PASS或FAIL），超时秒数
        // 返回 true=PASS, false=FAIL
        public bool WaitForResult(int timeoutSec, Action<string> log)
        {
            DateTime deadline = DateTime.Now.AddSeconds(timeoutSec);
            while (DateTime.Now < deadline)
            {
                TestState st = GetTestState();
                if (st == TestState.Pass)
                {
                    log?.Invoke("安规测试: PASS");
                    return true;
                }
                if (st == TestState.Fail || st == TestState.UpperAlarm ||
                    st == TestState.LowerAlarm || st == TestState.ShortAlarm ||
                    st == TestState.GFIAlarm || st == TestState.ArcAlarm)
                {
                    log?.Invoke($"安规测试: FAIL (状态={st})");
                    return false;
                }
                Thread.Sleep(200);
            }
            log?.Invoke("安规测试: 超时");
            return false;
        }

        // 读取结果数据（结果步编号由调用方先写入0x1600）
        public SafetyResult ReadStepResult(ushort stepNo)
        {
            // 用FC17: 先写步编号到0x1600，再读0x1601~0x160A
            // 简化：直接写再读
            _bus.WriteSingleRegister(_addr, 0x1600, stepNo);
            Thread.Sleep(50);
            var regs = _bus.ReadHoldingRegisters(_addr, 0x1601, 9);
            return new SafetyResult
            {
                Mode         = regs[0],
                State        = (TestState)regs[1],
                Time         = regs[2] * 0.1f,
                OutputValue  = RegsToFloat(regs[3], regs[4]),
                MeasureValue = RegsToFloat(regs[5], regs[6]),
                AuxInfo      = regs[7],
                TrueCurrentH = regs[8],
            };
        }

        private static float RegsToFloat(ushort hi, ushort lo)
        {
            uint raw = ((uint)hi << 16) | lo;
            return BitConverter.ToSingle(BitConverter.GetBytes(raw), 0);
        }
    }

    public class SafetyResult
    {
        public ushort Mode;
        public CS9933X20Driver.TestState State;
        public float  Time;
        public float  OutputValue;
        public float  MeasureValue;
        public ushort AuxInfo;
        public ushort TrueCurrentH;
    }
}

using InsertFrameTest.Models;

namespace InsertFrameTest.Processes
{
    // 当前挂接到工序4参数校准页签（手动校准，不再通过串口轮询读取）
    public class Process5Power : ProcessBase
    {
        protected override bool RequiresProgramFile => false;

        public Process5Power()
        {
            ProcessNo   = 4;
            ProcessName = "参数校准";
        }

        protected override void Execute(string barcode, byte[] programFile, ProcessResult result)
        {
            Log("工序4参数校准为手动模式，不执行串口查询");
            result.Pass = true;
        }
    }
}

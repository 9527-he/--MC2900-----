using InsertFrameTest.Models;

namespace InsertFrameTest.Processes
{
    // 工序3: 模块功能测试（模块通讯、地址设置）
    public class Process3Module : ProcessBase
    {
        public Process3Module()
        {
            ProcessNo   = 3;
            ProcessName = "模块功能测试";
        }

        protected override void Execute(string barcode, byte[] programFile, ProcessResult result)
        {
            Log("工序3收发数据流程已移除，当前版本仅保留工序1自动 ASCII 测试流程");
            AddBoolItem(result, 1, "工序3收发流程已移除", true);
            result.Pass = true;
        }
    }
}

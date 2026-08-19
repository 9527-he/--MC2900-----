using System;

namespace InsertFrameTest.Mes
{
    /// <summary>
    /// MES 作业头信息，对应 MES 界面顶部参数区。
    /// </summary>
    public sealed class MesHeaderInfo
    {
        public string Barcode { get; set; }
        public string JobId { get; set; }
        public string TaskOrder { get; set; }
        public string ProcessFlow { get; set; }
        public string ProductCode { get; set; }
        public string ProductDesc { get; set; }
        public string Operator { get; set; }
        public string ProductionLine { get; set; }
        public string Shift { get; set; }
        public string WorkTime { get; set; }
        public string PrevProcess { get; set; }
        public string NextProcess { get; set; }
        public string Quantity { get; set; }
        public string Foreman { get; set; }
        public string Result { get; set; }

        public static MesHeaderInfo CreateEmpty(string barcode, string result)
        {
            return new MesHeaderInfo
            {
                Barcode = barcode ?? string.Empty,
                JobId = string.Empty,
                TaskOrder = string.Empty,
                ProcessFlow = string.Empty,
                ProductCode = string.Empty,
                ProductDesc = string.Empty,
                Operator = string.Empty,
                ProductionLine = string.Empty,
                Shift = string.Empty,
                WorkTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                PrevProcess = string.Empty,
                NextProcess = string.Empty,
                Quantity = "1",
                Foreman = string.Empty,
                Result = string.IsNullOrWhiteSpace(result) ? string.Empty : result.Trim().ToUpperInvariant(),
            };
        }
    }
}

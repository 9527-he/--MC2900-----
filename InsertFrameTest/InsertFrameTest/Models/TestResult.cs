using System;
using System.Collections.Generic;
using System.Text;
using InsertFrameTest.Mes;

namespace InsertFrameTest.Models
{
    public class TestItem
    {
        public int    Step;
        public string Name;
        public float  MaxValue;
        public float  MinValue;
        public float  Value;
        public bool   Pass;
        public float  Time;

        public string ToDetail()
        {
            return Mes.MesClient.BuildDetail(Step, MaxValue, MinValue, Value, Pass, Name, Time);
        }
    }

    public class RawRecord
    {
        public int    Step;
        public string Title;
        public string RawData;
        public bool   Pass;
    }

    public class ProcessResult
    {
        private readonly object _rawRecordLock = new object();

        public string         Barcode;
        public int            ProcessNo;
        public bool           Pass;
        public DateTime       StartTime;
        public DateTime       EndTime;
        public List<TestItem> Items = new List<TestItem>();
        public string         DataFilePath;
        public string         DataCsvFilePath;
        public string         ExtraDetails;
        public string         UploadRawData;
        public List<RawRecord> RawRecords = new List<RawRecord>();

        public float ElapsedSeconds => (float)(EndTime - StartTime).TotalSeconds;

        public string BuildDetails()
        {
            var sb = new StringBuilder();
            foreach (var item in Items)
                sb.Append(item.ToDetail());
            if (!string.IsNullOrWhiteSpace(ExtraDetails))
                sb.Append(ExtraDetails);
            return sb.ToString();
        }

        public void AppendRawRecord(int step, string title, string rawData, bool pass = true)
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(rawData))
                return;

            lock (_rawRecordLock)
            {
                // 原始报文只写入结果文件 / UploadRawData，不进入 MES details，
                // 避免逗号拆分导致“参数计数不匹配”，并保持测试明细与 MES 界面一致。
                if (!string.IsNullOrWhiteSpace(UploadRawData))
                    UploadRawData += Environment.NewLine;

                UploadRawData += title + Environment.NewLine + rawData;
                RawRecords.Add(new RawRecord
                {
                    Step = step,
                    Title = title,
                    RawData = rawData,
                    Pass = pass,
                });
            }
        }
    }
}

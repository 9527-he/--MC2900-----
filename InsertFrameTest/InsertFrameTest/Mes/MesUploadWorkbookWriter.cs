using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace InsertFrameTest.Mes
{
    /// <summary>
    /// MES上传汇总.xlsx：底部 4 个工序工作表，列布局参考 MES「测试总结」：
    /// {工序备注列} / ITEM / TEST / TESTVALUE / MIN SPEC / MAX SPEC / UNIT / RESULT / TEST_REMARK
    /// </summary>
    internal static class MesUploadWorkbookWriter
    {
        // 防止多线程同时改写同一 xlsx（工序线程/后台 SNMP/UI）导致卡死或文件损坏
        private static readonly object WorkbookIoLock = new object();

        private sealed class ProcessSheetInfo
        {
            public int ProcessNo;
            public string SheetName;
            public string FirstColumnHeader;
            public string TestColumnHeader;
            public string EntryName;
            public string RelId;
        }

        private static readonly ProcessSheetInfo[] Sheets =
        {
            new ProcessSheetInfo
            {
                ProcessNo = 1,
                SheetName = "工序1-安规测试",
                FirstColumnHeader = "安规步骤",
                TestColumnHeader = "安规项目",
                EntryName = "xl/worksheets/sheet1.xml",
                RelId = "rId1",
            },
            new ProcessSheetInfo
            {
                ProcessNo = 2,
                SheetName = "工序2-温湿度时间同步",
                FirstColumnHeader = "同步序号",
                TestColumnHeader = "同步项目",
                EntryName = "xl/worksheets/sheet2.xml",
                RelId = "rId2",
            },
            new ProcessSheetInfo
            {
                ProcessNo = 3,
                SheetName = "工序3-告警测试",
                FirstColumnHeader = "告警序号",
                TestColumnHeader = "告警项目",
                EntryName = "xl/worksheets/sheet3.xml",
                RelId = "rId3",
            },
            new ProcessSheetInfo
            {
                ProcessNo = 4,
                SheetName = "工序4-参数校准",
                FirstColumnHeader = "校准序号",
                TestColumnHeader = "校准项目",
                EntryName = "xl/worksheets/sheet4.xml",
                RelId = "rId4",
            },
        };

        /// <summary>
        /// 工序4参数校准保存记录。
        /// </summary>
        public static string SaveCalibrationRecord(
            string barcode,
            string ip,
            string snmpOid,
            string itemName,
            string measure,
            string actual,
            string point,
            string k,
            string result,
            string setReply = null)
        {
            return SaveCalibrationRecord(
                barcode, ip, snmpOid, itemName, measure, actual, point, k, result, setReply, false, null);
        }

        public static string SaveCalibrationRecord(
            string barcode,
            string ip,
            string snmpOid,
            string itemName,
            string measure,
            string actual,
            string point,
            string k,
            string result,
            string setReply,
            bool writeMesHeader,
            MesHeaderInfo mesHeader)
        {
            string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string safeBarcode = string.IsNullOrWhiteSpace(barcode) ? "-" : barcode.Trim();
            string safeIp = string.IsNullOrWhiteSpace(ip) ? "-" : ip.Trim();
            string safeOid = string.IsNullOrWhiteSpace(snmpOid) ? "-" : snmpOid.Trim();
            string safeName = string.IsNullOrWhiteSpace(itemName) ? "参数校准" : itemName.Trim();

            string details = MesClient.BuildDetail(
                1, 0, 0, ParseFloat(measure),
                string.Equals(result, "PASS", StringComparison.OrdinalIgnoreCase),
                safeName, 0);

            string remark =
                $"Measure={measure}; Actual={actual}; Point={point}; K={k}" +
                (string.IsNullOrWhiteSpace(setReply) ? string.Empty : "; Reply=" + CompactText(setReply));

            string rawData =
                $"{time} {safeBarcode} {safeIp} {safeOid} {safeName} {remark} Result={result}";

            string filePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "TestData",
                "Process4",
                "CalibrationSave.log");

            return SaveUploadWorkbook(
                safeBarcode,
                "参数校准",
                result,
                details,
                filePath,
                rawData,
                safeIp,
                safeOid,
                4,
                remark,
                writeMesHeader,
                mesHeader);
        }

        /// <summary>
        /// 工序3告警记录。
        /// </summary>
        public static string SaveAlarmCaptureRecord(
            string barcode,
            string alarmName,
            string statusSummary,
            string frameText)
        {
            return SaveAlarmCaptureRecord(barcode, alarmName, statusSummary, frameText, false, null);
        }

        public static string SaveAlarmCaptureRecord(
            string barcode,
            string alarmName,
            string statusSummary,
            string frameText,
            bool writeMesHeader,
            MesHeaderInfo mesHeader)
        {
            string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string safeBarcode = string.IsNullOrWhiteSpace(barcode) ? "-" : barcode.Trim();
            string safeName = string.IsNullOrWhiteSpace(alarmName) ? "告警记录" : alarmName.Trim();
            string summary = CompactText(statusSummary ?? string.Empty);
            string frames = frameText ?? string.Empty;

            string details = MesClient.BuildDetail(1, 0, 0, 0, true, safeName, 0);
            string rawData = $"{time} {safeBarcode} 告警测试 {safeName} {summary}{Environment.NewLine}{frames}";

            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "Process3");
            Directory.CreateDirectory(dir);
            string filePath = Path.Combine(dir, "AlarmCapture.log");
            try
            {
                File.AppendAllText(filePath, rawData + Environment.NewLine + "----" + Environment.NewLine, Encoding.UTF8);
            }
            catch { }

            return SaveUploadWorkbook(
                safeBarcode,
                "告警测试",
                "PASS",
                details,
                filePath,
                rawData,
                ip: null,
                snmpOid: null,
                processNo: 3,
                extraRemark: summary,
                writeMesHeader: writeMesHeader,
                mesHeader: mesHeader);
        }

        /// <summary>
        /// 工序2模块通信记录。
        /// </summary>
        public static string SaveModuleCaptureRecord(
            string barcode,
            string actionName,
            string statusSummary,
            string frameText)
        {
            return SaveModuleCaptureRecord(barcode, actionName, statusSummary, frameText, false, null);
        }

        public static string SaveModuleCaptureRecord(
            string barcode,
            string actionName,
            string statusSummary,
            string frameText,
            bool writeMesHeader,
            MesHeaderInfo mesHeader)
        {
            string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string safeBarcode = string.IsNullOrWhiteSpace(barcode) ? "-" : barcode.Trim();
            string safeName = string.IsNullOrWhiteSpace(actionName) ? "模块通信" : actionName.Trim();
            string summary = CompactText(statusSummary ?? string.Empty);
            string frames = frameText ?? string.Empty;

            string details = MesClient.BuildDetail(1, 0, 0, 0, true, safeName, 0);
            string rawData = $"{time} {safeBarcode} 模块通信 {safeName} {summary}{Environment.NewLine}{frames}";

            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "Process2");
            Directory.CreateDirectory(dir);
            string filePath = Path.Combine(dir, "ModuleCapture.log");
            try
            {
                File.AppendAllText(filePath, rawData + Environment.NewLine + "----" + Environment.NewLine, Encoding.UTF8);
            }
            catch { }

            return SaveUploadWorkbook(
                safeBarcode,
                "模块通信",
                "PASS",
                details,
                filePath,
                rawData,
                ip: null,
                snmpOid: null,
                processNo: 2,
                extraRemark: summary,
                writeMesHeader: writeMesHeader,
                mesHeader: mesHeader);
        }

        /// <summary>
        /// 工序2南向485记录。
        /// </summary>
        public static string SaveSouth485CaptureRecord(
            string barcode,
            string direction,
            string frameHex)
        {
            return SaveSouth485CaptureRecord(barcode, direction, frameHex, false, null);
        }

        public static string SaveSouth485CaptureRecord(
            string barcode,
            string direction,
            string frameHex,
            bool writeMesHeader,
            MesHeaderInfo mesHeader)
        {
            string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string safeBarcode = string.IsNullOrWhiteSpace(barcode) ? "-" : barcode.Trim();
            string safeDirection = string.IsNullOrWhiteSpace(direction) ? "未知" : direction.Trim();
            string safeHex = CompactText(frameHex ?? string.Empty);

            string details = MesClient.BuildDetail(1, 0, 0, 0, true, "南向485-" + safeDirection, 0);
            string rawData = $"{time} {safeBarcode} 南向485 {safeDirection} HEX={frameHex}";

            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "Process2");
            Directory.CreateDirectory(dir);
            string filePath = Path.Combine(dir, "South485Capture.log");
            try
            {
                File.AppendAllText(filePath, rawData + Environment.NewLine, Encoding.UTF8);
            }
            catch { }

            return SaveUploadWorkbook(
                safeBarcode,
                "南向485测试",
                "PASS",
                details,
                filePath,
                rawData,
                ip: null,
                snmpOid: null,
                processNo: 2,
                extraRemark: "HEX=" + safeHex,
                writeMesHeader: writeMesHeader,
                mesHeader: mesHeader);
        }

        /// <summary>
        /// 工序2传感器温度记录。
        /// </summary>
        public static string SaveSensorCaptureRecord(
            string barcode,
            string sensorName,
            string statusSummary,
            string frameText)
        {
            return SaveSensorCaptureRecord(barcode, sensorName, statusSummary, frameText, false, null);
        }

        public static string SaveSensorCaptureRecord(
            string barcode,
            string sensorName,
            string statusSummary,
            string frameText,
            bool writeMesHeader,
            MesHeaderInfo mesHeader)
        {
            string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string safeBarcode = string.IsNullOrWhiteSpace(barcode) ? "-" : barcode.Trim();
            string safeName = string.IsNullOrWhiteSpace(sensorName) ? "温度传感器" : sensorName.Trim();
            string summary = CompactText(statusSummary ?? string.Empty);
            string frames = frameText ?? string.Empty;

            string details = MesClient.BuildDetail(1, 0, 0, 0, true, safeName, 0);
            string rawData = $"{time} {safeBarcode} 传感器温度 {safeName} {summary}{Environment.NewLine}{frames}";

            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "Process2");
            Directory.CreateDirectory(dir);
            string filePath = Path.Combine(dir, "SensorCapture.log");
            try
            {
                File.AppendAllText(filePath, rawData + Environment.NewLine + "----" + Environment.NewLine, Encoding.UTF8);
            }
            catch { }

            return SaveUploadWorkbook(
                safeBarcode,
                "传感器温度",
                "PASS",
                details,
                filePath,
                rawData,
                ip: null,
                snmpOid: null,
                processNo: 2,
                extraRemark: summary,
                writeMesHeader: writeMesHeader,
                mesHeader: mesHeader);
        }

        public static string SaveUploadWorkbook(
            string barcode,
            string deviceName,
            string result,
            string details,
            string filePath,
            string rawData,
            string ip,
            string snmpOid)
        {
            return SaveUploadWorkbook(barcode, deviceName, result, details, filePath, rawData, ip, snmpOid, 0, null, false, null);
        }

        public static string SaveUploadWorkbook(
            string barcode,
            string deviceName,
            string result,
            string details,
            string filePath,
            string rawData,
            int processNo)
        {
            return SaveUploadWorkbook(barcode, deviceName, result, details, filePath, rawData, null, null, processNo, null, false, null);
        }

        public static string SaveUploadWorkbook(
            string barcode,
            string deviceName,
            string result,
            string details,
            string filePath,
            string rawData,
            int processNo,
            bool writeMesHeader,
            MesHeaderInfo mesHeader)
        {
            return SaveUploadWorkbook(barcode, deviceName, result, details, filePath, rawData, null, null, processNo, null, writeMesHeader, mesHeader);
        }

        public static string SaveUploadWorkbook(
            string barcode,
            string deviceName,
            string result,
            string details,
            string filePath,
            string rawData,
            string ip,
            string snmpOid,
            int processNo)
        {
            return SaveUploadWorkbook(barcode, deviceName, result, details, filePath, rawData, ip, snmpOid, processNo, null, false, null);
        }

        public static string SaveUploadWorkbook(
            string barcode,
            string deviceName,
            string result,
            string details,
            string filePath,
            string rawData,
            string ip,
            string snmpOid,
            int processNo,
            string extraRemark)
        {
            return SaveUploadWorkbook(barcode, deviceName, result, details, filePath, rawData, ip, snmpOid, processNo, extraRemark, false, null);
        }

        public static string SaveUploadWorkbook(
            string barcode,
            string deviceName,
            string result,
            string details,
            string filePath,
            string rawData,
            string ip,
            string snmpOid,
            int processNo,
            string extraRemark,
            bool writeMesHeader,
            MesHeaderInfo mesHeader)
        {
            string rootDir = AppDomain.CurrentDomain.BaseDirectory;
            string workbookPath = Path.Combine(rootDir, "MES上传汇总.xlsx");

            var sheet = ResolveSheet(processNo, deviceName);
            var rows = BuildDetailRows(sheet, barcode, deviceName, result, details, ip, snmpOid, extraRemark);
            if (rows.Count == 0)
                rows.Add(CreateFallbackRow(sheet, barcode, deviceName, result, details, extraRemark));

            if (writeMesHeader && mesHeader == null)
                mesHeader = MesHeaderInfo.CreateEmpty(barcode, result);
            if (writeMesHeader && mesHeader != null)
            {
                if (string.IsNullOrWhiteSpace(mesHeader.Barcode))
                    mesHeader.Barcode = barcode ?? string.Empty;
                if (string.IsNullOrWhiteSpace(mesHeader.Result))
                    mesHeader.Result = result ?? string.Empty;
                if (string.IsNullOrWhiteSpace(mesHeader.WorkTime))
                    mesHeader.WorkTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }

            lock (WorkbookIoLock)
            {
                EnsureProcessWorkbook(workbookPath, writeMesHeader);

                if (!File.Exists(workbookPath))
                    CreateWorkbook(workbookPath, sheet, rows, writeMesHeader, mesHeader);
                else
                    AppendWorkbookRows(workbookPath, sheet, rows, writeMesHeader, mesHeader);
            }

            return workbookPath;
        }

        private const int MesHeaderBlockRows = 7; // 条码1行 + 参数5行 + 空行1

        private static List<string[]> BuildMesHeaderRows(MesHeaderInfo header)
        {
            header = header ?? MesHeaderInfo.CreateEmpty(string.Empty, string.Empty);
            var rows = new List<string[]>
            {
                new[] { "条码", NullToEmpty(header.Barcode), "", "", "", "", "", "", "" },
                new[] { "作业ID", NullToEmpty(header.JobId), "生产任务", NullToEmpty(header.TaskOrder), "工艺流程", NullToEmpty(header.ProcessFlow), "", "", "" },
                new[] { "产品编号", NullToEmpty(header.ProductCode), "产品描述", NullToEmpty(header.ProductDesc), "作业人员", NullToEmpty(header.Operator), "", "", "" },
                new[] { "生产线别", NullToEmpty(header.ProductionLine), "作业班次", NullToEmpty(header.Shift), "作业时间", NullToEmpty(header.WorkTime), "", "", "" },
                new[] { "前工序", NullToEmpty(header.PrevProcess), "后工序", NullToEmpty(header.NextProcess), "数量", NullToEmpty(string.IsNullOrWhiteSpace(header.Quantity) ? "1" : header.Quantity), "", "", "" },
                new[] { "领班", NullToEmpty(header.Foreman), "结果", NullToEmpty(header.Result), "", "", "", "", "" },
                new[] { "", "", "", "", "", "", "", "", "" },
            };
            return rows;
        }

        private static string NullToEmpty(string value)
        {
            return value ?? string.Empty;
        }

        private static ProcessSheetInfo ResolveSheet(int processNo, string deviceName)
        {
            if (processNo >= 1 && processNo <= 4)
                return Sheets[processNo - 1];

            string name = deviceName ?? string.Empty;
            if (name.IndexOf("安规", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("绝缘", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("交流", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("直流", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("接地", StringComparison.Ordinal) >= 0)
                return Sheets[0];

            if (name.IndexOf("告警", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("DI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("DO", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("LVD", StringComparison.OrdinalIgnoreCase) >= 0)
                return Sheets[2];

            if (name.IndexOf("校准", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("参数", StringComparison.Ordinal) >= 0)
                return Sheets[3];

            return Sheets[1];
        }

        private static string[] BuildHeaders(ProcessSheetInfo sheet)
        {
            return new[]
            {
                sheet.FirstColumnHeader,
                "ITEM",
                sheet.TestColumnHeader,
                "TESTVALUE",
                "MIN SPEC",
                "MAX SPEC",
                "UNIT",
                "RESULT",
                "TEST_REMARK",
            };
        }

        private static List<string[]> BuildDetailRows(
            ProcessSheetInfo sheet,
            string barcode,
            string deviceName,
            string result,
            string details,
            string ip,
            string snmpOid,
            string extraRemark)
        {
            var rows = new List<string[]>();
            string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string safeBarcode = barcode ?? string.Empty;
            int refIndex = 1;

            if (!string.IsNullOrWhiteSpace(details))
            {
                string[] lines = details.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    string[] parsed;
                    if (!TryParseMesDetailLine(line.Trim(), out parsed))
                        continue;

                    // parsed: step, max, min, value, result, title, time
                    string testName = parsed[5];
                    string unit;
                    string cleanName = SplitUnit(testName, out unit);
                    string remark = BuildRemark(safeBarcode, time, ip, snmpOid, extraRemark, deviceName);

                    rows.Add(new[]
                    {
                        refIndex.ToString(CultureInfo.InvariantCulture), // 工序备注列（序号）
                        parsed[0],                                       // ITEM = step
                        cleanName,                                       // TEST
                        parsed[3],                                       // TESTVALUE
                        parsed[2],                                       // MIN SPEC
                        parsed[1],                                       // MAX SPEC
                        unit,                                            // UNIT
                        parsed[4],                                       // RESULT
                        remark,                                          // TEST_REMARK
                    });
                    refIndex++;
                }
            }

            return rows;
        }

        private static string[] CreateFallbackRow(
            ProcessSheetInfo sheet,
            string barcode,
            string deviceName,
            string result,
            string details,
            string extraRemark)
        {
            string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string unit;
            string testName = SplitUnit(
                string.IsNullOrWhiteSpace(deviceName) ? CompactText(details) : deviceName.Trim(),
                out unit);

            return new[]
            {
                "1",
                "1",
                testName,
                string.Empty,
                string.Empty,
                string.Empty,
                unit,
                string.IsNullOrWhiteSpace(result) ? string.Empty : result.Trim(),
                BuildRemark(barcode, time, null, null, extraRemark ?? CompactText(details), deviceName),
            };
        }

        private static string BuildRemark(
            string barcode,
            string time,
            string ip,
            string snmpOid,
            string extraRemark,
            string deviceName)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(barcode))
                parts.Add("条码=" + barcode.Trim());
            if (!string.IsNullOrWhiteSpace(time))
                parts.Add("时间=" + time.Trim());
            if (!string.IsNullOrWhiteSpace(deviceName))
                parts.Add("来源=" + deviceName.Trim());
            if (!string.IsNullOrWhiteSpace(ip))
                parts.Add("IP=" + ip.Trim());
            if (!string.IsNullOrWhiteSpace(snmpOid))
                parts.Add("OID=" + snmpOid.Trim());
            if (!string.IsNullOrWhiteSpace(extraRemark))
                parts.Add(extraRemark.Trim());
            return string.Join("; ", parts);
        }

        private static string SplitUnit(string testName, out string unit)
        {
            unit = string.Empty;
            if (string.IsNullOrWhiteSpace(testName))
                return string.Empty;

            string name = testName.Trim();
            var match = Regex.Match(name, @"[\(（]([^\)）]+)[\)）]\s*$");
            if (match.Success)
            {
                unit = match.Groups[1].Value.Trim();
                name = name.Substring(0, match.Index).Trim();
            }
            return name;
        }

        private static bool TryParseMesDetailLine(string line, out string[] fields)
        {
            fields = null;
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith(",,,,", StringComparison.Ordinal))
                return false;

            string[] parts = line.Substring(4).Split(',');
            if (parts.Length < 7)
                return false;

            int resultIndex = -1;
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (string.Equals(p, "PASS", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p, "FAIL", StringComparison.OrdinalIgnoreCase))
                {
                    resultIndex = i;
                    break;
                }
            }

            if (resultIndex < 4 || resultIndex >= parts.Length - 1)
                return false;

            fields = new[]
            {
                parts[0].Trim(),
                parts[1].Trim(),
                parts[2].Trim(),
                string.Join(",", parts, 3, resultIndex - 3).Trim(),
                parts[resultIndex].Trim().ToUpperInvariant(),
                string.Join(",", parts, resultIndex + 1, parts.Length - resultIndex - 2).Trim(),
                parts[parts.Length - 1].Trim(),
            };
            return true;
        }

        private static float ParseFloat(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0f;
            float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value);
            return value;
        }

        private static string CompactText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            return text
                .Replace("\r\n", " | ")
                .Replace("\n", " | ")
                .Replace("\r", " | ")
                .Replace(",", "，")
                .Trim();
        }

        private static void EnsureProcessWorkbook(string workbookPath, bool requireMesHeader)
        {
            if (!File.Exists(workbookPath))
                return;

            try
            {
                if (IsProcessWorkbook(workbookPath))
                {
                    bool hasHeader = WorkbookHasMesHeader(workbookPath);
                    if (requireMesHeader == hasHeader)
                        return;
                }

                string backup = Path.Combine(
                    Path.GetDirectoryName(workbookPath) ?? ".",
                    "MES上传汇总_旧格式_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx");
                File.Copy(workbookPath, backup, true);
                File.Delete(workbookPath);
            }
            catch
            {
            }
        }

        private static bool WorkbookHasMesHeader(string workbookPath)
        {
            using (var stream = new FileStream(workbookPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var entry = archive.GetEntry(Sheets[0].EntryName);
                if (entry == null)
                    return false;
                using (var reader = new StreamReader(entry.Open(), Encoding.UTF8, true))
                {
                    string xml = reader.ReadToEnd();
                    return xml.Contains(">条码<") && xml.Contains(">作业ID<") && xml.Contains(">生产任务<");
                }
            }
        }

        private static bool IsProcessWorkbook(string workbookPath)
        {
            using (var stream = new FileStream(workbookPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                if (archive.GetEntry("xl/worksheets/sheet4.xml") == null)
                    return false;

                var wb = archive.GetEntry("xl/workbook.xml");
                if (wb == null)
                    return false;

                using (var reader = new StreamReader(wb.Open(), Encoding.UTF8, true))
                {
                    string xml = reader.ReadToEnd();
                    return xml.Contains("工序1-安规测试") &&
                           xml.Contains("工序2-温湿度时间同步") &&
                           xml.Contains("工序3-告警测试") &&
                           xml.Contains("工序4-参数校准");
                }
            }
        }

        private static void CreateWorkbook(
            string workbookPath,
            ProcessSheetInfo firstSheet,
            List<string[]> firstRows,
            bool writeMesHeader,
            MesHeaderInfo mesHeader)
        {
            using (var stream = new FileStream(workbookPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "[Content_Types].xml", BuildContentTypesXml());
                WriteEntry(archive, "_rels/.rels", BuildRootRelsXml());
                WriteEntry(archive, "xl/workbook.xml", BuildWorkbookXml());
                WriteEntry(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelsXml());

                foreach (var sheet in Sheets)
                {
                    var rows = ReferenceEquals(sheet, firstSheet) ? firstRows : new List<string[]>();
                    WriteEntry(archive, sheet.EntryName, BuildWorksheetXml(sheet, rows, writeMesHeader, mesHeader));
                }
            }
        }

        private static void AppendWorkbookRows(
            string workbookPath,
            ProcessSheetInfo sheet,
            List<string[]> rows,
            bool writeMesHeader,
            MesHeaderInfo mesHeader)
        {
            using (var stream = new FileStream(workbookPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Update))
            {
                // MES 检查开启时：每个工作表头部都刷新 MES 参数
                if (writeMesHeader)
                {
                    foreach (var s in Sheets)
                        UpdateSheetMesHeader(archive, s, mesHeader);
                }

                var worksheetEntry = archive.GetEntry(sheet.EntryName);
                if (worksheetEntry == null)
                    throw new FileNotFoundException("xlsx 工作表缺失: " + sheet.EntryName);

                string xml;
                using (var reader = new StreamReader(worksheetEntry.Open(), Encoding.UTF8, true, 4096, false))
                    xml = reader.ReadToEnd();

                var doc = new XmlDocument();
                doc.LoadXml(xml);

                var ns = new XmlNamespaceManager(doc.NameTable);
                ns.AddNamespace("x", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");

                var sheetData = doc.SelectSingleNode("/x:worksheet/x:sheetData", ns) as XmlElement;
                if (sheetData == null)
                    throw new InvalidOperationException("xlsx 工作表缺少 sheetData 节点");

                int existingRows = sheetData.SelectNodes("x:row", ns).Count;
                int headerOffset = writeMesHeader ? MesHeaderBlockRows : 0;
                int tableHeaderRow = headerOffset + 1;

                if (existingRows < tableHeaderRow)
                {
                    if (writeMesHeader)
                        WriteMesHeaderToSheetData(doc, sheetData, ns, mesHeader);
                    sheetData.AppendChild(CreateRow(doc, tableHeaderRow, BuildHeaders(sheet)));
                    existingRows = tableHeaderRow;
                }

                int rowIndex = existingRows + 1;
                int dataCount = Math.Max(0, existingRows - tableHeaderRow);
                int refBase = dataCount;
                foreach (var values in rows)
                {
                    var copy = (string[])values.Clone();
                    copy[0] = (refBase + 1).ToString(CultureInfo.InvariantCulture);
                    sheetData.AppendChild(CreateRow(doc, rowIndex, copy));
                    refBase++;
                    rowIndex++;
                }

                worksheetEntry.Delete();
                WriteEntry(archive, sheet.EntryName, SerializeXml(doc));
            }
        }

        private static void UpdateSheetMesHeader(ZipArchive archive, ProcessSheetInfo sheet, MesHeaderInfo mesHeader)
        {
            var worksheetEntry = archive.GetEntry(sheet.EntryName);
            if (worksheetEntry == null)
                return;

            string xml;
            using (var reader = new StreamReader(worksheetEntry.Open(), Encoding.UTF8, true, 4096, false))
                xml = reader.ReadToEnd();

            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("x", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
            var sheetData = doc.SelectSingleNode("/x:worksheet/x:sheetData", ns) as XmlElement;
            if (sheetData == null)
                return;

            int existingRows = sheetData.SelectNodes("x:row", ns).Count;
            if (existingRows == 0)
            {
                WriteMesHeaderToSheetData(doc, sheetData, ns, mesHeader);
                sheetData.AppendChild(CreateRow(doc, MesHeaderBlockRows + 1, BuildHeaders(sheet)));
            }
            else if (existingRows >= MesHeaderBlockRows)
            {
                ReplaceMesHeaderRows(doc, sheetData, ns, mesHeader);
            }
            else
            {
                // 旧表无头部：无法安全插入，跳过（EnsureProcessWorkbook 应已重建）
                return;
            }

            worksheetEntry.Delete();
            WriteEntry(archive, sheet.EntryName, SerializeXml(doc));
        }

        private static void WriteMesHeaderToSheetData(
            XmlDocument doc,
            XmlElement sheetData,
            XmlNamespaceManager ns,
            MesHeaderInfo mesHeader)
        {
            var headerRows = BuildMesHeaderRows(mesHeader);
            for (int i = 0; i < headerRows.Count; i++)
                sheetData.AppendChild(CreateRow(doc, i + 1, headerRows[i]));
        }

        private static void ReplaceMesHeaderRows(
            XmlDocument doc,
            XmlElement sheetData,
            XmlNamespaceManager ns,
            MesHeaderInfo mesHeader)
        {
            var existing = sheetData.SelectNodes("x:row", ns);
            var headerRows = BuildMesHeaderRows(mesHeader);
            for (int i = 0; i < headerRows.Count && i < existing.Count; i++)
            {
                var oldRow = existing[i] as XmlElement;
                var newRow = CreateRow(doc, i + 1, headerRows[i]);
                sheetData.ReplaceChild(newRow, oldRow);
            }
        }

        private static void WriteEntry(ZipArchive archive, string entryName, string content)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                writer.Write(content);
        }

        private static string BuildContentTypesXml()
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
            sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            sb.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            foreach (var sheet in Sheets)
                sb.Append("<Override PartName=\"/").Append(sheet.EntryName)
                  .Append("\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            sb.Append("</Types>");
            return sb.ToString();
        }

        private static string BuildRootRelsXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                   "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                   "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                   "</Relationships>";
        }

        private static string BuildWorkbookXml()
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
            sb.Append("<sheets>");
            for (int i = 0; i < Sheets.Length; i++)
            {
                sb.Append("<sheet name=\"").Append(EscapeXml(Sheets[i].SheetName))
                  .Append("\" sheetId=\"").Append(i + 1)
                  .Append("\" r:id=\"").Append(Sheets[i].RelId).Append("\"/>");
            }
            sb.Append("</sheets></workbook>");
            return sb.ToString();
        }

        private static string BuildWorkbookRelsXml()
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            foreach (var sheet in Sheets)
            {
                sb.Append("<Relationship Id=\"").Append(sheet.RelId)
                  .Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/")
                  .Append(Path.GetFileName(sheet.EntryName)).Append("\"/>");
            }
            sb.Append("</Relationships>");
            return sb.ToString();
        }

        private static string BuildWorksheetXml(
            ProcessSheetInfo sheet,
            List<string[]> dataRows,
            bool writeMesHeader,
            MesHeaderInfo mesHeader)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            sb.Append("<sheetData>");
            int rowIndex = 1;
            if (writeMesHeader)
            {
                foreach (var headerRow in BuildMesHeaderRows(mesHeader))
                {
                    AppendRow(sb, rowIndex, headerRow);
                    rowIndex++;
                }
            }
            AppendRow(sb, rowIndex, BuildHeaders(sheet));
            rowIndex++;
            for (int i = 0; i < dataRows.Count; i++)
                AppendRow(sb, rowIndex + i, dataRows[i]);
            sb.Append("</sheetData>");
            sb.Append("</worksheet>");
            return sb.ToString();
        }

        private static XmlElement CreateRow(XmlDocument doc, int rowIndex, params string[] values)
        {
            const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var row = doc.CreateElement("row", ns);
            row.SetAttribute("r", rowIndex.ToString());

            for (int i = 0; i < values.Length; i++)
            {
                string cellRef = GetColumnName(i + 1) + rowIndex;
                var cell = doc.CreateElement("c", ns);
                cell.SetAttribute("r", cellRef);
                cell.SetAttribute("t", "inlineStr");

                var inlineString = doc.CreateElement("is", ns);
                var text = doc.CreateElement("t", ns);
                var preserve = doc.CreateAttribute("xml", "space", "http://www.w3.org/XML/1998/namespace");
                preserve.Value = "preserve";
                text.Attributes.Append(preserve);
                text.InnerText = values[i] ?? string.Empty;

                inlineString.AppendChild(text);
                cell.AppendChild(inlineString);
                row.AppendChild(cell);
            }

            return row;
        }

        private static void AppendRow(StringBuilder sb, int rowIndex, params string[] values)
        {
            sb.Append("<row r=\"").Append(rowIndex).Append("\">");
            for (int i = 0; i < values.Length; i++)
            {
                string cellRef = GetColumnName(i + 1) + rowIndex;
                sb.Append("<c r=\"").Append(cellRef).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                  .Append(EscapeXml(values[i] ?? string.Empty))
                  .Append("</t></is></c>");
            }
            sb.Append("</row>");
        }

        private static string GetColumnName(int index)
        {
            string name = string.Empty;
            while (index > 0)
            {
                index--;
                name = (char)('A' + (index % 26)) + name;
                index /= 26;
            }
            return name;
        }

        private static string EscapeXml(string value)
        {
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        private static string SerializeXml(XmlDocument doc)
        {
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = false,
                OmitXmlDeclaration = false,
            };

            var builder = new StringBuilder();
            using (var writer = XmlWriter.Create(builder, settings))
                doc.Save(writer);

            return builder.ToString();
        }
    }
}

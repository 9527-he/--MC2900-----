using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Threading;
using InsertFrameTest.Communication;
using InsertFrameTest.Mes;
using InsertFrameTest.Models;

namespace InsertFrameTest.Processes
{
    public abstract class ProcessBase
    {
        protected MC2900Driver    MC2900;
        protected CS9933X20Driver Safety;
        protected MesClient       Mes;
        private ProcessResult _activeResult;
        private CancellationToken _runToken = CancellationToken.None;
        private readonly object _runtimeLogLock = new object();
        private StringBuilder _runtimeLog;
        private const int MaxRuntimeLogChars = 200000;

        public int    ProcessNo   { get; protected set; }
        public string ProcessName { get; protected set; }
        protected virtual bool RequiresProgramFile => true;

        // 是否启用 MES 条码检查（默认启用）
        public bool EnableMesBarcodeCheck { get; set; } = true;

        public event Action<string> LogMessage;
        public event Action<bool>   Finished;
        // 通用的数据更新事件：code 表示数据类型，value 为字符串表示的数据
        public event Action<int, string> DataUpdate;

        // 触发数据更新事件，供派生类调用
        protected void OnDataUpdate(int code, string value)
        {
            DataUpdate?.Invoke(code, value);
        }

        protected void Log(string msg)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";

            lock (_runtimeLogLock)
            {
                if (_runtimeLog != null && _runtimeLog.Length < MaxRuntimeLogChars)
                    _runtimeLog.AppendLine(line);
            }

            LogMessage?.Invoke(line);
        }

        public bool HasActiveResult()
        {
            return _activeResult != null;
        }

        public string GetActiveBarcode()
        {
            return string.IsNullOrWhiteSpace(_activeResult?.Barcode) ? "-" : _activeResult.Barcode.Trim();
        }

        /// <summary>
        /// 工序3告警/控制记录写入 MES上传汇总.xlsx
        /// </summary>
        public void AppendAlarmCaptureToMes(string alarmName, string statusSummary, string frameText)
        {
            try
            {
                string barcode = GetActiveBarcode();
                var header = TryBuildMesHeader(barcode, "PASS");
                string path = MesUploadWorkbookWriter.SaveAlarmCaptureRecord(
                    barcode, alarmName, statusSummary, frameText, EnableMesBarcodeCheck, header);
                Log($"告警记录已写入MES上传汇总: {alarmName} -> {path}");
            }
            catch (Exception ex)
            {
                Log($"告警记录写入MES上传汇总失败: {alarmName}, {ex.Message}");
            }
        }

        /// <summary>
        /// 工序2传感器抓包写入 MES上传汇总.xlsx
        /// </summary>
        public void AppendSensorCaptureToMes(string sensorName, string statusSummary, string frameText)
        {
            try
            {
                string barcode = GetActiveBarcode();
                var header = TryBuildMesHeader(barcode, "PASS");
                string path = MesUploadWorkbookWriter.SaveSensorCaptureRecord(
                    barcode, sensorName, statusSummary, frameText, EnableMesBarcodeCheck, header);
                Log($"传感器抓包已写入MES上传汇总: {sensorName} -> {path}");
            }
            catch (Exception ex)
            {
                Log($"传感器抓包写入MES上传汇总失败: {sensorName}, {ex.Message}");
            }
        }

        private MesHeaderInfo TryBuildMesHeader(string barcode, string result)
        {
            if (!EnableMesBarcodeCheck)
                return null;

            if (Mes != null && Mes.IsLoaded)
            {
                try { return Mes.BuildHeaderInfo(barcode, result); }
                catch { }
            }

            return MesHeaderInfo.CreateEmpty(barcode, result);
        }

        public void AppendRuntimeRawRecord(int step, string title, string rawData, bool pass = true)
        {
            var activeResult = _activeResult;
            if (activeResult == null)
                return;

            activeResult.AppendRawRecord(step, title, rawData, pass);
        }

        /// <summary>
        /// 界面手动操作日志写入运行日志缓冲区（不经 LogMessage，避免与 UI 重复显示）。
        /// </summary>
        public void AppendExternalRuntimeLog(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg))
                return;

            lock (_runtimeLogLock)
            {
                if (_runtimeLog == null || _runtimeLog.Length >= MaxRuntimeLogChars)
                    return;

                string line = msg.StartsWith("[") ? msg : $"[{DateTime.Now:HH:mm:ss}] {msg}";
                _runtimeLog.AppendLine(line);
            }
        }

        public void Initialize(MesClient mes, MC2900Driver mc2900, CS9933X20Driver safety = null)
        {
            Mes    = mes;
            MC2900 = mc2900;
            Safety = safety;
        }

        public void Run(string barcode)
        {
            Run(barcode, CancellationToken.None);
        }

        // 主入口：扫码→检条码→执行→上传
        public void Run(string barcode, CancellationToken runToken)
        {
            _runToken = runToken;
            var result = new ProcessResult
            {
                Barcode    = barcode,
                ProcessNo  = ProcessNo,
                StartTime  = DateTime.Now,
            };

            _activeResult = result;
            _runtimeLog = new StringBuilder();

            try
            {
                Log($"========== 工序{ProcessNo}开始 ==========");
                Log($"条码: {barcode}");
                Log($"[调试] Safety对象: {(Safety == null ? "null" : "已设置")}, MC2900对象: {(MC2900 == null ? "null" : "已设置")}");

                if (EnableMesBarcodeCheck)
                {
                    var barcodeWatch = Stopwatch.StartNew();
                    if (!Mes.CheckBarcode(barcode))
                    {
                        barcodeWatch.Stop();
                        Log($"MES条码校验耗时: {barcodeWatch.ElapsedMilliseconds} ms");
                        Log("条码不可入测，流程终止");
                        Finished?.Invoke(false);
                        return;
                    }
                    barcodeWatch.Stop();
                    Log($"MES条码校验耗时: {barcodeWatch.ElapsedMilliseconds} ms");
                    Log("条码验证通过");
                }
                else
                {
                    Log("已关闭 MES 条码检查，继续测试");
                }

                byte[] programFile = Array.Empty<byte>();
                if (RequiresProgramFile)
                {
                    if (!EnableMesBarcodeCheck)
                    {
                        Log("已关闭 MES 检查，跳过 MES.GetProgramFile");
                    }
                    else
                    {
                        var programWatch = Stopwatch.StartNew();
                        string fileName;
                        programFile = Mes.GetProgramFile(out fileName);
                        programWatch.Stop();
                        Log($"获取测试程序耗时: {programWatch.ElapsedMilliseconds} ms");
                        Log($"获取测试程序: {fileName} ({programFile.Length} bytes)");
                    }
                }
                else
                {
                    Log("当前工序不依赖测试程序文件，跳过 MES.GetProgramFile");
                }

                // 启用相应设备的通信
                Log("[调试] 准备启用通信...");
                EnableCommunication(true);
                Log("[调试] 通信启用完成，准备执行测试...");

                Execute(barcode, programFile, result);

                // 禁用通信
                EnableCommunication(false);

                result.EndTime = DateTime.Now;
                result.DataFilePath = SaveLocalFile(result);

                if (IsStopRequested())
                {
                    Log("已收到停止请求，跳过 MES/汇总表上传以加快退出");
                    Finished?.Invoke(false);
                    return;
                }

                string overallResult = result.Pass ? "PASS" : "FAIL";
                UploadMesResult(barcode, overallResult, result.BuildDetails(), result.DataFilePath, ProcessName, result.UploadRawData);
                Log($"测试完成: {overallResult}，用时 {result.ElapsedSeconds:F1}s");
                Finished?.Invoke(result.Pass);
            }
            catch (Exception ex)
            {
                Log($"异常: {ex.Message}");
                // 确保禁用通信
                EnableCommunication(false);
                
                result.EndTime = DateTime.Now;
                result.Pass = false;
                try
                {
                    result.DataFilePath = SaveLocalFile(result);
                    if (!IsStopRequested())
                        UploadMesResult(barcode, "FAIL", result.BuildDetails(), result.DataFilePath, ProcessName, result.UploadRawData);
                    else
                        Log("已收到停止请求，跳过异常路径的 MES/汇总表上传");
                }
                catch { }
                Finished?.Invoke(false);
            }
            finally
            {
                _activeResult = null;
                _runtimeLog = null;
                _runToken = CancellationToken.None;
            }
        }

        protected bool IsStopRequested()
        {
            return _runToken.IsCancellationRequested;
        }

        /// <summary>
        /// 分片等待，确保停止请求可快速生效；返回 false 表示中途收到停止请求。
        /// </summary>
        protected bool DelayWithStopCheck(int milliseconds, int sliceMilliseconds = 50)
        {
            if (milliseconds <= 0)
                return !IsStopRequested();

            int remain = milliseconds;
            int slice = sliceMilliseconds <= 0 ? 50 : sliceMilliseconds;
            while (remain > 0)
            {
                if (IsStopRequested())
                    return false;

                int current = remain < slice ? remain : slice;
                Thread.Sleep(current);
                remain -= current;
            }

            return !IsStopRequested();
        }

        private void UploadMesResult(string barcode, string result, string details, string filePath, string deviceName, string rawData)
        {
            // 本地汇总表始终可写；MES 服务器上传仅在 MES检查=开 时执行
            try
            {
                var header = TryBuildMesHeader(barcode, result);
                string workbookPath = MesUploadWorkbookWriter.SaveUploadWorkbook(
                    barcode, deviceName, result, details, filePath, rawData, ProcessNo, EnableMesBarcodeCheck, header);
                Log($"上传快照已保存: {workbookPath}");
            }
            catch (Exception ex)
            {
                Log($"上传快照保存失败: {ex.Message}");
            }

            if (!EnableMesBarcodeCheck)
            {
                Log($"已关闭 MES 检查，跳过 SaveTestData 上传。条码={barcode}, 结果={result}");
                return;
            }

            Log($"MES上传开始: 条码={barcode}, 结果={result}");
            try
            {
                bool uploaded = Mes != null && Mes.SaveTestData(barcode, result, details, filePath);
                if (uploaded)
                    Log($"MES上传成功: 条码={barcode}, 结果={result}");
                else
                    Log($"MES上传失败: 条码={barcode}, 结果={result}");
            }
            catch (Exception ex)
            {
                Log($"MES上传失败: 条码={barcode}, 结果={result}, 原因={ex.Message}");
                // 不向上抛：避免 MES 上传失败把整次测试打成异常/弹窗打断
            }
        }

        /// <summary>
        /// 启用/禁用设备通信（支持多工序并发）
        /// </summary>
        private void EnableCommunication(bool enable)
        {
            if (ProcessNo == 1)
            {
                // 工序1使用安规仪（独占）
                if (Safety != null)
                {
                    var bus = Safety.GetBus();
                    if (bus != null)
                    {
                        Log($"[调试] 工序1通信控制: enable={enable}, 当前Enabled={bus.Enabled}, UsageCount={GetUsageCount(bus)}");
                        if (enable)
                            bus.Acquire();
                        else
                            bus.Release();
                        Log($"安规测试仪通信{(enable ? "启用" : "禁用")}, Enabled={bus.Enabled}, UsageCount={GetUsageCount(bus)}");
                    }
                    else
                    {
                        Log("[错误] Safety.GetBus() 返回 null");
                    }
                }
                else
                {
                    Log("[错误] Safety 为 null");
                }
            }
            else
            {
                // 工序2-4使用MC2900（共享，引用计数）
                if (MC2900 != null)
                {
                    var bus = MC2900.GetBus();
                    if (bus != null)
                    {
                        Log($"[调试] 工序{ProcessNo}通信控制: enable={enable}, 当前Enabled={bus.Enabled}, UsageCount={GetUsageCount(bus)}");
                        if (enable)
                            bus.Acquire();
                        else
                            bus.Release();
                        Log($"MC2900通信{(enable ? "启用" : "禁用")}, Enabled={bus.Enabled}, UsageCount={GetUsageCount(bus)}");
                    }
                    else
                    {
                        Log("[错误] MC2900.GetBus() 返回 null");
                    }
                }
                else
                {
                    Log("[错误] MC2900 为 null");
                }
            }
        }
        
        /// <summary>
        /// 获取使用计数（通过反射，用于调试）
        /// </summary>
        private int GetUsageCount(ModbusRtu bus)
        {
            try
            {
                var field = typeof(ModbusRtu).GetField("_usageCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                    return (int)field.GetValue(bus);
            }
            catch { }
            return -1;
        }

        protected abstract void Execute(string barcode, byte[] programFile, ProcessResult result);

        protected bool AddItem(ProcessResult result, int step, string name,
                               float value, float min, float max, float time = 0)
        {
            bool pass = value >= min && value <= max;
            result.Items.Add(new TestItem
            {
                Step     = step,
                Name     = name,
                MaxValue = max,
                MinValue = min,
                Value    = value,
                Pass     = pass,
                Time     = time,
            });
            Log($"  [{(pass ? "PASS" : "FAIL")}] {name}: {value} (范围:{min}~{max})");
            return pass;
        }

        protected bool AddBoolItem(ProcessResult result, int step, string name,
                                   bool pass, float time = 0)
        {
            result.Items.Add(new TestItem
            {
                Step     = step,
                Name     = name,
                MaxValue = 1,
                MinValue = 0,
                Value    = pass ? 1 : 0,
                Pass     = pass,
                Time     = time,
            });
            Log($"  [{(pass ? "PASS" : "FAIL")}] {name}");
            return pass;
        }

        private string SaveLocalFile(ProcessResult result)
        {
            string dir = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "TestData",
                $"Process{result.ProcessNo}");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir,
                $"{result.Barcode}_{result.StartTime:yyyyMMdd_HHmmss}.txt");
            using (var w = new StreamWriter(path))
            {
                w.WriteLine($"条码: {result.Barcode}");
                w.WriteLine($"工序: {result.ProcessNo}");
                w.WriteLine($"结果: {(result.Pass ? "PASS" : "FAIL")}");
                w.WriteLine($"开始: {result.StartTime:yyyy-MM-dd HH:mm:ss}");
                w.WriteLine($"结束: {result.EndTime:yyyy-MM-dd HH:mm:ss}");
                w.WriteLine("---测试项目---");
                foreach (var item in result.Items)
                    w.WriteLine($"{item.Step},{item.Name},{item.Value},{item.MinValue}," +
                                $"{item.MaxValue},{(item.Pass ? "PASS" : "FAIL")}");

                w.WriteLine("---运行日志---");
                lock (_runtimeLogLock)
                {
                    if (_runtimeLog != null)
                        w.Write(_runtimeLog.ToString());
                }

                if (!string.IsNullOrWhiteSpace(result.UploadRawData))
                {
                    w.WriteLine("---原始记录---");
                    w.WriteLine(result.UploadRawData);
                }
            }

            result.DataCsvFilePath = SaveLocalCsvFile(result);
            return path;
        }

        private string SaveLocalCsvFile(ProcessResult result)
        {
            string dir = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "TestData",
                $"Process{result.ProcessNo}");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir,
                $"{result.Barcode}_{result.StartTime:yyyyMMdd_HHmmss}.csv");

            using (var w = new StreamWriter(path, false, Encoding.UTF8))
            {
                w.WriteLine("Step,Title,RawData,Pass");
                foreach (var raw in result.RawRecords)
                {
                    string sanitizedTitle = raw.Title?.Replace("\"", "\"\"") ?? string.Empty;
                    string sanitizedRawData = raw.RawData?.Replace("\"", "\"\"") ?? string.Empty;
                    string quotedTitle = sanitizedTitle.Contains(",") || sanitizedTitle.Contains("\"" ) || sanitizedTitle.Contains("\n") || sanitizedTitle.Contains("\r")
                        ? $"\"{sanitizedTitle}\""
                        : sanitizedTitle;
                    string quotedRawData = sanitizedRawData.Contains(",") || sanitizedRawData.Contains("\"" ) || sanitizedRawData.Contains("\n") || sanitizedRawData.Contains("\r")
                        ? $"\"{sanitizedRawData}\""
                        : sanitizedRawData;
                    w.WriteLine($"{raw.Step},{quotedTitle},{quotedRawData},{(raw.Pass ? "PASS" : "FAIL")}");
                }
            }
            return path;
        }
    }
}

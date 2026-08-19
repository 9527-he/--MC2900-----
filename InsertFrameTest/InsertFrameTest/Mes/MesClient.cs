using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace InsertFrameTest.Mes
{
    // 动态加载 mes.dll，运行时不依赖编译期引用
    public class MesClient
    {
        private object _instance;
        private MethodInfo _getProgram;
        private MethodInfo _checkBarcode;
        private MethodInfo _getProgramFile;
        private MethodInfo _saveTestData;
        private MethodInfo _getTaskOrder;
        private MethodInfo _getWorker;
        private MethodInfo _getSeqTransition;
        private Assembly _assembly;
        private Type _mesType;
        private Type _testerConfigType;

        public bool IsLoaded { get; private set; }
        public string LastError { get; private set; }
        public string DebugInfo { get; private set; }
        public string LastNodeInfo { get; private set; }

        private void TraceNode(string node, bool ok, string detail)
        {
            LastNodeInfo = $"[{DateTime.Now:HH:mm:ss}] {node} | {(ok ? "PASS" : "FAIL")} | {detail}";
            System.Diagnostics.Debug.WriteLine("[MES] " + LastNodeInfo);
        }

        private object InvokeMes(MethodInfo method, string node, params object[] args)
        {
            try
            {
                return method.Invoke(_instance, args);
            }
            catch (TargetInvocationException ex)
            {
                string msg = ex.InnerException?.Message ?? ex.Message;
                TraceNode(node, false, msg);
                throw new Exception($"调用 {node} 失败: {msg}\n\n" + DebugInfo);
            }
        }

        public bool Load(string dllPath = "mes.dll")
        {
            try
            {
                string found = FindDllPath(dllPath);
                if (found == null)
                    throw new FileNotFoundException("找不到 mes.dll，请将其放在程序目录或项目根目录下 (mes.dll)");

                // 加载 DLL
                try
                {
                    _assembly = Assembly.LoadFrom(found);
                }
                catch (FileLoadException)
                {
                    string dllName = Path.GetFileNameWithoutExtension(found);
                    _assembly = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => a.GetName().Name == dllName);
                    if (_assembly == null)
                        throw;
                }

                // 获取调试信息
                var debugBuilder = new StringBuilder();
                debugBuilder.AppendLine($"DLL 路径: {found}");
                debugBuilder.AppendLine($"DLL 名称: {_assembly.GetName().Name}");
                debugBuilder.AppendLine($"DLL 版本: {_assembly.GetName().Version}");
                debugBuilder.AppendLine();

                // 查找 MES 类型
                _mesType = FindMesType(_assembly, debugBuilder);
                if (_mesType == null)
                {
                    DebugInfo = debugBuilder.ToString();
                    throw new Exception("mes.dll 中未找到包含必需方法的类。\n\n" + DebugInfo);
                }

                debugBuilder.AppendLine($"选中类型: {_mesType.FullName}");
                debugBuilder.AppendLine();

                // 查找方法（使用灵活匹配）
                FindMethodsFlexible(debugBuilder);

                DebugInfo = debugBuilder.ToString();

                // 验证至少有 CheckBarcode 和 SaveTestData 方法
                if (_checkBarcode == null)
                    throw new Exception("未找到 CheckBarcode 方法\n\n" + DebugInfo);

                // 创建实例
                _instance = Activator.CreateInstance(_mesType);
                IsLoaded = true;
                TraceNode("Load", true, "mes.dll加载成功");
                return true;
            }
            catch (ReflectionTypeLoadException ex)
            {
                var sb = new StringBuilder();
                sb.AppendLine("类型加载失败详情:");
                foreach (var loaderEx in ex.LoaderExceptions)
                {
                    sb.AppendLine($"  - {loaderEx?.Message}");
                }
                LastError = sb.ToString();
                IsLoaded = false;
                TraceNode("Load", false, LastError);
                return false;
            }
            catch (TargetInvocationException ex)
            {
                LastError = $"调用出错: {ex.InnerException?.Message ?? ex.Message}";
                IsLoaded = false;
                TraceNode("Load", false, LastError);
                return false;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                IsLoaded = false;
                TraceNode("Load", false, LastError);
                return false;
            }
        }

        private string FindDllPath(string dllPath)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string currDir = Environment.CurrentDirectory;
            string[] candidates = new string[] {
                dllPath,
                Path.Combine(baseDir, Path.GetFileName(dllPath)),
                Path.Combine(currDir, Path.GetFileName(dllPath)),
                Path.Combine(baseDir, "..", Path.GetFileName(dllPath)),
                Path.Combine(baseDir, "..", "..", Path.GetFileName(dllPath)),
                Path.Combine(baseDir, "mes.dll"),
                Path.Combine(currDir, "mes.dll")
            };

            foreach (var c in candidates)
            {
                if (string.IsNullOrWhiteSpace(c)) continue;
                try
                {
                    string fullPath = Path.GetFullPath(c);
                    if (File.Exists(fullPath))
                        return fullPath;
                }
                catch { }
            }
            return null;
        }

        private Type FindMesType(Assembly asm, StringBuilder debug)
        {
            Type[] exportedTypes;
            try
            {
                exportedTypes = asm.GetExportedTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                exportedTypes = ex.Types.Where(t => t != null).ToArray();
            }

            debug.AppendLine("DLL 中的类型:");
            foreach (var t in exportedTypes)
            {
                if (t.IsInterface || t.IsAbstract)
                    continue;

                var methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                var methodNames = methods.Select(m => m.Name).ToArray();
                
                debug.AppendLine($"  类: {t.FullName}");
                debug.AppendLine($"    方法: {string.Join(", ", methodNames)}");

                // 检查是否包含所有必需方法
                bool hasCheckBarcode = methods.Any(m => m.Name == "CheckBarcode");
                bool hasGetProgram = methods.Any(m => m.Name == "GetProgram");
                bool hasGetProgramFile = methods.Any(m => m.Name == "GetProgramFile");
                bool hasSaveTestData = methods.Any(m => m.Name == "SaveTestData");

                if (hasCheckBarcode && hasGetProgram && hasGetProgramFile && hasSaveTestData)
                {
                    debug.AppendLine($"    ✓ 包含所有必需方法");
                    return t;
                }
            }

            // 如果没有找到包含所有方法的类，退而查找包含 CheckBarcode 的类
            foreach (var t in exportedTypes)
            {
                if (t.IsInterface || t.IsAbstract)
                    continue;

                if (t.GetMethods().Any(m => m.Name == "CheckBarcode"))
                {
                    debug.AppendLine($"  → 使用包含 CheckBarcode 的类: {t.FullName}");
                    return t;
                }
            }

            return null;
        }

        private void FindMethodsFlexible(StringBuilder debug)
        {
            debug.AppendLine("方法匹配详情:");
            
            // 获取所有实例方法
            var allMethods = _mesType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => !m.IsSpecialName)
                .ToArray();

            // 1. CheckBarcode - 期望: bool CheckBarcode(string)
            var checkBarcodeMethods = allMethods.Where(m => m.Name == "CheckBarcode").ToArray();
            debug.AppendLine($"  CheckBarcode 方法数: {checkBarcodeMethods.Length}");
            foreach (var m in checkBarcodeMethods)
            {
                debug.AppendLine($"    - 返回: {m.ReturnType.Name}, 参数: {string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))}");
            }
            _checkBarcode = checkBarcodeMethods.FirstOrDefault(m => 
                m.ReturnType == typeof(bool) && 
                m.GetParameters().Length == 1 && 
                m.GetParameters()[0].ParameterType == typeof(string));
            if (_checkBarcode == null)
                _checkBarcode = checkBarcodeMethods.FirstOrDefault();
            debug.AppendLine($"    → 选中: {_checkBarcode?.Name}");

            // 2. GetProgram - 期望: string GetProgram()
            var getProgramMethods = allMethods.Where(m => m.Name == "GetProgram").ToArray();
            debug.AppendLine($"  GetProgram 方法数: {getProgramMethods.Length}");
            foreach (var m in getProgramMethods)
            {
                debug.AppendLine($"    - 返回: {m.ReturnType.Name}, 参数: {m.GetParameters().Length}");
            }
            _getProgram = getProgramMethods.FirstOrDefault(m => 
                m.ReturnType == typeof(string) && 
                m.GetParameters().Length == 0);
            if (_getProgram == null)
                _getProgram = getProgramMethods.FirstOrDefault();
            debug.AppendLine($"    → 选中: {_getProgram?.Name}");

            // 3. GetProgramFile - 期望: byte[] GetProgramFile(out string)
            var getProgramFileMethods = allMethods.Where(m => m.Name == "GetProgramFile").ToArray();
            debug.AppendLine($"  GetProgramFile 方法数: {getProgramFileMethods.Length}");
            foreach (var m in getProgramFileMethods)
            {
                var ps = m.GetParameters();
                debug.AppendLine($"    - 返回: {m.ReturnType.Name}, 参数: {string.Join(", ", ps.Select(p => $"{p.ParameterType.Name}({(p.IsOut ? "out" : p.IsRetval ? "ref" : "in")})"))}");
            }
            _getProgramFile = getProgramFileMethods.FirstOrDefault(m => 
                m.ReturnType == typeof(byte[]) && 
                m.GetParameters().Length == 1 && 
                m.GetParameters()[0].IsOut);
            if (_getProgramFile == null)
                _getProgramFile = getProgramFileMethods.FirstOrDefault();
            debug.AppendLine($"    → 选中: {_getProgramFile?.Name}");

            // 4. SaveTestData - 实际签名: bool SaveTestData(string, string, string, string, out string err)
            var saveTestDataMethods = allMethods.Where(m => m.Name == "SaveTestData").ToArray();
            debug.AppendLine($"  SaveTestData 方法数: {saveTestDataMethods.Length}");
            foreach (var m in saveTestDataMethods)
            {
                var ps = m.GetParameters();
                debug.AppendLine($"    - 返回: {m.ReturnType.Name}, 参数: {ps.Length} ({string.Join(", ", ps.Select(p => p.ParameterType.Name + (p.IsOut ? "(out)" : "")))})");
            }
            _saveTestData = saveTestDataMethods.FirstOrDefault(m =>
            {
                var ps = m.GetParameters();
                return ps.Length == 5
                    && ps[0].ParameterType == typeof(string)
                    && ps[1].ParameterType == typeof(string)
                    && ps[2].ParameterType == typeof(string)
                    && ps[3].ParameterType == typeof(string)
                    && ps[4].IsOut;
            });
            if (_saveTestData == null)
            {
                _saveTestData = saveTestDataMethods.FirstOrDefault(m =>
                    m.GetParameters().Length == 4 &&
                    m.GetParameters().All(p => p.ParameterType == typeof(string)));
            }
            if (_saveTestData == null)
                _saveTestData = saveTestDataMethods.FirstOrDefault();
            debug.AppendLine($"    → 选中: {_saveTestData?.Name} 参数数={_saveTestData?.GetParameters().Length}");

            // 5. 可选：任务/人员/工序流转
            _getTaskOrder = allMethods.FirstOrDefault(m =>
                m.Name == "GetTaskOrder" && m.GetParameters().Length == 0 && m.ReturnType == typeof(string));
            _getWorker = allMethods.FirstOrDefault(m =>
                m.Name == "GetWorker" && m.GetParameters().Length == 0 && m.ReturnType == typeof(string));
            _getSeqTransition = allMethods.FirstOrDefault(m =>
                m.Name == "Get_seq_transition" && m.GetParameters().Length == 3);

            _testerConfigType = _assembly.GetType("mes.TesterConfig");
            debug.AppendLine($"  GetTaskOrder: {_getTaskOrder != null}, GetWorker: {_getWorker != null}, Get_seq_transition: {_getSeqTransition != null}, TesterConfig: {_testerConfigType != null}");
        }

        // 弹出MES参数设置窗口
        public string GetProgram()
        {
            if (!IsLoaded)
            {
                TraceNode("GetProgram", false, "mes.dll 未加载");
                return string.Empty;
            }
            if (_getProgram == null)
            {
                TraceNode("GetProgram", false, "GetProgram 方法未找到");
                throw new Exception("GetProgram 方法未找到\n\n" + DebugInfo);
            }
            var value = (string)InvokeMes(_getProgram, "GetProgram", null);
            TraceNode("GetProgram", true, "调用成功");
            return value;
        }

        // 验条码（true=可入测）
        public bool CheckBarcode(string barcode)
        {
            if (!IsLoaded)
            {
                TraceNode("CheckBarcode", false, "mes.dll 未加载");
                throw new Exception("mes.dll 未加载，无法执行 CheckBarcode");
            }
            if (_checkBarcode == null)
            {
                TraceNode("CheckBarcode", false, "CheckBarcode 方法未找到");
                throw new Exception("CheckBarcode 方法未找到\n\n" + DebugInfo);
            }

            bool canTest = (bool)InvokeMes(_checkBarcode, "CheckBarcode", barcode);
            TraceNode("CheckBarcode", canTest, canTest ? "条码可入测" : "条码不可入测");
            return canTest;
        }

        // 获取归档测试程序
        public byte[] GetProgramFile(out string file_name)
        {
            file_name = string.Empty;
            if (!IsLoaded)
            {
                TraceNode("GetProgramFile", false, "mes.dll 未加载");
                return new byte[0];
            }
            if (_getProgramFile == null)
            {
                TraceNode("GetProgramFile", false, "GetProgramFile 方法未找到");
                // 如果没有 GetProgramFile，返回空数组
                return new byte[0];
            }
            // 处理 out 参数
            var parameters = _getProgramFile.GetParameters();
            if (parameters.Length == 1 && parameters[0].IsOut)
            {
                object[] args = new object[] { null };
                byte[] data = (byte[])InvokeMes(_getProgramFile, "GetProgramFile", args);
                file_name = (string)args[0];
                var finalData = data ?? new byte[0];
                TraceNode("GetProgramFile", true, $"成功，文件名={file_name ?? ""}，长度={finalData.Length}");
                return finalData;
            }
            else
            {
                // 非 out 参数的情况
                object[] args = new object[parameters.Length];
                byte[] data = (byte[])InvokeMes(_getProgramFile, "GetProgramFile", args);
                if (parameters.Length > 0 && args[0] != null)
                    file_name = args[0].ToString();
                var finalData = data ?? new byte[0];
                TraceNode("GetProgramFile", true, $"成功，文件名={file_name ?? ""}，长度={finalData.Length}");
                return finalData;
            }
        }

        // 上传测试结果
        public bool SaveTestData(string barcode, string result, string details, string file)
        {
            if (!IsLoaded)
            {
                TraceNode("SaveTestData", false, "mes.dll 未加载");
                return false;
            }
            if (_saveTestData == null)
            {
                TraceNode("SaveTestData", false, "SaveTestData 方法未实现");
                System.Diagnostics.Debug.WriteLine($"MES SaveTestData 未实现，本地结果: {barcode} = {result}");
                return false;
            }

            var parameters = _saveTestData.GetParameters();
            if (parameters.Length == 5 && parameters[4].IsOut)
            {
                object[] args = { barcode, result, details, file, null };
                object ret = InvokeMes(_saveTestData, "SaveTestData", args);
                string err = args[4] as string ?? string.Empty;
                bool ok = ret is bool b ? b : string.IsNullOrWhiteSpace(err);
                if (!ok)
                {
                    TraceNode("SaveTestData", false, string.IsNullOrWhiteSpace(err) ? "上传失败" : err);
                    throw new Exception(string.IsNullOrWhiteSpace(err) ? "MES SaveTestData 返回失败" : err);
                }
                TraceNode("SaveTestData", true, $"上传成功，barcode={barcode}，result={result}");
                return true;
            }

            InvokeMes(_saveTestData, "SaveTestData", barcode, result, details, file);
            TraceNode("SaveTestData", true, $"上传成功，barcode={barcode}，result={result}");
            return true;
        }

        /// <summary>
        /// 读取 MES 参数配置，组装作业头信息（对应 MES 界面顶部参数区）。
        /// </summary>
        public MesHeaderInfo BuildHeaderInfo(string barcode, string result)
        {
            var header = MesHeaderInfo.CreateEmpty(barcode, result);
            if (!IsLoaded)
                return header;

            try
            {
                object config = TryLoadTesterConfig();
                if (config != null)
                {
                    header.JobId = ReadConfigString(config, "task_id");
                    header.TaskOrder = FirstNonEmpty(ReadConfigString(config, "TaskOrder"), TryInvokeString(_getTaskOrder, "GetTaskOrder"));
                    header.ProductCode = ReadConfigString(config, "ItemCode");
                    header.ProductDesc = ReadConfigString(config, "ItemName");
                    header.Operator = FirstNonEmpty(
                        ReadConfigString(config, "Worker"),
                        ReadConfigString(config, "UserCode"),
                        TryInvokeString(_getWorker, "GetWorker"));
                    header.ProductionLine = ReadConfigString(config, "WorkLine");
                    header.Shift = ReadConfigString(config, "WorkShift");
                    header.Foreman = ReadConfigString(config, "WorkLeader");
                    header.PrevProcess = FirstNonEmpty(
                        ReadConfigString(config, "Sequence"),
                        ReadConfigString(config, "WorkStation"));

                    string itemCode = header.ProductCode;
                    string itemName = header.ProductDesc;
                    string workType = ReadConfigString(config, "work_type");
                    if (!string.IsNullOrWhiteSpace(itemCode) || !string.IsNullOrWhiteSpace(itemName))
                        header.ProcessFlow = string.IsNullOrWhiteSpace(itemCode)
                            ? itemName
                            : (string.IsNullOrWhiteSpace(itemName) ? itemCode : itemCode + " - " + itemName);
                    else
                        header.ProcessFlow = workType;

                    string seqId = ReadConfigString(config, "seq_id");
                    if (string.IsNullOrWhiteSpace(seqId))
                        seqId = ReadConfigString(config, "processID");
                    if (!string.IsNullOrWhiteSpace(seqId) && _getSeqTransition != null)
                    {
                        try
                        {
                            object[] args = { seqId, null, null };
                            InvokeMes(_getSeqTransition, "Get_seq_transition", args);
                            string passTo = args[1] as string ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(passTo))
                                header.NextProcess = passTo.Trim();
                        }
                        catch
                        {
                        }
                    }
                }
                else
                {
                    header.TaskOrder = TryInvokeString(_getTaskOrder, "GetTaskOrder");
                    header.Operator = TryInvokeString(_getWorker, "GetWorker");
                }
            }
            catch (Exception ex)
            {
                TraceNode("BuildHeaderInfo", false, ex.Message);
            }

            return header;
        }

        private object TryLoadTesterConfig()
        {
            if (_testerConfigType == null)
                return null;

            try
            {
                var load = _testerConfigType.GetMethod("LoadConfig", Type.EmptyTypes);
                if (load == null)
                    return null;
                return load.Invoke(null, null);
            }
            catch
            {
                return null;
            }
        }

        private static string ReadConfigString(object config, string propertyName)
        {
            if (config == null || string.IsNullOrWhiteSpace(propertyName))
                return string.Empty;
            try
            {
                var prop = config.GetType().GetProperty(propertyName);
                if (prop == null)
                    return string.Empty;
                object value = prop.GetValue(config, null);
                return value?.ToString()?.Trim() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string TryInvokeString(MethodInfo method, string node)
        {
            if (!IsLoaded || method == null)
                return string.Empty;
            try
            {
                object value = InvokeMes(method, node, null);
                return value?.ToString()?.Trim() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
                return string.Empty;
            foreach (string v in values)
            {
                if (!string.IsNullOrWhiteSpace(v))
                    return v.Trim();
            }
            return string.Empty;
        }

        // 构造 details 字符串的辅助方法
        // 格式：,,,,step序号,maxvalue,minvalue,value,result,testname,time\n
        // testname 内逗号/换行会破坏 MES 字段计数，需清洗。
        public static string BuildDetail(int step, float maxVal, float minVal,
                                         float value, bool pass, string testName, float time)
        {
            string r = pass ? "PASS" : "FAIL";
            string safeName = SanitizeDetailField(testName);
            return $",,,,{step},{maxVal},{minVal},{value},{r},{safeName},{time:F1}\n";
        }

        public static string SanitizeDetailField(string text)
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
    }
}

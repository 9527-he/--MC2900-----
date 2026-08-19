using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Globalization;
using InsertFrameTest.Communication;
using InsertFrameTest.Processes;
using InsertFrameTest.Mes;

namespace InsertFrameTest.Forms
{
    public class MainForm : Form
    {
        // ── 硬件连接 ──────────────────────────────────────────────
        private ModbusRtu       _safetyBus  = new ModbusRtu { PortType = ModbusDataLogger.PortType.Safety };
        private ModbusRtu       _mc2900Bus  = new ModbusRtu { PortType = ModbusDataLogger.PortType.MC2900 };
        private CS9933X20Driver _safety;
        private MC2900Driver    _mc2900;
        private MesClient       _mes        = new MesClient();

        // ── 4个工序 ───────────────────────────────────────────────
        private ProcessBase[] _processes;

        // ── 顶部控件 ──────────────────────────────────────────────
        private ComboBox  _cmbSafetyPort, _cmbSafetyBaud;
        private Button    _btnSafetyConnect;
        private Label     _lblSafetyStatus;
        private ComboBox  _cmbMcPort, _cmbMcBaud;
        private Button    _btnMcConnect;
        private Label     _lblMcStatus;
        private Button    _btnMesParams;
        private Label     _lblMesStatus;
        private TabControl _tabProcesses;
        // 全局 MES 检查开关控件与状态
        private Button    _btnMesCheckToggle;
        private bool      _mesCheckEnabled = true;

        // ── 每个工序Tab的控件集 ───────────────────────────────────
        private TextBox[] _txtBarcode   = new TextBox[4];
        private Button[]  _btnStart     = new Button[4];
        private Button[]  _btnStop      = new Button[4];
        private RichTextBox[] _rtbLog   = new RichTextBox[4];
        private TextBox[,] _txtSafetyMeasure = new TextBox[5, 2];
        private Button[] _btnSafetyStandaloneTest = new Button[2];
        private bool _safetyStandaloneRunning = false;
        // 工序2 显示控件：三组温度、湿度传感器4、时间校准与模块按钮
        private TextBox[] _txtTemps = new TextBox[3];
        private TextBox _txtHumidity;
        private TextBox _txtPcTime;
        private TextBox _txtDeviceTime;
        private Button  _btnSyncTime;
        private Label   _lblModuleTestStatus;
        private Button  _btnModuleAllOff;
        private Button  _btnModuleAllOn;
        private const int ModuleButtonCount = 32;
        private Button[] _btnModule = new Button[ModuleButtonCount];
        private System.Windows.Forms.Timer _moduleRefreshTimer;
        private volatile int _moduleRefreshRunning = 0;
        private bool _moduleTestActive = false;
        private volatile int _moduleControlRunning = 0;
        private readonly bool[] _moduleOnlineStates = new bool[ModuleButtonCount];
        private readonly bool[] _modulePowerStates = new bool[ModuleButtonCount];
        private int _onlineModuleCount = 0;
        /// <summary>已查询到在线模块数量后，不再继续发送在线查询报文。</summary>
        private bool _moduleCountResolved = false;
        private const int ModuleBatchStartDelayMs = 3000;
        private CancellationTokenSource _mcBackgroundCts = new CancellationTokenSource();
        private Label[]   _lblResult    = new Label[4];
        private Label[]   _lblRunStatus = new Label[4]; // 各工序运行状态

        // 工序3（告警测试）控件
        private Label[] _lblSpdAlarms = new Label[2];       // 0=AC SPD 告警, 1=DC SPD Alarm（暂共用原防雷器位）
        private Label[] _lblBatFuseAlarms = new Label[8];   // BAT1~BAT8（暂共用原蓄电池熔丝位）
        private Label[] _lblLoadFuseAlarms = new Label[4];  // LLVD1~LLVD4（暂共用原负载熔丝位）
        private Label[] _lblDiAlarms = new Label[8];
        private Label[] _lblDoAlarms = new Label[8];
        private Label[] _lblDoStates = new Label[8];
        private Button[] _btnDoToggle = new Button[8];
        private Button _btnDoBatchOpen;
        private Button _btnDoBatchClose;
        private Label[] _lblLvdAlarms = new Label[4];
        private Label[] _lblLvdStates = new Label[4];
        private Button[] _btnLvdToggle = new Button[4];
        private Label _lblBlvoAlarm;
        private Label _lblBlvdState;
        private Button _btnBlvdToggle;
        private readonly bool[] _doOpenStates = new bool[8];
        private readonly bool[] _lvdOffStates = new bool[4];
        private bool _blvdOffState;
        private volatile int _process3ControlRunning = 0;

        // 工序4 参数校准控件
        private TextBox[] _txtParamCurrent;
        private TextBox[] _txtParamTarget;
        private TextBox[] _txtParamPoint;
        private Button[] _btnParamSave;
        private Button[] _btnParamReset;
        // 工序4 设备IP输入框
        private TextBox _txtDeviceIp;
        private Button _btnDeviceIpConfirm;
        private TextBox _txtSnmpReadCommunity;
        private TextBox _txtSnmpWriteCommunity;
        private bool _deviceIpLocked;
        private string _lockedDeviceIp = string.Empty;
        private string _lockedSnmpReadCommunity = SnmpV2cClient.DefaultCommunity;
        private string _lockedSnmpWriteCommunity = SnmpV2cClient.DefaultWriteCommunity;
        private CancellationTokenSource _snmpPollCts;
        private readonly object _snmpPollLock = new object();
        private const int SnmpPollIntervalMs = 3000;
        private bool _paramCalibrationManualMode = false;
        private readonly object _process4LogLock = new object();
        private StringBuilder _process4LogBuffer;
        private string _process4LogPath;
        private string _process4Barcode = string.Empty;
        private DateTime _process4StartTime;
        private int _process4SaveOkCount;
        private int _process4SaveFailCount;
        private readonly List<string> _process4SaveDetails = new List<string>();
        private string _process2PendingStartRaw;
        private Button _btnClearRecord;
        private Button _btnClearModuleAlarmRecord;

        // Modbus监控窗口
        private ModbusMonitorForm _modbusMonitor;
        // 本窗口内南向485串口调试控件与串口对象
        private ComboBox _cmbSouthPorts;
        private ComboBox _cmbSouthBaud;
        private Button _btnSouthRefresh;
        private Button _btnSouthConnect;
        private TextBox _txtSouthSend;
        private Button _btnSouthSend;
        private RichTextBox _rtbSouthLog;
        private Button _btnSouthClear;  // 清空南向日志按钮
        private ToolStripStatusLabel _lblSouthSerialStatus;
        private SerialPort _southSerialPort;
        private const int SouthResponseTimeoutMs = 2000;
        private readonly Queue<PendingSouthTx> _southPendingTxQueue = new Queue<PendingSouthTx>();
        private readonly object _southPendingTxLock = new object();
        // 保留独立窗口类（SerialDebugForm）但不用于打开
        
        // 串口自动刷新定时器
        private System.Windows.Forms.Timer _portRefreshTimer;
        // MES节点状态刷新定时器
        private System.Windows.Forms.Timer _mesStatusTimer;
        private string _lastMesPopupInfo;

        // 工序运行状态控制
        private int _currentRunningProcess = -1;  // 当前运行的工序索引，-1表示无
        private bool _stopRequested = false;      // 停止请求标志
        private CancellationTokenSource _processStopCts;  // 工序停止取消令牌源

        private static class UiTheme
        {
            public const string FontFamily = "微软雅黑";
            public const float FontSizeNormal = 9f;
            public const float FontSizeButton = 10f;
            public const float FontSizeResult = 11f;
            public static readonly Color PrimaryBlue = Color.FromArgb(0, 120, 215);
            public static readonly Color DangerRed = Color.FromArgb(196, 43, 28);
            public static readonly Color SuccessGreen = Color.LimeGreen;
            public static readonly Color WarningOrange = Color.Orange;
            public static readonly Color IdleGray = Color.Gray;
            public static readonly Color ConnectedGreen = Color.LimeGreen;
            public static readonly Color DisconnectedRed = Color.OrangeRed;
            public static readonly Color ResultBorder = Color.FromArgb(180, 180, 180);
        }

        private sealed class PendingSouthTx
        {
            public PendingSouthTx(string txHex)
            {
                TxHex = txHex ?? string.Empty;
            }

            public string TxHex { get; }
        }

        public MainForm()
        {
            Text            = "插框测试上位机 v1.0";
            Size            = new Size(900, 680);
            StartPosition   = FormStartPosition.CenterScreen;
            MinimumSize     = new Size(800, 600);
            Font            = new Font(UiTheme.FontFamily, UiTheme.FontSizeNormal);

            // 使用字体缩放以便控件随窗体/字体变化进行缩放
            this.AutoScaleMode = AutoScaleMode.Font;

            BuildUI();
            InitProcesses();
            ApplyMesCheckToAllProcesses();
            RefreshPorts();
            
            // 启动串口自动刷新定时器（每2秒扫描一次）
            StartPortRefreshTimer();
            StartMesStatusTimer();

            FormClosing += (s, e) =>
            {
                CancelMcBackgroundOperations();
                _moduleRefreshTimer?.Stop();
                _portRefreshTimer?.Stop();
                _mesStatusTimer?.Stop();
                StopSnmpPolling();

                // 先 Pause，再关口：避免 Close 与进行中的 Read 互锁卡死 UI
                try { _mc2900Bus?.Pause(); } catch { }
                try { _safetyBus?.Pause(); } catch { }
                try { _safetyBus?.Close(); } catch { }
                try { _mc2900Bus?.Close(); } catch { }
                try { _southSerialPort?.Close(); } catch { }

                // 关闭时不做同步 Excel 重写，仅尽量落盘文本日志，防止关窗卡死
                try { FlushProcess4LogToFile(); } catch { }
            };
        }
        
        /// <summary>
        /// 启动串口自动刷新定时器
        /// </summary>
        private void StartPortRefreshTimer()
        {
            _portRefreshTimer = new System.Windows.Forms.Timer { Interval = 2000 }; // 2秒
            _portRefreshTimer.Tick += (s, e) =>
            {
                // 只有在未连接时才自动刷新
                if (!_safetyBus.IsOpen && !_mc2900Bus.IsOpen)
                {
                    RefreshPorts();
                }
            };
            _portRefreshTimer.Start();
        }

        /// <summary>
        /// 定时刷新MES节点状态到主界面；仅在 MES检查=开 时对失败节点弹窗提示
        /// </summary>
        private void StartMesStatusTimer()
        {
            _mesStatusTimer = new System.Windows.Forms.Timer { Interval = 300 };
            _mesStatusTimer.Tick += (s, e) =>
            {
                string info = _mes?.LastNodeInfo;
                if (string.IsNullOrWhiteSpace(info)) return;

                bool ok = info.IndexOf("| PASS |", StringComparison.OrdinalIgnoreCase) >= 0;
                SetMesStatus(ok, info);

                // MES检查关闭时屏蔽所有 MES 失败弹窗
                if (!_mesCheckEnabled)
                    return;

                // 仅更新状态栏，禁止在 300ms 定时器里 MessageBox（会堵住 UI 消息泵导致卡死）
                if (!ok && info != _lastMesPopupInfo)
                    _lastMesPopupInfo = info;
            };
            _mesStatusTimer.Start();
        }

        // ════════════════════════════════════════════════════════
        //  UI 构建
        // ════════════════════════════════════════════════════════
        private void BuildUI()
        {
            // 顶部连接面板
            var pnlTop = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 100,
                BackColor = Color.FromArgb(30, 80, 120),
                Padding   = new Padding(8),
            };

            // 安规仪连接组
            var grpSafety = MakeGroupBox("安规测试仪 (工序1)", 5, 5, 270, 88, Color.White);
            pnlTop.Controls.Add(grpSafety);

            var tblSafety = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
            tblSafety.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tblSafety.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            tblSafety.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tblSafety.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            _cmbSafetyPort = MakeComboBox(0,0,120); // location ignored
            _cmbSafetyBaud = MakeComboBox(0,0,80);
            _cmbSafetyBaud.Items.AddRange(new object[]{"9600","19200","38400"});
            _cmbSafetyBaud.SelectedIndex = 0;
            _btnSafetyConnect = MakeButton("连接", 0,0,60, Color.LimeGreen);
            _lblSafetyStatus  = MakeLabel("未连接", 0,28,240, Color.OrangeRed);
            _btnSafetyConnect.Click += BtnSafetyConnect_Click;
            tblSafety.Controls.Add(new Label { Text = "端口:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            tblSafety.Controls.Add(_cmbSafetyPort, 1, 0);
            tblSafety.Controls.Add(new Label { Text = "波特率:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 2, 0);
            tblSafety.Controls.Add(_cmbSafetyBaud, 3, 0);
            tblSafety.Controls.Add(_btnSafetyConnect, 2, 1);
            tblSafety.Controls.Add(_lblSafetyStatus, 3, 1);
            grpSafety.Controls.Add(tblSafety);

            // MC2900连接组
            var grpMc = MakeGroupBox("MC2900 (工序2-4)", 285, 5, 270, 88, Color.White);
            pnlTop.Controls.Add(grpMc);

            var tblMc = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
            tblMc.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tblMc.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            tblMc.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tblMc.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            _cmbMcPort = MakeComboBox(0,0,120);
            _cmbMcBaud = MakeComboBox(0,0,80);
            _cmbMcBaud.Items.AddRange(new object[]{"9600","19200","38400"});
            _cmbMcBaud.SelectedIndex = 0;
            _btnMcConnect  = MakeButton("连接", 0,0,60, Color.LimeGreen);
            _lblMcStatus   = MakeLabel("未连接", 0,28,240, Color.OrangeRed);
            _btnMcConnect.Click += BtnMcConnect_Click;
            tblMc.Controls.Add(new Label { Text = "端口:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            tblMc.Controls.Add(_cmbMcPort, 1, 0);
            tblMc.Controls.Add(new Label { Text = "波特率:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 2, 0);
            tblMc.Controls.Add(_cmbMcBaud, 3, 0);
            tblMc.Controls.Add(_btnMcConnect, 2, 1);
            tblMc.Controls.Add(_lblMcStatus, 3, 1);
            grpMc.Controls.Add(tblMc);

            // MES组
            var grpMes = MakeGroupBox("MES系统", 565, 5, 300, 88, Color.White);
            pnlTop.Controls.Add(grpMes);

            var tblMes = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            tblMes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            tblMes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            tblMes.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            tblMes.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            
            _btnMesParams = MakeButton("MES参数设置", 0,0,120, Color.DodgerBlue);
            _btnMesParams.ForeColor = Color.White;
            _btnMesParams.Click += BtnMesParams_Click;
            _lblMesStatus = MakeLabel("mes.dll 未加载", 0,28,280, Color.OrangeRed);
            
            // 数据按钮 - 放在MES参数设置下方，与串口调试共用一个宿主面板
            var btnMonitor = new Button
            {
                Text = "数据监控",
                AutoSize = true,
                BackColor = Color.FromArgb(60, 120, 180),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("微软雅黑", 9f),
                Margin = new Padding(3)
            };
            btnMonitor.Click += (s, e) => ShowModbusMonitor();

            var hostFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            hostFlow.Controls.Add(btnMonitor);

            // 全工序统一 MES 检查开关按钮
            _btnMesCheckToggle = new Button
            {
                Name = "btnMesCheckToggle",
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("微软雅黑", 9f),
                ForeColor = Color.White,
                Margin = new Padding(3)
            };
            _btnMesCheckToggle.Click += BtnMesCheckToggle_Click;

            tblMes.Controls.Add(_btnMesParams, 0, 0);
            tblMes.Controls.Add(_lblMesStatus, 1, 0);
            tblMes.Controls.Add(hostFlow, 0, 1);  // 放在MES参数设置下方，包含 Modbus监控 与 串口调试
            tblMes.Controls.Add(_btnMesCheckToggle, 1, 1); // 右下角: MES检查开关
            grpMes.Controls.Add(tblMes);
            UpdateMesCheckToggleButtonUi();

            // 刷新端口按钮
            var btnRefresh = MakeButton("↺", 5, 5, 30, Color.Gray);
            btnRefresh.ForeColor = Color.White;
            btnRefresh.Click += (s, e) => RefreshPorts();
            pnlTop.Controls.Add(btnRefresh);

            // Tab页（Fill先加入Form，再加pnlTop，保证Dock布局正确）
            _tabProcesses = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("微软雅黑", 9f),
            };
            _tabProcesses.Selecting += ProcessTab_Selecting;
            string[] names = { "工序1 安规测试", "工序2 传感器/时间/模块",
                                "工序3 告警测试", "工序4 参数校准" };
            for (int i = 0; i < 4; i++)
                _tabProcesses.TabPages.Add(BuildProcessTab(i, names[i]));

            // Fill控件先加，Top控件后加
            Controls.Add(_tabProcesses);
            Controls.Add(pnlTop);

            // 加载mes.dll
            if (_mes.Load())
                SetMesStatus(true, "mes.dll 已加载");
            else
                SetMesStatus(false, "mes.dll: " + _mes.LastError);
        }

        private TabPage BuildProcessTab(int idx, string title)
        {
            var page = new TabPage(title) { Padding = new Padding(0) };

            // 顶部控件条（固定高度，DockStyle.Top）
            var strip = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 44,
                BackColor = Color.FromArgb(245, 245, 245),
            };

            // 使用 TableLayoutPanel 使顶部控件随宽度自适应
            var topLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Height = 44,
                ColumnCount = 10,
                RowCount = 1,
                Padding = new Padding(0),
                Margin = new Padding(0),
            };
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // 标签
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180)); // 条码输入（固定宽度 180，支持18位）
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80)); // 开始按钮
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60)); // 停止按钮
            // 仅工序4占用「结果:」与结果徽章列；工序1~3这两列宽度为0
            topLayout.ColumnStyles.Add(idx == 3
                ? new ColumnStyle(SizeType.AutoSize)
                : new ColumnStyle(SizeType.Absolute, 0));
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, idx == 3 ? 180 : 0));
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80)); // 运行状态
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // 右侧占位
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); // 清除记录
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140)); // 清除模块告警记录

            var lblBarcode = new Label { Text = "条码:", AutoSize = true, TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill, Font = new Font("微软雅黑", 9f) };

            _txtBarcode[idx] = MakeTextBox(180);
            _txtBarcode[idx].KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) StartProcess(idx); };
            if (idx == 3)
            {
                int processIndex = idx;
                _txtBarcode[idx].TextChanged += (s, e) => UpdateProcess4StartButtonState();
            }

            _btnStart[idx] = new Button { Text = "开始测试", BackColor = UiTheme.PrimaryBlue, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Dock = DockStyle.Fill, Font = new Font(UiTheme.FontFamily, UiTheme.FontSizeButton), Enabled = idx != 3 };
            ApplyButtonStyle(_btnStart[idx], UiTheme.PrimaryBlue);
            _btnStart[idx].Click += (s, e) => StartProcess(idx);

            _btnStop[idx] = new Button { Text = "停止", BackColor = UiTheme.DangerRed, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Enabled = false, Dock = DockStyle.Fill, Font = new Font(UiTheme.FontFamily, UiTheme.FontSizeButton) };
            ApplyButtonStyle(_btnStop[idx], UiTheme.DangerRed);
            _btnStop[idx].Click += (s, e) => StopProcess(idx);

            // 结果标签对象保留（供 SetResult 调用），仅工序4加入顶栏显示
            _lblResult[idx] = new Label
            {
                Text = "等待测试",
                Font = new Font(UiTheme.FontFamily, UiTheme.FontSizeResult, FontStyle.Bold),
                ForeColor = UiTheme.IdleGray,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                BackColor = GetSoftStatusBackColor(UiTheme.IdleGray),
                BorderStyle = BorderStyle.FixedSingle,
                AutoEllipsis = true,
                Margin = new Padding(2, 6, 6, 6),
                Visible = idx == 3,
            };
            
            // 运行状态标签
            _lblRunStatus[idx] = new Label 
            { 
                Text = "● 空闲", 
                Font = new Font(UiTheme.FontFamily, UiTheme.FontSizeNormal, FontStyle.Bold), 
                ForeColor = UiTheme.IdleGray, 
                TextAlign = ContentAlignment.MiddleCenter, 
                Dock = DockStyle.Fill 
            };

            topLayout.Controls.Add(lblBarcode, 0, 0);
            topLayout.Controls.Add(_txtBarcode[idx], 1, 0);
            topLayout.Controls.Add(_btnStart[idx], 2, 0);
            topLayout.Controls.Add(_btnStop[idx], 3, 0);

            if (idx == 3)
            {
                var lblResultTitle = new Label
                {
                    Text = "结果:",
                    AutoSize = true,
                    TextAlign = ContentAlignment.MiddleRight,
                    Dock = DockStyle.Fill,
                    Font = new Font(UiTheme.FontFamily, UiTheme.FontSizeNormal, FontStyle.Bold),
                    ForeColor = Color.FromArgb(60, 60, 60),
                    Margin = new Padding(8, 0, 2, 0),
                };
                topLayout.Controls.Add(lblResultTitle, 4, 0);
                topLayout.Controls.Add(_lblResult[idx], 5, 0);
            }

            topLayout.Controls.Add(_lblRunStatus[idx], 6, 0);

            if (idx == 2)
            {
                _btnClearRecord = new Button
                {
                    Text = "清除记录",
                    Dock = DockStyle.Fill,
                    Font = new Font(UiTheme.FontFamily, UiTheme.FontSizeNormal),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = UiTheme.PrimaryBlue,
                    ForeColor = Color.White,
                    Enabled = false,
                    Margin = new Padding(4, 4, 4, 4),
                };
                ApplyButtonStyle(_btnClearRecord, UiTheme.PrimaryBlue);

                _btnClearModuleAlarmRecord = new Button
                {
                    Text = "清除模块告警记录",
                    Dock = DockStyle.Fill,
                    Font = new Font(UiTheme.FontFamily, UiTheme.FontSizeNormal),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = UiTheme.PrimaryBlue,
                    ForeColor = Color.White,
                    Enabled = false,
                    Margin = new Padding(4, 4, 8, 4),
                };
                ApplyButtonStyle(_btnClearModuleAlarmRecord, UiTheme.PrimaryBlue);

                _btnClearRecord.Click += BtnClearRecord_Click;
                _btnClearModuleAlarmRecord.Click += BtnClearModuleAlarmRecord_Click;

                topLayout.Controls.Add(_btnClearRecord, 8, 0);
                topLayout.Controls.Add(_btnClearModuleAlarmRecord, 9, 0);
            }

            strip.Controls.Add(topLayout);

            // 对于工序1（安规测试），不使用黑色日志框，而在下方布局显示四个安规测试数据框
            if (idx == 0)
            {
                var panel = new Panel
                {
                    Dock = DockStyle.Fill,
                    Padding = new Padding(20, 20, 40, 20),
                    AutoScroll = true,
                };

                int leftX = 40;
                int leftLabelWidth = 110;
                int leftBoxX = leftX + leftLabelWidth;
                int rightX = 390;
                int buttonX = 635;
                int rightLabelWidth = 100;
                int rightBoxX = rightX + rightLabelWidth;
                int boxWidth = 110;
                int currentY = 20;

                Action<string> addSectionTitle = text =>
                {
                    var titleLabel = new Label
                    {
                        Text = text,
                        AutoSize = true,
                        Location = new Point(leftX, currentY),
                        Font = new Font("微软雅黑", 10f, FontStyle.Bold),
                        ForeColor = Color.FromArgb(30, 80, 120),
                    };
                    panel.Controls.Add(titleLabel);
                    currentY += 36;
                };

                Action<int, string, string, bool> addMeasureRow = (row, leftLabelText, rightLabelText, withStartButton) =>
                {
                    var lblLeft = new Label
                    {
                        Text = leftLabelText,
                        AutoSize = true,
                        Location = new Point(leftX, currentY + 5),
                        Font = new Font("微软雅黑", 9f),
                    };
                    var tbLeft = new TextBox
                    {
                        ReadOnly = true,
                        Font = new Font("Consolas", 11f),
                        BorderStyle = BorderStyle.FixedSingle,
                        BackColor = Color.White,
                        TextAlign = HorizontalAlignment.Center,
                        Width = boxWidth,
                        Height = 25,
                        Location = new Point(leftBoxX, currentY),
                    };
                    _txtSafetyMeasure[row, 0] = tbLeft;

                    var lblRight = new Label
                    {
                        Text = rightLabelText,
                        AutoSize = true,
                        Location = new Point(rightX, currentY + 5),
                        Font = new Font("微软雅黑", 9f),
                    };
                    var tbRight = new TextBox
                    {
                        ReadOnly = true,
                        Font = new Font("Consolas", 11f),
                        BorderStyle = BorderStyle.FixedSingle,
                        BackColor = Color.White,
                        TextAlign = HorizontalAlignment.Center,
                        Width = boxWidth,
                        Height = 25,
                        Location = new Point(rightBoxX, currentY),
                    };
                    _txtSafetyMeasure[row, 1] = tbRight;

                    panel.Controls.Add(lblLeft);
                    panel.Controls.Add(tbLeft);
                    panel.Controls.Add(lblRight);
                    panel.Controls.Add(tbRight);
                    if (withStartButton)
                    {
                        int buttonIndex = row == 2 ? 0 : 1;
                        var btnStartSingle = new Button
                        {
                            Text = "启动测试",
                            Size = new Size(90, 28),
                            Location = new Point(buttonX, currentY - 1),
                            Font = new Font("微软雅黑", 9f),
                            FlatStyle = FlatStyle.Flat,
                            BackColor = Color.FromArgb(0, 120, 215),
                            ForeColor = Color.White,
                            Tag = row,
                            Enabled = false,
                        };
                        btnStartSingle.Click += BtnSafetyStandaloneTest_Click;
                        _btnSafetyStandaloneTest[buttonIndex] = btnStartSingle;
                        panel.Controls.Add(btnStartSingle);
                    }
                    currentY += 42;
                };

                addSectionTitle("绝缘测试");
                addMeasureRow(0, "绝缘电压(KV)", "阻值(mΩ)", false);

                currentY += 8;
                addSectionTitle("耐压测试");
                addMeasureRow(1, "交流对地(KV)", "漏电流(mA)", false);
                addMeasureRow(2, "交流对直流(KV)", "漏电流(mA)", true);
                addMeasureRow(3, "直流对地(KV)", "漏电流(mA)", true);

                currentY += 8;
                addSectionTitle("接地电阻测试");
                addMeasureRow(4, "接地电流(A)", "电阻(mΩ)", false);

                page.Controls.Add(panel);
                page.Controls.Add(strip);
            }
            else if (idx == 1)
            {
                // 工序2自定义界面：三组温度 + 时间校准 + 模块按钮 + 南向485测试
                var panel2 = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10), AutoScroll = true };

                // 三组温度 + 湿度传感器4（横向）
                int grpWidth = 220;
                int grpHeight = 90;
                for (int g = 0; g < 3; g++)
                {
                    var grp = new GroupBox
                    {
                        Text = $"传感器 {g + 1}",
                        Width = grpWidth,
                        Height = grpHeight,
                        Font = new Font("微软雅黑", 9f),
                        Location = new Point(20 + g * (grpWidth + 15), 10),
                    };

                    var lblT = new Label
                    {
                        Text = "温度(°C)",
                        Location = new Point(8, 22),
                        AutoSize = true,
                        Font = new Font("微软雅黑", 9f),
                    };
                    var tbT = new TextBox
                    {
                        ReadOnly = true,
                        Location = new Point(8, 40),
                        Size = new Size(120, 25),
                        Font = new Font("Consolas", 11f),
                        BorderStyle = BorderStyle.FixedSingle,
                        BackColor = Color.White,
                        TextAlign = HorizontalAlignment.Center,
                    };

                    _txtTemps[g] = tbT;
                    grp.Controls.AddRange(new Control[] { lblT, tbT });
                    panel2.Controls.Add(grp);
                }

                var grpHum = new GroupBox
                {
                    Text = "传感器 4",
                    Width = grpWidth,
                    Height = grpHeight,
                    Font = new Font("微软雅黑", 9f),
                    Location = new Point(20 + 3 * (grpWidth + 15), 10),
                };
                var lblH = new Label
                {
                    Text = "湿度(%RH)",
                    Location = new Point(8, 22),
                    AutoSize = true,
                    Font = new Font("微软雅黑", 9f),
                };
                _txtHumidity = new TextBox
                {
                    ReadOnly = true,
                    Location = new Point(8, 40),
                    Size = new Size(120, 25),
                    Font = new Font("Consolas", 11f),
                    BorderStyle = BorderStyle.FixedSingle,
                    BackColor = Color.White,
                    TextAlign = HorizontalAlignment.Center,
                    Text = "--",
                };
                grpHum.Controls.AddRange(new Control[] { lblH, _txtHumidity });
                panel2.Controls.Add(grpHum);

                // 时间校准（单独一行）
                int timeY = 155;
                var lblPc = new Label
                {
                    Text = "电脑时间",
                    AutoSize = true,
                    Font = new Font("微软雅黑", 9f, FontStyle.Bold),
                    Location = new Point(20, timeY + 5),
                };
                _txtPcTime = new TextBox
                {
                    ReadOnly = true,
                    Location = new Point(80, timeY),
                    Size = new Size(170, 25),
                    Font = new Font("Consolas", 11f),
                    BorderStyle = BorderStyle.FixedSingle,
                    BackColor = Color.White,
                    Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                };
                var lblDev = new Label
                {
                    Text = "设备时间",
                    AutoSize = true,
                    Font = new Font("微软雅黑", 9f, FontStyle.Bold),
                    Location = new Point(270, timeY + 5),
                };
                _txtDeviceTime = new TextBox
                {
                    ReadOnly = true,
                    Location = new Point(330, timeY),
                    Size = new Size(170, 25),
                    Font = new Font("Consolas", 11f),
                    BorderStyle = BorderStyle.FixedSingle,
                    BackColor = Color.White,
                    Text = "--:--:--",
                };
                _btnSyncTime = new Button
                {
                    Text = "同步时间",
                    Enabled = false,
                    Size = new Size(80, 28),
                    Location = new Point(520, timeY),
                    Font = new Font("微软雅黑", 9f),
                    FlatStyle = FlatStyle.Flat,
                };
                _btnSyncTime.Click += BtnSyncTime_Click;

                panel2.Controls.AddRange(new Control[] { lblPc, _txtPcTime, lblDev, _txtDeviceTime, _btnSyncTime });

                // 模块测试状态区（由工序2顶部开始/停止按钮控制）
                int moduleControlY = 200;
                var lblModuleTest = new Label
                {
                    Text = "模块通信状态:",
                    AutoSize = true,
                    Font = new Font("微软雅黑", 9f, FontStyle.Bold),
                    Location = new Point(20, moduleControlY + 5),
                };
                
                _lblModuleTestStatus = new Label
                {
                    Text = "未测试",
                    AutoSize = true,
                    Font = new Font("微软雅黑", 9f),
                    ForeColor = Color.Gray,
                    Location = new Point(110, moduleControlY + 5),
                };

                panel2.Controls.AddRange(new Control[] { lblModuleTest,  _lblModuleTestStatus });

                _btnModuleAllOff = new Button
                {
                    Text = "全部关闭",
                    Size = new Size(85, 28),
                    Location = new Point(220, moduleControlY),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(196, 43, 28),
                    ForeColor = Color.White,
                    Enabled = false,
                };
                _btnModuleAllOff.Click += BtnModulePowerAllOff_Click;

                _btnModuleAllOn = new Button
                {
                    Text = "全部启动",
                    Size = new Size(85, 28),
                    Location = new Point(312, moduleControlY),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(0, 120, 215),
                    ForeColor = Color.White,
                    Enabled = false,
                };
                _btnModuleAllOn.Click += BtnModulePowerAllOn_Click;

                panel2.Controls.AddRange(new Control[] { _btnModuleAllOff, _btnModuleAllOn });
                // 模块控制按钮区（32个，4行×8列）
                int moduleStartY = 230;
                int btnWidth = 85;
                int btnHeight = 45;
                int btnMargin = 6;
                int moduleCols = 8;
                for (int m = 0; m < ModuleButtonCount; m++)
                {
                    int row = m / moduleCols;
                    int col = m % moduleCols;
                    var b = new Button
                    {
                        Text = $"模块{m + 1}",
                        Size = new Size(btnWidth, btnHeight),
                        Location = new Point(20 + col * (btnWidth + btnMargin), moduleStartY + row * (btnHeight + btnMargin)),
                        FlatStyle = FlatStyle.Flat,
                        Font = new Font("微软雅黑", 9f),
                        BackColor = Color.FromArgb(120, 120, 120),
                        ForeColor = Color.White,
                        Enabled = false,
                        Tag = m,
                    };
                    b.Click += (s, e) => OnModuleButtonClick((int)((Button)s).Tag);
                    _btnModule[m] = b;
                    panel2.Controls.Add(b);
                }

                int moduleRows = (ModuleButtonCount + moduleCols - 1) / moduleCols;
                int moduleAreaBottom = moduleStartY + moduleRows * (btnHeight + btnMargin);

                // 在工序2界面内直接嵌入南向485串口调试区（模块通信状态下方）
                var grpSouth = new GroupBox
                {
                    Text = "南向485测试",
                    Location = new Point(10, moduleAreaBottom + 16),
                    Width = 760,
                    Height = 520,
                    Font = new Font("微软雅黑", 9f, FontStyle.Bold),
                    Padding = new Padding(8),
                };

                // 顶部串口选择行
                var topRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(4) };
                topRow.Controls.Add(new Label { Text = "串口:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("微软雅黑", 9f), Padding = new Padding(6,6,6,6) });
                _cmbSouthPorts = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
                _cmbSouthPorts.DropDown += (s, e) => RefreshSouthPortList();
                topRow.Controls.Add(_cmbSouthPorts);
                _btnSouthRefresh = new Button { Text = "刷新", Width = 60, Height = 28, BackColor = Color.Gray, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(6, 4, 6, 4) };
                _btnSouthRefresh.Click += (s, e) => RefreshSouthPortList();
                topRow.Controls.Add(_btnSouthRefresh);

                topRow.Controls.Add(new Label { Text = "波特率:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("微软雅黑", 9f), Padding = new Padding(6,6,6,6) });
                _cmbSouthBaud = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
                _cmbSouthBaud.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200" });
                _cmbSouthBaud.SelectedIndex = 0;
                topRow.Controls.Add(_cmbSouthBaud);

                _btnSouthConnect = new Button { Text = "连接", Width = 90, Height = 30, BackColor = Color.FromArgb(0,120,215), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(12,4,4,4) };
                ApplyButtonStyle(_btnSouthConnect, UiTheme.PrimaryBlue);
                _btnSouthConnect.Click += BtnSouthConnect_Click;
                topRow.Controls.Add(_btnSouthConnect);

                // var lblSouthSteps = new Label
                // {
                //     Text = "步骤：1. 选择串口 2. 设置波特率 3. 点击连接 4. 输入十六进制指令 5. 点击发送",
                //     Dock = DockStyle.Top,
                //     AutoSize = true,
                //     Margin = new Padding(0, 4, 0, 6),
                //     ForeColor = Color.FromArgb(0, 120, 215),
                //     Font = new Font("微软雅黑", 9f),
                // };
                // grpSouth.Controls.Add(lblSouthSteps);
                grpSouth.Controls.Add(topRow);

                // 发送行
                var sendRow = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(4) };
                _txtSouthSend = new TextBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 10f), Text = "01 03 00 00 00 09 85 CC" };
                _btnSouthSend = new Button { Text = "发送", Width = 90, Dock = DockStyle.Right, BackColor = Color.FromArgb(0,120,215), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
                ApplyButtonStyle(_btnSouthSend, UiTheme.PrimaryBlue);
                _btnSouthSend.Click += BtnSouthSend_Click;
                sendRow.Controls.Add(_txtSouthSend);
                sendRow.Controls.Add(_btnSouthSend);
                grpSouth.Controls.Add(sendRow);

                // 占位符间距（确保日志显示不被遮挡）
                var spacer = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.Transparent };
                grpSouth.Controls.Add(spacer);

                // 日志显示区（固定大小，带清空按钮）
                var logPanel = new Panel 
                { 
                    Dock = DockStyle.Top,
                    Height = 280,
                    BorderStyle = BorderStyle.FixedSingle,
                    Padding = new Padding(4)
                };
                _rtbSouthLog = new RichTextBox 
                { 
                    Dock = DockStyle.Fill, 
                    ReadOnly = true, 
                    Font = new Font("Consolas", 10f),
                    ScrollBars = RichTextBoxScrollBars.Vertical,
                    WordWrap = true
                };
                _btnSouthClear = new Button 
                { 
                    Text = "清空日志", 
                    Width = 80, 
                    Height = 28, 
                    Dock = DockStyle.Bottom, 
                    BackColor = Color.FromArgb(150, 150, 150), 
                    ForeColor = Color.White, 
                    FlatStyle = FlatStyle.Flat, 
                    Margin = new Padding(0, 4, 0, 0) 
                };
                _btnSouthClear.Click += (s, e) => { if (_rtbSouthLog != null && !_rtbSouthLog.IsDisposed) _rtbSouthLog.Clear(); };
                logPanel.Controls.Add(_rtbSouthLog);
                logPanel.Controls.Add(_btnSouthClear);
                grpSouth.Controls.Add(logPanel);

                // 状态条（放在GroupBox底部）
                var status = new StatusStrip { Dock = DockStyle.Bottom };
                _lblSouthSerialStatus = new ToolStripStatusLabel { Text = "未连接" };
                status.Items.Add(_lblSouthSerialStatus);
                grpSouth.Controls.Add(status);

                // 初始化端口列表
                RefreshSouthPortList();

                // 把 grpSouth 添加到 panel2
                panel2.Controls.Add(grpSouth);
                
                page.Controls.Add(panel2);
                page.Controls.Add(strip);
            }
            else if (idx == 2)
            {
                // 工序3：告警测试界面
                var panel3 = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10), AutoScroll = true };

                var spdGrp = BuildNamedAlarmGroup(
                    "防雷器告警",
                    new[] { "AC SPD 告警", "DC SPD 告警" },
                    _lblSpdAlarms);
                var batFuseGrp = BuildNamedAlarmGroup(
                    "蓄电池熔丝告警",
                    new[] { "BAT1", "BAT2", "BAT3", "BAT4", "BAT5", "BAT6", "BAT7", "BAT8" },
                    _lblBatFuseAlarms);
                var loadFuseGrp = BuildNamedAlarmGroup(
                    "负载熔丝告警",
                    new[] { "LLVD1", "LLVD2", "LLVD3", "LLVD4" },
                    _lblLoadFuseAlarms);

                var diGrp = BuildIoAlarmGroup("DI口告警测试", "DI", _lblDiAlarms);
                var doGrp = BuildDoAlarmGroup();
                var lvdGrp = BuildLvdAlarmGroup();

                panel3.Controls.Add(lvdGrp);
                panel3.Controls.Add(doGrp);
                panel3.Controls.Add(diGrp);
                panel3.Controls.Add(loadFuseGrp);
                panel3.Controls.Add(batFuseGrp);
                panel3.Controls.Add(spdGrp);

                page.Controls.Add(panel3);
                page.Controls.Add(strip);
            }
            else if (idx == 3)
            {
                // 工序4：参数校准
                var panel4 = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10), AutoScroll = true };

                string[] items =
                {
                    "直流母排电压(V)",
                    "电池1电压(V)",
                    "电池2电压(V)",
                    "电池3电压(V)",
                    "电池4电压(V)",
                    "电池1中电压(V)",
                    "电池2中电压(V)",
                    "电池3中电压(V)",
                    "电池4中电压(V)",
                    "负载1电流K(A)",
                    "负载1电流B(A)",
                    "负载2电流K(A)",
                    "负载2电流B(A)",
                    "负载3电流K(A)",
                    "负载3电流B(A)",
                    "负载4电流K(A)",
                    "负载4电流B(A)",
                    // "温度 1(℃)",
                    // "温度 2(℃)",
                    // "温度 3(℃)",
                    // "温度 4(℃)",
                    // "温度 5(℃)",
                    "电池电流K(A)",
                    "电池电流B(A)",
                    // "环境湿度(RH)",
                    // "DG Fuel Cap",
                    // "输入电压 L1(V)",
                    // "输入电压 L2(V)",
                    // "输入电压 L3(V)",
                    // "输入电流 L1(A)",
                    // "输入电流 L2(A)",
                    // "输入电流 L3(A)",
                };

                int headerY = 20;
                int rowY = 48;
                int rowGap = 36;
                int itemWidth = 150;
                int boxWidth = 90;
                int boxHeight = 25;
                int itemX = 20;
                int resetX = 175;
                int monitorX = 255;
                int testX = 375;
                int pointX = 495;
                int buttonX = 615;

                var grp = new GroupBox 
                { 
                    Text = "参数校准", 
                    Dock = DockStyle.Top, 
                    Height = rowY + items.Length * rowGap + 40,
                    Font = new Font("微软雅黑", 10f, FontStyle.Bold),
                    Padding = new Padding(10),
                };

                _txtParamCurrent = new TextBox[items.Length];
                _txtParamTarget = new TextBox[items.Length];
                _txtParamPoint = new TextBox[items.Length];
                _btnParamSave = new Button[items.Length];
                _btnParamReset = new Button[items.Length];

                var bodyPanel = new Panel
                {
                    Dock = DockStyle.Fill,
                    Padding = new Padding(0),
                };

                bodyPanel.Controls.Add(new Label
                {
                    Text = "参数项",
                    AutoSize = false,
                    Size = new Size(itemWidth, 20),
                    Location = new Point(itemX, headerY),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("微软雅黑", 9f, FontStyle.Bold),
                });
                bodyPanel.Controls.Add(new Label
                {
                    Text = "复位",
                    AutoSize = false,
                    Size = new Size(70, 20),
                    Location = new Point(resetX, headerY),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("微软雅黑", 9f, FontStyle.Bold),
                });
                bodyPanel.Controls.Add(new Label
                {
                    Text = "Measure",
                    AutoSize = false,
                    Size = new Size(boxWidth, 20),
                    Location = new Point(monitorX, headerY),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("微软雅黑", 9f, FontStyle.Bold),
                });
                bodyPanel.Controls.Add(new Label
                {
                    Text = "Actual",
                    AutoSize = false,
                    Size = new Size(boxWidth, 20),
                    Location = new Point(testX, headerY),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("微软雅黑", 9f, FontStyle.Bold),
                });
                bodyPanel.Controls.Add(new Label
                {
                    Text = "Point(K or B)",
                    AutoSize = false,
                    Size = new Size(boxWidth, 20),
                    Location = new Point(pointX, headerY),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("微软雅黑", 9f, FontStyle.Bold),
                });
                bodyPanel.Controls.Add(new Label
                {
                    Text = "保存",
                    AutoSize = false,
                    Size = new Size(70, 20),
                    Location = new Point(buttonX, headerY),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("微软雅黑", 9f, FontStyle.Bold),
                });

                for (int r = 0; r < items.Length; r++)
                {
                    int currentY = rowY + r * rowGap;

                    var lbl = new Label
                    {
                        Text = items[r],
                        AutoSize = false,
                        Size = new Size(itemWidth, 20),
                        Location = new Point(itemX, currentY + 3),
                        TextAlign = ContentAlignment.MiddleLeft,
                        Font = new Font("微软雅黑", 9f, FontStyle.Bold),
                    };
                    var btnRowReset = new Button
                    {
                        Text = "复位",
                        Size = new Size(70, 28),
                        Location = new Point(resetX, currentY - 1),
                        Font = new Font("微软雅黑", 9f),
                        FlatStyle = FlatStyle.Flat,
                        BackColor = Color.FromArgb(120, 120, 120),
                        ForeColor = Color.White,
                        Enabled = false,
                        Tag = r,
                    };
                    btnRowReset.Click += BtnParamReset_Click;
                    var tbMonitor = new TextBox
                    {
                        Enabled = false,
                        Text = string.Empty,
                        Font = new Font("Consolas", 11f),
                        BorderStyle = BorderStyle.FixedSingle,
                        BackColor = Color.FromArgb(240, 240, 240),
                        TextAlign = HorizontalAlignment.Center,
                        Width = boxWidth,
                        Height = boxHeight,
                        Location = new Point(monitorX, currentY),
                    };
                    var tbTest = new TextBox
                    {
                        Enabled = false,
                        Text = string.Empty,
                        Font = new Font("Consolas", 11f),
                        BorderStyle = BorderStyle.FixedSingle,
                        BackColor = Color.FromArgb(240, 240, 240),
                        TextAlign = HorizontalAlignment.Center,
                        Width = boxWidth,
                        Height = boxHeight,
                        Location = new Point(testX, currentY),
                    };
                    var tbPoint = new TextBox
                    {
                        ReadOnly = true,
                        Text = string.Empty,
                        Font = new Font("Consolas", 11f),
                        BorderStyle = BorderStyle.FixedSingle,
                        BackColor = Color.WhiteSmoke,
                        TextAlign = HorizontalAlignment.Center,
                        Width = boxWidth,
                        Height = boxHeight,
                        Location = new Point(pointX, currentY),
                    };
                    var btnRowSave = new Button
                    {
                        Text = "保存",
                        Size = new Size(70, 28),
                        Location = new Point(buttonX, currentY - 1),
                        Font = new Font("微软雅黑", 9f),
                        FlatStyle = FlatStyle.Flat,
                        BackColor = Color.FromArgb(0, 120, 215),
                        ForeColor = Color.White,
                        Enabled = false,
                        Tag = r,
                    };
                    btnRowSave.Click += BtnParamSave_Click;

                    int rowIndex = r;
                    tbMonitor.TextChanged += (s, e) => UpdateCalibrationPoint(rowIndex);
                    tbTest.TextChanged += (s, e) => UpdateCalibrationPoint(rowIndex);

                    _txtParamCurrent[r] = tbMonitor;
                    _txtParamTarget[r] = tbTest;
                    _txtParamPoint[r] = tbPoint;
                    _btnParamSave[r] = btnRowSave;
                    _btnParamReset[r] = btnRowReset;

                    bodyPanel.Controls.Add(lbl);
                    bodyPanel.Controls.Add(btnRowReset);
                    bodyPanel.Controls.Add(tbMonitor);
                    bodyPanel.Controls.Add(tbTest);
                    bodyPanel.Controls.Add(tbPoint);
                    bodyPanel.Controls.Add(btnRowSave);
                }

                grp.Controls.Add(bodyPanel);

                panel4.Controls.Add(grp);

                // 在条码输入框下方为工序4增加设备IP + SNMP团体名输入控件
                var ipPanel = new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 40,
                    Padding = new Padding(12, 6, 12, 6),
                };
                var lblIp = new Label
                {
                    Text = "设备IP:",
                    AutoSize = true,
                    Location = new Point(8, 8),
                    Font = new Font("微软雅黑", 9f),
                };
                _txtDeviceIp = MakeTextBox(160);
                _txtDeviceIp.Location = new Point(70, 6);
                _txtDeviceIp.ReadOnly = true;
                _txtDeviceIp.BackColor = Color.FromArgb(240, 240, 240);

                var lblReadCommunity = new Label
                {
                    Text = "读团体名:",
                    AutoSize = true,
                    Location = new Point(240, 8),
                    Font = new Font("微软雅黑", 9f),
                };
                _txtSnmpReadCommunity = MakeTextBox(110);
                _txtSnmpReadCommunity.Location = new Point(310, 6);
                _txtSnmpReadCommunity.Text = SnmpV2cClient.DefaultCommunity;
                _txtSnmpReadCommunity.ReadOnly = true;
                _txtSnmpReadCommunity.BackColor = Color.FromArgb(240, 240, 240);

                var lblWriteCommunity = new Label
                {
                    Text = "写团体名:",
                    AutoSize = true,
                    Location = new Point(430, 8),
                    Font = new Font("微软雅黑", 9f),
                };
                _txtSnmpWriteCommunity = MakeTextBox(110);
                _txtSnmpWriteCommunity.Location = new Point(500, 6);
                _txtSnmpWriteCommunity.Text = SnmpV2cClient.DefaultWriteCommunity;
                _txtSnmpWriteCommunity.ReadOnly = true;
                _txtSnmpWriteCommunity.BackColor = Color.FromArgb(240, 240, 240);

                _btnDeviceIpConfirm = new Button
                {
                    Text = "确认",
                    Size = new Size(70, 28),
                    Location = new Point(620, 5),
                    Font = new Font("微软雅黑", 9f),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(0, 120, 215),
                    ForeColor = Color.White,
                };
                _btnDeviceIpConfirm.Click += BtnDeviceIpConfirm_Click;
                ipPanel.Controls.Add(lblIp);
                ipPanel.Controls.Add(_txtDeviceIp);
                ipPanel.Controls.Add(lblReadCommunity);
                ipPanel.Controls.Add(_txtSnmpReadCommunity);
                ipPanel.Controls.Add(lblWriteCommunity);
                ipPanel.Controls.Add(_txtSnmpWriteCommunity);
                ipPanel.Controls.Add(_btnDeviceIpConfirm);

                // 先加入主体面板，再加入IP输入行，最后加入顶部条（保持和其它页一致的Dock顺序）
                page.Controls.Add(panel4);
                page.Controls.Add(ipPanel);
                page.Controls.Add(strip);
            }
            else
            {
                // 日志区（DockStyle.Fill，必须先加入page）
                _rtbLog[idx] = new RichTextBox
                {
                    Dock        = DockStyle.Fill,
                    ReadOnly    = true,
                    BackColor   = Color.FromArgb(20, 20, 30),
                    ForeColor   = Color.LightGreen,
                    Font        = new Font("Consolas", 9f),
                    ScrollBars  = RichTextBoxScrollBars.Vertical,
                    BorderStyle = BorderStyle.None,
                };

                // Fill先加，Top后加（WinForms Dock布局要求）
                page.Controls.Add(_rtbLog[idx]);
                page.Controls.Add(strip);
            }
            return page;
        }

        // 工序4 SNMP 查询项：与参数校准界面行索引一一对应
        private static readonly (int Index, string Name, string Oid)[] CalibrationSnmpItems =
        {
            (0,  "直流母排电压", ".1.3.6.1.4.1.40211.2.1.1.1.0"),
            (1,  "电池1电压",   ".1.3.6.1.4.1.40211.3.1.1.2.0"),
            (2,  "电池2电压",   ".1.3.6.1.4.1.40211.3.1.1.26.0"),
            (3,  "电池3电压",   ".1.3.6.1.4.1.40211.3.1.1.27.0"),
            (4,  "电池4电压",   ".1.3.6.1.4.1.40211.3.1.1.28.0"),
            (5,  "电池1中电压", ".1.3.6.1.4.1.40211.3.1.1.5.0"),
            (6,  "电池2中电压", ".1.3.6.1.4.1.40211.3.1.1.29.0"),
            (7,  "电池3中电压", ".1.3.6.1.4.1.40211.3.1.1.30.0"),
            (8,  "电池4中电压", ".1.3.6.1.4.1.40211.3.1.1.31.0"),
            (9,  "负载1电流K",  ".1.3.6.1.4.1.40211.2.1.1.8.0"),
            (10, "负载1电流B",  ".1.3.6.1.4.1.40211.2.1.1.8.0"),
            (11, "负载2电流K",  ".1.3.6.1.4.1.40211.2.1.1.9.0"),
            (12, "负载2电流B",  ".1.3.6.1.4.1.40211.2.1.1.9.0"),
            (13, "负载3电流K",  ".1.3.6.1.4.1.40211.2.1.1.10.0"),
            (14, "负载3电流B",  ".1.3.6.1.4.1.40211.2.1.1.10.0"),
            (15, "负载4电流K",  ".1.3.6.1.4.1.40211.2.1.1.11.0"),
            (16, "负载4电流B",  ".1.3.6.1.4.1.40211.2.1.1.11.0"),
            (17, "电池电流K",   ".1.3.6.1.4.1.40211.3.1.1.3.0"),
            (18, "电池电流B",   ".1.3.6.1.4.1.40211.3.1.1.3.0"),
        };

        private void BtnDeviceIpConfirm_Click(object sender, EventArgs e)
        {
            if (_txtDeviceIp == null || _btnDeviceIpConfirm == null)
                return;

            if (_deviceIpLocked)
            {
                UnlockDeviceIp();
                return;
            }

            string ip = _txtDeviceIp.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(ip))
            {
                MessageBox.Show("请先输入设备IP。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!System.Net.IPAddress.TryParse(ip, out _))
            {
                MessageBox.Show("设备IP格式无效。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string readCommunity = _txtSnmpReadCommunity?.Text?.Trim() ?? string.Empty;
            string writeCommunity = _txtSnmpWriteCommunity?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(readCommunity))
            {
                MessageBox.Show("请输入读团体名（默认 PowerPublic）。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(writeCommunity))
            {
                MessageBox.Show("请输入写团体名（默认 PowerPrivate）。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            LockDeviceIp(ip, readCommunity, writeCommunity);
            StartSnmpPolling(ip);
        }

        private void StartSnmpPolling(string ip)
        {
            StopSnmpPolling();

            CancellationToken token;
            lock (_snmpPollLock)
            {
                _lockedDeviceIp = ip;
                _snmpPollCts = new CancellationTokenSource();
                token = _snmpPollCts.Token;
            }

            SetResult(3, "SNMP轮询中...", Color.Orange);
            AppendLog(3, $"工序4确认设备IP: {ip}，启动SNMP轮询（{SnmpPollIntervalMs / 1000}s）");
            ModbusDataLogger.LogInfo(
                $"工序4确认设备IP: {ip}，启动 SNMP 轮询（间隔 {SnmpPollIntervalMs / 1000}s）",
                ModbusDataLogger.PortType.SNMP);

            Task.Run(() => SnmpPollingLoop(ip, token), token);
        }

        private void StopSnmpPolling()
        {
            CancellationTokenSource cts;
            lock (_snmpPollLock)
            {
                cts = _snmpPollCts;
                _snmpPollCts = null;
            }
            if (cts == null)
                return;

            try { cts.Cancel(); } catch { }
            try { cts.Dispose(); } catch { }

            AppendLog(3, "工序4SNMP轮询已停止");
            ModbusDataLogger.LogInfo("工序4 SNMP 轮询已停止", ModbusDataLogger.PortType.SNMP);
        }

        private void SnmpPollingLoop(string ip, CancellationToken token)
        {
            bool firstRound = true;

            while (!token.IsCancellationRequested)
            {
                int okCount = 0;
                int failCount = 0;
                var failNames = new System.Collections.Generic.List<string>();

                if (!IsHandleCreated || IsDisposed)
                    break;

                try
                {
                    BeginInvoke(new Action(() => SetResult(3, "SNMP查询中...", Color.Orange)));
                }
                catch { break; }

                foreach (var item in CalibrationSnmpItems)
                {
                    if (token.IsCancellationRequested)
                        break;

                    try
                    {
                        long value = SnmpV2cClient.Get(ip, item.Oid, _lockedSnmpReadCommunity);
                        if (token.IsCancellationRequested)
                            break;

                        BeginInvoke(new Action(() => ApplyCalibrationMeasure(item.Index, value)));
                        okCount++;
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        failNames.Add(item.Name);
                        AppendLog(3, $"{item.Name} SNMP查询失败: {ex.Message}");
                        ModbusDataLogger.LogInfo(
                            $"工序4 {item.Name} SNMP 查询失败: {ex.Message}",
                            ModbusDataLogger.PortType.SNMP);
                    }
                }

                if (token.IsCancellationRequested)
                    break;

                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (failCount == 0)
                        {
                            SetResult(3, "SNMP轮询中", Color.Green);
                            AppendLog(3, $"本轮SNMP查询成功: {okCount}/{okCount + failCount}");
                        }
                        else if (okCount == 0)
                        {
                            SetResult(3, "SNMP失败", Color.Red);
                            AppendLog(3, "本轮SNMP查询全部失败" + (firstRound ? "（请检查设备IP与网络）" : ""));
                        }
                        else
                        {
                            SetResult(3, $"SNMP部分成功({okCount}/{okCount + failCount})", Color.Orange);
                            AppendLog(3, $"本轮SNMP部分成功: 成功{okCount}，失败{failCount}");
                            if (firstRound && failNames != null && failNames.Count > 0)
                                AppendLog(3, "失败项: " + string.Join("、", failNames));
                        }
                    }));
                }
                catch { break; }

                firstRound = false;

                try
                {
                    Task.Delay(SnmpPollIntervalMs, token).Wait(token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    break;
                }
            }
        }

        private void ApplyCalibrationMeasure(int index, long value)
        {
            if (_txtParamCurrent == null || index < 0 || index >= _txtParamCurrent.Length)
                return;
            if (_txtParamCurrent[index] == null)
                return;

            // SNMP 原始整数缩小十倍后显示，保留两位小数
            double display = value / 10.0;
            _txtParamCurrent[index].Text = display.ToString("0.00", CultureInfo.InvariantCulture);
            UpdateCalibrationPoint(index);
        }

        private void LockDeviceIp(string ip, string readCommunity, string writeCommunity)
        {
            _deviceIpLocked = true;
            _lockedDeviceIp = ip;
            _lockedSnmpReadCommunity = string.IsNullOrWhiteSpace(readCommunity)
                ? SnmpV2cClient.DefaultCommunity
                : readCommunity.Trim();
            _lockedSnmpWriteCommunity = string.IsNullOrWhiteSpace(writeCommunity)
                ? SnmpV2cClient.DefaultWriteCommunity
                : writeCommunity.Trim();

            if (_txtDeviceIp != null)
            {
                _txtDeviceIp.Text = ip;
                _txtDeviceIp.ReadOnly = true;
                _txtDeviceIp.BackColor = Color.FromArgb(240, 240, 240);
            }
            if (_txtSnmpReadCommunity != null)
            {
                _txtSnmpReadCommunity.Text = _lockedSnmpReadCommunity;
                _txtSnmpReadCommunity.ReadOnly = true;
                _txtSnmpReadCommunity.BackColor = Color.FromArgb(240, 240, 240);
            }
            if (_txtSnmpWriteCommunity != null)
            {
                _txtSnmpWriteCommunity.Text = _lockedSnmpWriteCommunity;
                _txtSnmpWriteCommunity.ReadOnly = true;
                _txtSnmpWriteCommunity.BackColor = Color.FromArgb(240, 240, 240);
            }
            if (_btnDeviceIpConfirm != null)
            {
                _btnDeviceIpConfirm.Text = "解锁";
                _btnDeviceIpConfirm.Enabled = true;
            }
        }

        private void UnlockDeviceIp()
        {
            StopSnmpPolling();
            _deviceIpLocked = false;
            _lockedDeviceIp = string.Empty;
            _lockedSnmpReadCommunity = SnmpV2cClient.DefaultCommunity;
            _lockedSnmpWriteCommunity = SnmpV2cClient.DefaultWriteCommunity;
            if (_txtDeviceIp != null)
            {
                _txtDeviceIp.ReadOnly = false;
                _txtDeviceIp.BackColor = Color.White;
            }
            if (_txtSnmpReadCommunity != null)
            {
                _txtSnmpReadCommunity.ReadOnly = false;
                _txtSnmpReadCommunity.BackColor = Color.White;
            }
            if (_txtSnmpWriteCommunity != null)
            {
                _txtSnmpWriteCommunity.ReadOnly = false;
                _txtSnmpWriteCommunity.BackColor = Color.White;
            }
            if (_btnDeviceIpConfirm != null)
            {
                _btnDeviceIpConfirm.Text = "确认";
                _btnDeviceIpConfirm.Enabled = true;
            }
            SetResult(3, "已停止轮询", Color.Gray);
        }

        // 工序4 SNMP SET OID：保存时写入 Point×1000
        private static readonly (int Index, string Name, string SetOid)[] CalibrationSnmpSetItems =
        {
            (0,  "直流母排电压", ".1.3.6.1.4.1.40211.8.20.1.1.0"),
            (1,  "电池1电压",   ".1.3.6.1.4.1.40211.8.20.1.4.0"),
            (2,  "电池2电压",   ".1.3.6.1.4.1.40211.8.20.1.30.0"),
            (3,  "电池3电压",   ".1.3.6.1.4.1.40211.8.20.1.31.0"),
            (4,  "电池4电压",   ".1.3.6.1.4.1.40211.8.20.1.32.0"),
            (5,  "电池1中电压", ".1.3.6.1.4.1.40211.8.20.1.5.0"),
            (6,  "电池2中电压", ".1.3.6.1.4.1.40211.8.20.1.33.0"),
            (7,  "电池3中电压", ".1.3.6.1.4.1.40211.8.20.1.34.0"),
            (8,  "电池4中电压", ".1.3.6.1.4.1.40211.8.20.1.35.0"),
            (9,  "负载1电流K",  ".1.3.6.1.4.1.40211.8.20.1.6.0"),
            (10, "负载1电流B",  ".1.3.6.1.4.1.40211.8.20.1.7.0"),
            (11, "负载2电流K",  ".1.3.6.1.4.1.40211.8.20.1.8.0"),
            (12, "负载2电流B",  ".1.3.6.1.4.1.40211.8.20.1.9.0"),
            (13, "负载3电流K",  ".1.3.6.1.4.1.40211.8.20.1.10.0"),
            (14, "负载3电流B",  ".1.3.6.1.4.1.40211.8.20.1.11.0"),
            (15, "负载4电流K",  ".1.3.6.1.4.1.40211.8.20.1.12.0"),
            (16, "负载4电流B",  ".1.3.6.1.4.1.40211.8.20.1.13.0"),
            (17, "电池电流K",   ".1.3.6.1.4.1.40211.8.20.1.2.0"),
            (18, "电池电流B",   ".1.3.6.1.4.1.40211.8.20.1.3.0"),
        };

        private static bool TryGetCalibrationSetOid(int index, out string name, out string setOid)
        {
            foreach (var item in CalibrationSnmpSetItems)
            {
                if (item.Index == index)
                {
                    name = item.Name;
                    setOid = item.SetOid;
                    return true;
                }
            }
            name = null;
            setOid = null;
            return false;
        }

        /// <summary>
        /// 复位默认值：B 写 0，K（及电压类系数）写 1000。
        /// </summary>
        private static long GetCalibrationResetValue(string itemName)
        {
            if (!string.IsNullOrWhiteSpace(itemName) && itemName.TrimEnd().EndsWith("B", StringComparison.Ordinal))
                return 0;
            return 1000;
        }

        private void BtnParamReset_Click(object sender, EventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is int index))
                return;

            if (!TryGetCalibrationSetOid(index, out string itemName, out string setOid))
            {
                MessageBox.Show("当前参数没有对应的复位 SNMP OID。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!_deviceIpLocked || string.IsNullOrWhiteSpace(_lockedDeviceIp))
            {
                MessageBox.Show("请先确认并锁定设备IP。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            long resetValue = GetCalibrationResetValue(itemName);
            string valueKind = resetValue == 0 ? "B" : "K";
            string ip = _lockedDeviceIp;
            string barcode = (_txtBarcode != null && _txtBarcode.Length > 3 && _txtBarcode[3] != null)
                ? (_txtBarcode[3].Text?.Trim() ?? string.Empty)
                : string.Empty;
            if (string.IsNullOrWhiteSpace(barcode))
                barcode = "-";

            if (_txtParamTarget != null && index < _txtParamTarget.Length && _txtParamTarget[index] != null)
                _txtParamTarget[index].Text = string.Empty;
            if (_txtParamPoint != null && index < _txtParamPoint.Length && _txtParamPoint[index] != null)
                _txtParamPoint[index].Text = resetValue == 0 ? "0" : "1.000";

            string resetLogLine =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {barcode} {ip} {setOid} " +
                $"{itemName} 复位 {valueKind}={resetValue}";
            WriteProcess4SaveLog(resetLogLine);
            AppendLog(3, resetLogLine);

            btn.Enabled = false;
            if (_btnParamSave != null && index < _btnParamSave.Length && _btnParamSave[index] != null)
                _btnParamSave[index].Enabled = false;

            SetResult(3, "SNMP复位中...", Color.Orange);
            ModbusDataLogger.LogInfo(
                $"工序4复位{itemName}: {valueKind}={resetValue}, SET {setOid}",
                ModbusDataLogger.PortType.SNMP);

            Task.Run(() =>
            {
                try
                {
                    long setValue = SnmpV2cClient.SetInteger(ip, setOid, resetValue, _lockedSnmpWriteCommunity);
                    string okLine =
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {barcode} {ip} {setOid} " +
                        $"{itemName} 复位Result=成功 {valueKind}={resetValue} INTEGER={setValue}";
                    WriteProcess4SaveLog(okLine);
                    AppendProcess4SaveToMesWorkbook(
                        barcode, ip, setOid, itemName + "-复位",
                        "-",
                        "-",
                        resetValue == 0 ? "0" : "1.000",
                        resetValue.ToString(CultureInfo.InvariantCulture),
                        "PASS",
                        "INTEGER=" + setValue);
                    ModbusDataLogger.LogInfo(
                        $"工序4{itemName} 复位 SET 回包 INTEGER={setValue}",
                        ModbusDataLogger.PortType.SNMP);

                    BeginInvoke(new Action(() =>
                    {
                        SetResult(3, "复位成功", Color.Green);
                        AppendLog(3, okLine);
                        if (setValue != resetValue)
                            AppendLog(3, $"复位回包值 {setValue} 与期望 {valueKind}={resetValue} 不一致");
                    }));
                }
                catch (Exception ex)
                {
                    string failLine =
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {barcode} {ip} {setOid} " +
                        $"{itemName} 复位Result=失败 Error={ex.Message}";
                    WriteProcess4SaveLog(failLine);
                    AppendProcess4SaveToMesWorkbook(
                        barcode, ip, setOid, itemName + "-复位",
                        "-",
                        "-",
                        resetValue == 0 ? "0" : "1.000",
                        resetValue.ToString(CultureInfo.InvariantCulture),
                        "FAIL",
                        ex.Message);

                    BeginInvoke(new Action(() =>
                    {
                        SetResult(3, "复位失败", Color.Red);
                        AppendLog(3, failLine);
                    }));
                }
                finally
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (_paramCalibrationManualMode)
                        {
                            btn.Enabled = true;
                            if (_btnParamSave != null && index < _btnParamSave.Length && _btnParamSave[index] != null)
                                _btnParamSave[index].Enabled = true;
                        }
                    }));
                }
            });
        }

        private void BtnParamSave_Click(object sender, EventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is int index))
                return;

            if (_txtParamCurrent == null || _txtParamTarget == null || _txtParamPoint == null)
                return;

            if (index < 0 || index >= _txtParamCurrent.Length)
                return;

            string measureText = _txtParamCurrent[index].Text.Trim();
            string actualText = _txtParamTarget[index].Text.Trim();
            if (!double.TryParse(measureText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double measure)
                || !double.TryParse(actualText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double actual)
                || Math.Abs(actual) <= double.Epsilon)
            {
                _txtParamPoint[index].Text = string.Empty;
                MessageBox.Show("请输入有效的 Measure 和 Actual 数值，且 Actual 不能为 0。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            double point = measure / actual;
            _txtParamPoint[index].Text = point.ToString("0.000", CultureInfo.InvariantCulture);

            if (!TryGetCalibrationSetOid(index, out string itemName, out string setOid))
                return;

            if (!_deviceIpLocked || string.IsNullOrWhiteSpace(_lockedDeviceIp))
            {
                MessageBox.Show("请先确认并锁定设备IP。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            long k = (long)Math.Round(point * 1000.0, MidpointRounding.AwayFromZero);
            string ip = _lockedDeviceIp;
            string barcode = (_txtBarcode != null && _txtBarcode.Length > 3 && _txtBarcode[3] != null)
                ? (_txtBarcode[3].Text?.Trim() ?? string.Empty)
                : string.Empty;
            if (string.IsNullOrWhiteSpace(barcode))
                barcode = "-";

            // 格式：时间 条码 IP snmpOID 参数名 Measure Actual Point K
            string saveLogLine =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {barcode} {ip} {setOid} " +
                $"{itemName} Measure={measure.ToString(CultureInfo.InvariantCulture)} " +
                $"Actual={actual.ToString(CultureInfo.InvariantCulture)} " +
                $"Point={point.ToString("0.000", CultureInfo.InvariantCulture)} K={k}";
            WriteProcess4SaveLog(saveLogLine);
            AppendLog(3, saveLogLine);

            btn.Enabled = false;
            SetResult(3, "SNMP设置中...", Color.Orange);
            ModbusDataLogger.LogInfo(
                $"工序4保存{itemName}: Point={point:0.000}, K={k}, SET {setOid}",
                ModbusDataLogger.PortType.SNMP);

            Task.Run(() =>
            {
                try
                {
                    long setValue = SnmpV2cClient.SetInteger(ip, setOid, k, _lockedSnmpWriteCommunity);
                    string okLine =
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {barcode} {ip} {setOid} " +
                        $"{itemName} Result=成功 INTEGER={setValue}";
                    WriteProcess4SaveLog(okLine);
                    AppendProcess4SaveToMesWorkbook(
                        barcode, ip, setOid, itemName,
                        measure.ToString(CultureInfo.InvariantCulture),
                        actual.ToString(CultureInfo.InvariantCulture),
                        point.ToString("0.000", CultureInfo.InvariantCulture),
                        k.ToString(CultureInfo.InvariantCulture),
                        "PASS",
                        "INTEGER=" + setValue);
                    ModbusDataLogger.LogInfo(
                        $"工序4{itemName} SET 回包 INTEGER={setValue}",
                        ModbusDataLogger.PortType.SNMP);

                    BeginInvoke(new Action(() =>
                    {
                        SetResult(3, "SET成功", Color.Green);
                        AppendLog(3, okLine);
                        if (setValue != k)
                            AppendLog(3, $"SET 回包值 {setValue} 与期望 K={k} 不一致");
                    }));
                }
                catch (Exception ex)
                {
                    string failLine =
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {barcode} {ip} {setOid} " +
                        $"{itemName} Result=失败 Error={ex.Message}";
                    WriteProcess4SaveLog(failLine);
                    AppendProcess4SaveToMesWorkbook(
                        barcode, ip, setOid, itemName,
                        measure.ToString(CultureInfo.InvariantCulture),
                        actual.ToString(CultureInfo.InvariantCulture),
                        point.ToString("0.000", CultureInfo.InvariantCulture),
                        k.ToString(CultureInfo.InvariantCulture),
                        "FAIL",
                        "Error=" + ex.Message);
                    ModbusDataLogger.LogInfo(
                        $"工序4{itemName} SET 失败: {ex.Message}",
                        ModbusDataLogger.PortType.SNMP);

                    BeginInvoke(new Action(() =>
                    {
                        SetResult(3, "SET失败", Color.Red);
                        AppendLog(3, failLine);
                    }));
                }
                finally
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (btn != null && !btn.IsDisposed)
                            btn.Enabled = _paramCalibrationManualMode;
                    }));
                }
            });
        }

        private void SetCalibrationMode(bool active)
        {
            if (_txtParamCurrent != null)
            {
                for (int i = 0; i < _txtParamCurrent.Length; i++)
                {
                    if (_txtParamCurrent[i] == null) continue;
                    _txtParamCurrent[i].Enabled = active;
                    _txtParamCurrent[i].BackColor = active ? Color.White : Color.FromArgb(240, 240, 240);
                }
            }

            if (_txtParamTarget != null)
            {
                for (int i = 0; i < _txtParamTarget.Length; i++)
                {
                    if (_txtParamTarget[i] == null) continue;
                    _txtParamTarget[i].Enabled = active;
                    _txtParamTarget[i].BackColor = active ? Color.White : Color.FromArgb(240, 240, 240);
                }
            }

            if (_btnParamSave != null)
            {
                for (int i = 0; i < _btnParamSave.Length; i++)
                {
                    if (_btnParamSave[i] == null) continue;
                    _btnParamSave[i].Enabled = active;
                }
            }

            if (_btnParamReset != null)
            {
                for (int i = 0; i < _btnParamReset.Length; i++)
                {
                    if (_btnParamReset[i] == null) continue;
                    _btnParamReset[i].Enabled = active;
                }
            }
        }

        private void UpdateCalibrationPoint(int index)
        {
            if (_txtParamCurrent == null || _txtParamTarget == null || _txtParamPoint == null)
                return;

            if (index < 0 || index >= _txtParamCurrent.Length)
                return;

            string measureText = _txtParamCurrent[index]?.Text?.Trim();
            string actualText = _txtParamTarget[index]?.Text?.Trim();
            if (double.TryParse(measureText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double measure)
                && double.TryParse(actualText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double actual)
                && Math.Abs(actual) > double.Epsilon)
            {
                _txtParamPoint[index].Text = (measure / actual).ToString("0.000", CultureInfo.InvariantCulture);
            }
            else
            {
                _txtParamPoint[index].Text = string.Empty;
            }
        }

        private void UpdateProcess4StartButtonState()
        {
            if (_txtBarcode == null || _btnStart == null) return;
            if (_txtBarcode.Length <= 3 || _btnStart.Length <= 3) return;
            if (_txtBarcode[3] == null || _btnStart[3] == null) return;

            _btnStart[3].Enabled = !string.IsNullOrWhiteSpace(_txtBarcode[3].Text);
        }

        private void UpdateProcess4IpInputState()
        {
            if (_txtDeviceIp == null)
                return;

            // 仅在工序4已开始且IP未锁定时允许输入
            bool canEdit = _paramCalibrationManualMode && !_deviceIpLocked;
            _txtDeviceIp.ReadOnly = !canEdit;
            _txtDeviceIp.BackColor = canEdit ? Color.White : Color.FromArgb(240, 240, 240);

            if (_txtSnmpReadCommunity != null)
            {
                _txtSnmpReadCommunity.ReadOnly = !canEdit;
                _txtSnmpReadCommunity.BackColor = canEdit ? Color.White : Color.FromArgb(240, 240, 240);
            }
            if (_txtSnmpWriteCommunity != null)
            {
                _txtSnmpWriteCommunity.ReadOnly = !canEdit;
                _txtSnmpWriteCommunity.BackColor = canEdit ? Color.White : Color.FromArgb(240, 240, 240);
            }
        }

        private void ResetProcess4CalibrationControls()
        {
            if (_txtParamCurrent != null)
            {
                for (int i = 0; i < _txtParamCurrent.Length; i++)
                {
                    if (_txtParamCurrent[i] == null) continue;
                    _txtParamCurrent[i].Enabled = false;
                    _txtParamCurrent[i].Text = string.Empty;
                    _txtParamCurrent[i].BackColor = Color.FromArgb(240, 240, 240);
                }
            }

            if (_txtParamTarget != null)
            {
                for (int i = 0; i < _txtParamTarget.Length; i++)
                {
                    if (_txtParamTarget[i] == null) continue;
                    _txtParamTarget[i].Enabled = false;
                    _txtParamTarget[i].Text = string.Empty;
                    _txtParamTarget[i].BackColor = Color.FromArgb(240, 240, 240);
                }
            }

            if (_txtParamPoint != null)
            {
                for (int i = 0; i < _txtParamPoint.Length; i++)
                {
                    if (_txtParamPoint[i] == null) continue;
                    _txtParamPoint[i].Text = string.Empty;
                }
            }

            if (_btnParamSave != null)
            {
                for (int i = 0; i < _btnParamSave.Length; i++)
                {
                    if (_btnParamSave[i] == null) continue;
                    _btnParamSave[i].Enabled = false;
                }
            }

            if (_btnParamReset != null)
            {
                for (int i = 0; i < _btnParamReset.Length; i++)
                {
                    if (_btnParamReset[i] == null) continue;
                    _btnParamReset[i].Enabled = false;
                }
            }
        }

        private void InitProcesses()
        {
            // 包含4个工序实例（工序1~工序4）
            _processes = new ProcessBase[]
            {
                new Process1Safety(),
                new Process2Software(),
                new Process3Alarm(), // 现在为工序3
                new Process5Power(),  // 现在为工序4
            };

            for (int i = 0; i < _processes.Length; i++)
            {
                int idx = i;
                _processes[i].LogMessage += msg => AppendLog(idx, msg, false);
                _processes[i].Finished   += pass => OnProcessFinished(idx, pass);
                // 订阅通用数据更新事件，用于工序2的温湿度与时间显示
                _processes[i].DataUpdate += (code, value) =>
                {
                    // BeginInvoke：避免工序线程在 UI 忙碌(Excel/弹窗)时被同步 Invoke 卡死
                    if (InvokeRequired) { BeginInvoke(new Action<int, string>(HandleDataUpdate), code, value); }
                    else HandleDataUpdate(code, value);
                };
            }

            // 每秒更新电脑时间显示（若工序2界面已创建则更新）
            var t = new System.Windows.Forms.Timer { Interval = 1000 };
            t.Tick += (s, e) => { if (_txtPcTime != null) _txtPcTime.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"); };
            t.Start();

            // 模块在线状态周期刷新（默认不启动）
            _moduleRefreshTimer = new System.Windows.Forms.Timer { Interval = 2000 }; // 2秒
            _moduleRefreshTimer.Tick += (s, e) => RefreshModuleStatus();
        }

        private void HandleDataUpdate(int code, string value)
        {
            // code 101/102: 工序1 ASCII 回包中的绝缘电压与漏电流
            if (code == 101)
            {
                SetSafetyMeasure(0, 0, value);
            }
            else if (code == 102)
            {
                SetSafetyMeasure(0, 1, value);
            }
            else if (code == 111)
            {
                SetSafetyMeasure(1, 0, value);
            }
            else if (code == 112)
            {
                SetSafetyMeasure(1, 1, value);
            }
            else if (code == 121)
            {
                SetSafetyMeasure(2, 0, value);
            }
            else if (code == 122)
            {
                SetSafetyMeasure(2, 1, value);
            }
            else if (code == 131)
            {
                SetSafetyMeasure(3, 0, value);
            }
            else if (code == 132)
            {
                SetSafetyMeasure(3, 1, value);
            }
            else if (code == 141)
            {
                SetSafetyMeasure(4, 0, value);
            }
            else if (code == 142)
            {
                SetSafetyMeasure(4, 1, value);
            }
            // code 1/2/3: 三个温度传感器
            else if (code >= 1 && code <= 3)
            {
                int t = code - 1;
                if (_txtTemps != null && _txtTemps.Length > t && _txtTemps[t] != null)
                    _txtTemps[t].Text = value;
            }
            // code 4: 湿度传感器4 (%RH，有符号显示)
            else if (code == 4)
            {
                if (_txtHumidity != null)
                    _txtHumidity.Text = value;
            }
            // code 10: 电脑时间 (由流程发出)
            else if (code == 10)
            {
                if (_txtPcTime != null) _txtPcTime.Text = value;
            }
            // code 11: 设备时间
            else if (code == 11)
            {
                if (_txtDeviceTime != null) _txtDeviceTime.Text = value;
            }
            else if (code >= 201 && code <= 202)
            {
                UpdateAlarmIndicator(_lblSpdAlarms, code - 201, value);
            }
            else if (code >= 240 && code <= 247)
            {
                UpdateAlarmIndicator(_lblBatFuseAlarms, code - 240, value);
            }
            else if (code >= 250 && code <= 253)
            {
                UpdateAlarmIndicator(_lblLoadFuseAlarms, code - 250, value);
            }
            else if (code >= 210 && code <= 217)
            {
                UpdateAlarmIndicator(_lblDiAlarms, code - 210, value);
            }
            else if (code >= 220 && code <= 227)
            {
                UpdateAlarmIndicator(_lblDoAlarms, code - 220, value);
            }
            else if (code >= 230 && code <= 233)
            {
                UpdateAlarmIndicator(_lblLvdAlarms, code - 230, value);
            }
            else if (code == 234)
            {
                UpdateAlarmIndicator(_lblBlvoAlarm, value);
            }
            else if (code >= 320 && code <= 327)
            {
                UpdateDoControlState(code - 320, value);
            }
            else if (code >= 330 && code <= 333)
            {
                UpdateLvdControlState(code - 330, value);
            }
            else if (code == 334)
            {
                UpdateBlvdControlState(value);
            }
        }

        private void UpdateProcessBindings()
        {
            foreach (var p in _processes)
                p.Initialize(_mes, _mc2900, _safety);
        }

        /// <summary>
        /// 将当前全局 MES 条码检查开关应用到所有工序实例上
        /// </summary>
        private void ApplyMesCheckToAllProcesses()
        {
            if (_processes == null) return;
            foreach (var p in _processes)
            {
                try { p.EnableMesBarcodeCheck = _mesCheckEnabled; } catch { }
            }
            UpdateMesCheckToggleButtonUi();
        }

        private void BtnMesCheckToggle_Click(object sender, EventArgs e)
        {
            _mesCheckEnabled = !_mesCheckEnabled;
            ApplyMesCheckToAllProcesses();
            if (!_mesCheckEnabled)
            {
                _lastMesPopupInfo = null;
                AppendLog(0, "MES检查已关闭：跳过条码校验/程序拉取/结果上传及相关弹窗限制");
            }
            else
            {
                AppendLog(0, "MES检查已开启：恢复条码校验与结果上传");
            }
        }

        private void UpdateMesCheckToggleButtonUi()
        {
            if (_btnMesCheckToggle == null) return;
            _btnMesCheckToggle.Text = _mesCheckEnabled ? "MES检查: 开" : "MES检查: 关";
            _btnMesCheckToggle.BackColor = _mesCheckEnabled ? Color.FromArgb(34, 139, 34) : Color.FromArgb(128, 128, 128);
            _btnMesCheckToggle.ForeColor = Color.White;
        }

        private void ResetMcBackgroundOperations()
        {
            var old = Interlocked.Exchange(ref _mcBackgroundCts, new CancellationTokenSource());
            try { old?.Cancel(); } catch { }
            try { old?.Dispose(); } catch { }
        }

        private void CancelMcBackgroundOperations()
        {
            _moduleRefreshTimer?.Stop();
            ResetMcBackgroundOperations();
        }

        // ════════════════════════════════════════════════════════
        //  连接操作
        // ════════════════════════════════════════════════════════
        private void BtnSafetyConnect_Click(object sender, EventArgs e)
        {
            if (_safetyBus.IsOpen)
            {
                _safetyBus.Close();
                _btnSafetyConnect.Text = "连接";
                SetSafetyStatus(false, "已断开");
                UpdateSafetyStandaloneButtonsState();
                return;
            }
            try
            {
                int baud = int.Parse(_cmbSafetyBaud.Text);
                _safetyBus.Open(_cmbSafetyPort.Text, baud);
                _safetyBus.Enabled = true; // 连接后默认启用通信
                _safety = new CS9933X20Driver(_safetyBus, 1);
                UpdateProcessBindings();
                _btnSafetyConnect.Text = "断开";
                SetSafetyStatus(true, $"已连接 {_cmbSafetyPort.Text} {baud}bps");
                UpdateSafetyStandaloneButtonsState();
            }
            catch (Exception ex)
            {
                SetSafetyStatus(false, "连接失败: " + ex.Message);
                UpdateSafetyStandaloneButtonsState();
            }
        }

        private void BtnMcConnect_Click(object sender, EventArgs e)
        {
            if (_mc2900Bus.IsOpen)
            {
                if (_currentRunningProcess == 1 || _currentRunningProcess == 2)
                {
                    MessageBox.Show("工序2或工序3运行中或停止处理中，必须先点击停止测试并等待停止完成后，才可以断开串口。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                StopModuleCommunicationTest(false);
                CancelMcBackgroundOperations();
                _mc2900Bus.Pause();
                _mc2900Bus.Release();
                // Close 放到后台，避免 UI 线程卡在 SerialPort.Close
                var busToClose = _mc2900Bus;
                _btnMcConnect.Enabled = false;
                Task.Run(() =>
                {
                    try { busToClose.Close(); } catch { }
                    BeginInvoke(new Action(() =>
                    {
                        _btnMcConnect.Text = "连接";
                        _btnMcConnect.Enabled = true;
                        SetMcStatus(false, "已断开");
                        UpdateModuleTestControlsState();
                        UpdateSyncTimeButtonState();
                        UpdateProcess3ControlButtonsState();
                    }));
                });
                return;
            }
            try
            {
                int baud = int.Parse(_cmbMcBaud.Text);
                ResetMcBackgroundOperations();
                _mc2900Bus.Open(_cmbMcPort.Text, baud);
                _mc2900Bus.Acquire();
                _mc2900Bus.Resume();
                ModbusUserAlert.ResetCrcAlert();
                _mc2900 = new MC2900Driver(_mc2900Bus, 1);
                UpdateProcessBindings();
                _btnMcConnect.Text = "断开";
                SetMcStatus(true, $"已连接 {_cmbMcPort.Text} {baud}bps");
                UpdateSyncTimeButtonState();
                UpdateModuleTestControlsState();
                UpdateProcess3ControlButtonsState();
                SetModuleTestStatus(false, "未测试");
            }
            catch (Exception ex)
            {
                SetMcStatus(false, "连接失败: " + ex.Message);
                UpdateProcess3ControlButtonsState();
            }
        }

        private void RefreshModuleStatus()
        {
            if (_mc2900 == null || !_moduleTestActive) return;
            // 已查到模块数量后，不再发送在线查询报文
            if (_moduleCountResolved) return;

            var token = _mcBackgroundCts.Token;
            if (token.IsCancellationRequested) return;
            // 防止重叠刷新
            if (Interlocked.Exchange(ref _moduleRefreshRunning, 1) == 1) return;

            Task.Factory.StartNew(() =>
            {
                if (token.IsCancellationRequested)
                {
                    Interlocked.Exchange(ref _moduleRefreshRunning, 0);
                    return;
                }

                ushort onlineCount;
                try
                {
                    // 仅查询一次在线模块数量，不再逐模块查询电流/在线状态
                    onlineCount = _mc2900.ReadOnlineModuleCount();
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Exchange(ref _moduleRefreshRunning, 0);
                    return;
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                        BeginInvoke(new Action(() => AppendLog(1, "查询在线模块数量失败: " + ex.Message)));
                    Interlocked.Exchange(ref _moduleRefreshRunning, 0);
                    return;
                }

                if (token.IsCancellationRequested || IsDisposed)
                {
                    Interlocked.Exchange(ref _moduleRefreshRunning, 0);
                    return;
                }

                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        ApplyOnlineModuleCount(onlineCount);
                        _moduleCountResolved = true;
                        _moduleRefreshTimer?.Stop();
                        AppendLog(1, $"已查询到在线模块数量: {_onlineModuleCount}，停止继续查询在线状态");
                    }));
                }
                finally { Interlocked.Exchange(ref _moduleRefreshRunning, 0); }
            });
        }

        // 更新模块按钮的启用/禁用状态（线程安全）
        private void UpdateModuleButtonsState(bool mcConnected)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<bool>(UpdateModuleButtonsState), mcConnected);
                return;
            }

            if (_btnModule == null)
                return;

            if (mcConnected && _moduleTestActive)
            {
                RefreshModuleButtonsFromState();
                return;
            }

            for (int i = 0; i < _btnModule.Length; i++)
            {
                var b = _btnModule[i];
                if (b == null) continue;
                b.Enabled = false;
                b.BackColor = Color.FromArgb(120, 120, 120);
                b.ForeColor = Color.White;
            }
        }

        private void UpdateModuleTestControlsState()
        {
            bool mcConnected = _mc2900 != null && _mc2900Bus != null && _mc2900Bus.IsOpen && _mc2900Bus.Enabled;

            if (!mcConnected)
            {
                UpdateModuleButtonsState(false);
                SetModuleTestStatus(false, "未连接");
                return;
            }

            if (_moduleTestActive)
            {
                UpdateModuleButtonsState(true);
            }
            else
            {
                UpdateModuleButtonsState(false);
                SetModuleTestStatus(false, "未测试");
            }
        }

        private bool EnsureMcBusReady(string actionName, bool allowDuringStart = false)
        {
            if (_mc2900 == null || _mc2900Bus == null || !_mc2900Bus.IsOpen)
                return false;

            // allowDuringStart=true 时允许在停止流程中恢复总线（用于发送停止包）
            bool canRecover = allowDuringStart
                || (!_stopRequested && (_currentRunningProcess == -1 || _currentRunningProcess == 1 || _currentRunningProcess == 2));

            if (!_mc2900Bus.Enabled)
            {
                if (!canRecover)
                    return false;

                _mc2900Bus.Acquire();
                ModbusDataLogger.LogInfo($"{actionName}: 检测到MC2900通信未启用，已自动重新启用通信");
            }

            if (_mc2900Bus.Paused)
            {
                if (!canRecover)
                    return false;

                _mc2900Bus.Resume();
                ModbusDataLogger.LogInfo($"{actionName}: 检测到MC2900处于暂停状态，已自动恢复通信");
            }

            return true;
        }

        private bool EnsureSafetyBusReady(string actionName, out bool acquiredHere)
        {
            acquiredHere = false;
            if (_safety == null)
                return false;

            var bus = _safety.GetBus();
            if (bus == null || !bus.IsOpen)
                return false;

            if (!bus.Enabled)
            {
                bus.Acquire();
                acquiredHere = true;
                ModbusDataLogger.LogInfo($"{actionName}: 检测到Safety通信未启用，已临时启用通信");
            }

            if (bus.Paused)
            {
                bus.Resume();
                ModbusDataLogger.LogInfo($"{actionName}: 检测到Safety处于暂停状态，已自动恢复通信");
            }

            return true;
        }

        private void SetModuleTestStatus(bool running, string text)
        {
            if (_lblModuleTestStatus == null)
                return;

            _lblModuleTestStatus.Text = text;
            _lblModuleTestStatus.ForeColor = running ? Color.Green : Color.Gray;
        }

        private void StopModuleCommunicationTest(bool writeLog, string reason = null)
        {
            _moduleRefreshTimer?.Stop();
            ResetMcBackgroundOperations();
            _moduleTestActive = false;
            ResetModuleStates();
            UpdateModuleButtonsState(false);
            UpdateModuleTestControlsState();
            SetModuleTestStatus(false, string.IsNullOrWhiteSpace(reason) ? "已停止" : reason);

            if (writeLog)
            {
                AppendLog(1, string.IsNullOrWhiteSpace(reason) ? "模块通信测试已停止" : reason);
            }
        }

        /// <summary>
        /// 工序2模块通信相关数据包：写入原始记录 + ModuleCapture.log + MES上传汇总.xlsx
        /// </summary>
        private void AppendProcess2ModulePacketLog(int step, string title, string status, string frameText, bool pass = true)
        {
            string raw = $"{status}{Environment.NewLine}{frameText ?? string.Empty}";

            if (_processes != null && _processes.Length > 1 && _processes[1].HasActiveResult())
            {
                _processes[1].AppendRuntimeRawRecord(step, title, raw, pass);
            }

            string barcode = (_processes != null && _processes.Length > 1 && _processes[1].HasActiveResult())
                ? _processes[1].GetActiveBarcode()
                : (_txtBarcode != null && _txtBarcode.Length > 1 && _txtBarcode[1] != null
                    ? (_txtBarcode[1].Text?.Trim() ?? "-")
                    : "-");
            if (string.IsNullOrWhiteSpace(barcode))
                barcode = "-";

            try
            {
                var header = TryBuildMesHeaderForWorkbook(barcode, "PASS");
                string path = MesUploadWorkbookWriter.SaveModuleCaptureRecord(
                    barcode, title, status, frameText, _mesCheckEnabled, header);
                AppendLog(1, $"模块通信数据包已写入MES上传汇总: {title}");
                AppendLog(1, "模块通信抓包文件: " + path);
            }
            catch (Exception ex)
            {
                AppendLog(1, $"模块通信数据包写入MES上传汇总失败: {title}, {ex.Message}");
            }
        }

        private void AttachPendingProcess2StartRawIfReady()
        {
            if (string.IsNullOrWhiteSpace(_process2PendingStartRaw))
                return;

            if (_processes == null || _processes.Length <= 1 || !_processes[1].HasActiveResult())
                return;

            _processes[1].AppendRuntimeRawRecord(70,
                "工序2-模块通信启动原始记录",
                _process2PendingStartRaw);
            _process2PendingStartRaw = null;
        }

        /// <summary>
        /// 南向485测试报文：记录到工序2 ---原始记录---，并同步 MES上传汇总.xlsx。
        /// </summary>
        private void AppendSouth485PacketLog(string direction, string hex)
        {
            string safeDirection = string.IsNullOrWhiteSpace(direction) ? "UNKNOWN" : direction.Trim();
            string safeHex = hex ?? string.Empty;
            string title = $"工序2-南向485{safeDirection}原始记录";
            string status = $"{safeDirection}[HEX]={safeHex}";
            string raw = $"{status}{Environment.NewLine}南向485测试";

            bool hasActiveProcess2 = _processes != null && _processes.Length > 1 && _processes[1].HasActiveResult();
            if (!hasActiveProcess2)
                return;

            int step = string.Equals(safeDirection, "TX", StringComparison.OrdinalIgnoreCase) ? 80 : 81;
            _processes[1].AppendRuntimeRawRecord(step, title, raw);
            string barcode = _processes[1].GetActiveBarcode();

            try
            {
                var header = TryBuildMesHeaderForWorkbook(barcode, "PASS");
                MesUploadWorkbookWriter.SaveSouth485CaptureRecord(
                    barcode, safeDirection, safeHex, _mesCheckEnabled, header);
            }
            catch (Exception ex)
            {
                AppendLog(1, $"南向485报文写入MES上传汇总失败: {ex.Message}");
            }
        }

        // 简单映射：将工序索引映射到要高亮的模块列表。默认映射为同索引的模块。
        // 若需自定义映射，请修改此方法。
        private int[] MapProcessToModules(int processIdx)
        {
            if (_btnModule == null) return Array.Empty<int>();
            if (processIdx == 1) return Array.Empty<int>();
            if (processIdx >= 0 && processIdx < _btnModule.Length)
                return new[] { processIdx };
            return Array.Empty<int>();
        }

        // 高亮/恢复与某工序相关的模块按钮（线程安全）
        private void HighlightModuleForProcess(int processIdx, bool highlight)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<int, bool>(HighlightModuleForProcess), processIdx, highlight);
                return;
            }

            var modules = MapProcessToModules(processIdx);
            Color activeColor = Color.FromArgb(0, 180, 60);   // 高亮色
            Color normalColor = Color.FromArgb(0, 120, 215);  // 普通已启用色

            foreach (var m in modules)
            {
                if (m < 0 || _btnModule == null || m >= _btnModule.Length) continue;
                var b = _btnModule[m];
                if (b == null) continue;
                if (highlight)
                {
                    // 仅通过背景色高亮，不使用黄色边框
                    b.BackColor = activeColor;
                }
                else
                {
                    b.BackColor = b.Enabled ? normalColor : Color.FromArgb(120, 120, 120);
                    b.FlatAppearance.BorderSize = 0;
                }
            }
        }

        private async void OnModuleButtonClick(int moduleIndex)
        {
            if (!_moduleTestActive)
            {
                AppendLog(1, "请先点击模块通信测试开始按钮");
                return;
            }

            var btn = _btnModule[moduleIndex];
            if (btn == null) return;

            if (moduleIndex < 0 || moduleIndex >= _moduleOnlineStates.Length || !_moduleOnlineStates[moduleIndex])
            {
                AppendLog(1, $"模块{moduleIndex + 1} 当前不在线，无法控制");
                return;
            }

            bool currentPowerOn = _modulePowerStates[moduleIndex];
            bool nextPowerOn = !currentPowerOn;

            if (Interlocked.Exchange(ref _moduleControlRunning, 1) == 1)
            {
                AppendLog(1, $"模块{moduleIndex + 1} 正在执行控制，请稍后重试");
                return;
            }

            try
            {
                _moduleRefreshTimer?.Stop();
                btn.Enabled = false;
                long startSequence = ModbusDataLogger.GetLatestSequence();
                using (_mc2900.SuspendQueryPolling())
                {
                    await Task.Run(() => _mc2900.SetModulePowerState(moduleIndex, nextPowerOn));
                }
                _modulePowerStates[moduleIndex] = nextPowerOn;
                RefreshModuleButtonsFromState();
                string status = $"控制结果={(nextPowerOn ? "上电" : "关机")} Result=成功";
                string frameText = ModbusDataLogger.FramesToText(ModbusDataLogger.GetFramesAfter(startSequence, ModbusDataLogger.PortType.MC2900));
                AppendProcess2ModulePacketLog(
                    24 + moduleIndex,
                    $"工序2-模块{moduleIndex + 1}{(nextPowerOn ? "启动" : "关机")}原始记录",
                    status,
                    frameText);
                string okMsg = nextPowerOn
                    ? $"模块{moduleIndex + 1}启动成功"
                    : $"模块{moduleIndex + 1}关闭成功";
                AppendLog(1, okMsg);
                ShowModuleControlPrompt(okMsg, true);
            }
            catch (Exception ex)
            {
                // 关闭命令无论成功/失败都显示灰色；启动失败保持非启动(灰色)
                if (!nextPowerOn)
                    _modulePowerStates[moduleIndex] = false;
                RefreshModuleButtonsFromState();
                string failMsg = nextPowerOn
                    ? $"模块{moduleIndex + 1}启动失败: {ex.Message}"
                    : $"模块{moduleIndex + 1}关闭失败: {ex.Message}";
                AppendLog(1, failMsg);
                ShowModuleControlPrompt(failMsg, false);
            }
            finally
            {
                Interlocked.Exchange(ref _moduleControlRunning, 0);
                if (_moduleTestActive)
                    RefreshModuleButtonsFromState();

                UpdateModuleBatchControlButtonsState();
            }
        }

        private void BtnModulePowerAllOff_Click(object sender, EventArgs e)
        {
            _ = ExecuteModulePowerBatchAsync(false);
        }

        private void BtnModulePowerAllOn_Click(object sender, EventArgs e)
        {
            _ = ExecuteModulePowerBatchAsync(true);
        }

        private async Task ExecuteModulePowerBatchAsync(bool powerOn)
        {
            if (!_moduleTestActive)
            {
                AppendLog(1, "请先点击模块通信测试开始按钮");
                return;
            }

            if (_mc2900 == null || _mc2900Bus == null || !_mc2900Bus.IsOpen || !_mc2900Bus.Enabled)
            {
                AppendLog(1, "MC2900通信未就绪，无法执行模块批量控制");
                return;
            }

            if (Interlocked.Exchange(ref _moduleControlRunning, 1) == 1)
            {
                AppendLog(1, "模块控制命令执行中，请稍后重试");
                return;
            }

            int[] moduleIndexes = new int[_moduleOnlineStates.Length];
            int moduleCount = 0;
            for (int i = 1; i < _moduleOnlineStates.Length; i++)
            {
                if (_moduleOnlineStates[i])
                    moduleIndexes[moduleCount++] = i;
            }

            if (moduleCount == 0)
            {
                AppendLog(1, "没有可控制的在线模块（模块1除外）。");
                Interlocked.Exchange(ref _moduleControlRunning, 0);
                return;
            }

            _moduleRefreshTimer?.Stop();
            long startSequence = ModbusDataLogger.GetLatestSequence();
            int successCount = 0;
            int failCount = 0;

            try
            {
                using (_mc2900.SuspendQueryPolling())
                {
                    for (int i = 0; i < moduleCount; i++)
                    {
                        int moduleIndex = moduleIndexes[i];
                        try
                        {
                            await Task.Run(() => _mc2900.SetModulePowerState(moduleIndex, powerOn));
                            _modulePowerStates[moduleIndex] = powerOn;
                            successCount++;
                            RefreshModuleButtonsFromState();

                            string okMsg = powerOn
                                ? $"模块{moduleIndex + 1}启动成功"
                                : $"模块{moduleIndex + 1}关闭成功";
                            AppendLog(1, okMsg);
                        }
                        catch (Exception ex)
                        {
                            failCount++;
                            if (!powerOn)
                                _modulePowerStates[moduleIndex] = false;
                            RefreshModuleButtonsFromState();
                            string failMsg = powerOn
                                ? $"模块{moduleIndex + 1}启动失败: {ex.Message}"
                                : $"模块{moduleIndex + 1}关闭失败: {ex.Message}";
                            AppendLog(1, failMsg);
                        }

                        // 全部启动：每块之间延时3秒；全部关闭：不延时
                        if (powerOn && i < moduleCount - 1)
                            await Task.Delay(ModuleBatchStartDelayMs);
                    }
                }

                RefreshModuleButtonsFromState();

                string moduleList = string.Empty;
                for (int i = 0; i < moduleCount; i++)
                {
                    if (i > 0) moduleList += ",";
                    moduleList += (moduleIndexes[i] + 1).ToString();
                }

                if (_processes != null && _processes.Length > 1)
                {
                    string frameText = ModbusDataLogger.FramesToText(ModbusDataLogger.GetFramesAfter(startSequence, ModbusDataLogger.PortType.MC2900));
                    string status = $"目标状态={(powerOn ? "启动" : "关闭")}的模块: {moduleList} 成功={successCount} 失败={failCount}";
                    AppendProcess2ModulePacketLog(
                        powerOn ? 50 : 60,
                        $"工序2-模块批量{(powerOn ? "启动" : "关闭")}原始记录",
                        status,
                        frameText,
                        failCount == 0);
                }

                string summary = powerOn
                    ? $"全部启动完成（模块1除外）\n成功: {successCount} 块\n失败: {failCount} 块"
                    : $"全部关闭完成（模块1除外）\n成功: {successCount} 块\n失败: {failCount} 块";
                AppendLog(1, summary.Replace("\n", "，"));
                ShowModuleControlPrompt(summary, failCount == 0);
            }
            catch (Exception ex)
            {
                string failMsg = "模块批量控制失败: " + ex.Message;
                AppendLog(1, failMsg);
                ShowModuleControlPrompt(failMsg, false);
            }
            finally
            {
                Interlocked.Exchange(ref _moduleControlRunning, 0);
                if (_moduleTestActive)
                    RefreshModuleButtonsFromState();
                UpdateModuleBatchControlButtonsState();
            }
        }

        private void ShowModuleControlPrompt(string message, bool success)
        {
            if (IsDisposed || string.IsNullOrWhiteSpace(message))
                return;

            MessageBox.Show(
                message,
                success ? "模块控制成功" : "模块控制失败",
                MessageBoxButtons.OK,
                success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void ResetModuleStates()
        {
            _onlineModuleCount = 0;
            _moduleCountResolved = false;
            for (int i = 0; i < _moduleOnlineStates.Length; i++)
            {
                _moduleOnlineStates[i] = false;
                _modulePowerStates[i] = false;
            }
            RefreshModuleButtonsFromState();
        }

        private void ApplyOnlineModuleCount(ushort onlineCount)
        {
            int normalizedCount = Math.Max(0, Math.Min(_btnModule.Length, onlineCount));
            _onlineModuleCount = normalizedCount;

            for (int i = 0; i < _btnModule.Length; i++)
            {
                bool online = i < normalizedCount;
                _moduleOnlineStates[i] = online;
                // 查到在线模块后直接显示蓝色；离线保持灰色
                _modulePowerStates[i] = online;
            }

            SetModuleTestStatus(true, $"测试中,在线{normalizedCount}块");
            RefreshModuleButtonsFromState();
        }

        private void RefreshModuleButtonsFromState()
        {
            if (_btnModule == null)
                return;

            for (int i = 0; i < _btnModule.Length; i++)
            {
                var button = _btnModule[i];
                if (button == null)
                    continue;

                bool canControl = _moduleTestActive && _moduleOnlineStates[i];
                button.Enabled = canControl;

                // 在线/启动=蓝色；关闭/离线=灰色
                if (_moduleTestActive && _moduleOnlineStates[i] && _modulePowerStates[i])
                    button.BackColor = Color.FromArgb(0, 120, 215);
                else
                    button.BackColor = Color.FromArgb(120, 120, 120);

                button.ForeColor = Color.White;
            }

            UpdateModuleBatchControlButtonsState();
        }

        private void UpdateModuleBatchControlButtonsState()
        {
            if (_btnModuleAllOff == null || _btnModuleAllOn == null)
                return;

            bool canBatch = _moduleTestActive &&
                _mc2900 != null &&
                _mc2900Bus != null &&
                _mc2900Bus.IsOpen &&
                _mc2900Bus.Enabled &&
                !_mc2900Bus.Paused;

            if (canBatch)
            {
                bool hasOnlineModules = false;
                for (int i = 1; i < _moduleOnlineStates.Length; i++)
                {
                    if (_moduleOnlineStates[i])
                    {
                        hasOnlineModules = true;
                        break;
                    }
                }
                canBatch = hasOnlineModules;
            }

            _btnModuleAllOff.Enabled = canBatch;
            _btnModuleAllOn.Enabled = canBatch;
        }

        private async void BtnSyncTime_Click(object sender, EventArgs e)
        {
            if (_mc2900 == null || _mc2900Bus == null || !_mc2900Bus.IsOpen || !_mc2900Bus.Enabled)
                return;

            if (_stopRequested || _currentRunningProcess != 1)
            {
                AppendLog(1, "当前状态不允许同步时间");
                UpdateSyncTimeButtonState();
                return;
            }

            _btnSyncTime.Enabled = false;

            try
            {
                DateTime now = DateTime.Now;
                if (_txtPcTime != null)
                    _txtPcTime.Text = now.ToString("yyyy-MM-dd HH:mm:ss");

                DateTime deviceTime;
                long startSequence = ModbusDataLogger.GetLatestSequence();
                using (_mc2900.SuspendQueryPolling())
                {
                    deviceTime = await Task.Run(() => _mc2900.SyncSystemTime(2, 2));
                }
                int diffSeconds = (int)Math.Abs(Math.Round((DateTime.Now - deviceTime).TotalSeconds, MidpointRounding.AwayFromZero));

                if (_txtDeviceTime != null)
                    _txtDeviceTime.Text = deviceTime.ToString("yyyy-MM-dd HH:mm:ss");

                if (_processes != null && _processes.Length > 1)
                {
                    string frameText = ModbusDataLogger.FramesToText(ModbusDataLogger.GetFramesAfter(startSequence, ModbusDataLogger.PortType.MC2900));
                    _processes[1].AppendRuntimeRawRecord(27,
                        "工序2-同步时间原始记录",
                        $"电脑时间={now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}设备时间={deviceTime:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}差值={diffSeconds}秒{Environment.NewLine}{frameText}");
                }

                AppendLog(1, $"同步时间成功: {deviceTime:yyyy-MM-dd HH:mm:ss}，与电脑时间相差{diffSeconds}秒");
                MessageBox.Show("同步时间成功", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                AppendLog(1, "MC2900通信已暂停，无法同步时间");
                MessageBox.Show("同步时间失败", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                AppendLog(1, "同步时间失败: " + ex.Message);
                MessageBox.Show("同步时间失败", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                UpdateSyncTimeButtonState();
            }
        }

        private void UpdateSyncTimeButtonState()
        {
            if (_btnSyncTime == null)
                return;

            _btnSyncTime.Enabled =
                _mc2900 != null &&
                _mc2900Bus != null &&
                _mc2900Bus.IsOpen &&
                _mc2900Bus.Enabled &&
                !_mc2900Bus.Paused &&
                !_stopRequested &&
                _currentRunningProcess == 1;

            UpdateModuleTestControlsState();
            UpdateProcess3ClearActionButtonsState();
        }

        private void UpdateProcess3ClearActionButtonsState()
        {
            bool canOperate =
                _mc2900 != null &&
                _mc2900Bus != null &&
                _mc2900Bus.IsOpen &&
                _mc2900Bus.Enabled &&
                !_mc2900Bus.Paused &&
                !_stopRequested &&
                _currentRunningProcess == 2;

            if (_btnClearRecord != null)
                _btnClearRecord.Enabled = canOperate;

            if (_btnClearModuleAlarmRecord != null)
                _btnClearModuleAlarmRecord.Enabled = canOperate;
        }

        private async void BtnClearRecord_Click(object sender, EventArgs e)
        {
            if (_btnClearRecord == null)
                return;

            if (_currentRunningProcess != 2 || _processes == null || _processes.Length <= 2 || !_processes[2].HasActiveResult())
            {
                MessageBox.Show("请先启动工序3，再执行清除记录，确保原始记录写入测试文件", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                UpdateProcess3ClearActionButtonsState();
                return;
            }

            if (!EnsureMcBusReady("工序3清除记录", true))
            {
                MessageBox.Show("请先连接MC2900", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                UpdateProcess3ClearActionButtonsState();
                return;
            }

            _btnClearRecord.Enabled = false;
            if (_btnClearModuleAlarmRecord != null)
                _btnClearModuleAlarmRecord.Enabled = false;

            try
            {
                AppendLog(2, "开始清除工序3所有记录");
                long startSequence = ModbusDataLogger.GetLatestSequence();
                using (_mc2900.SuspendQueryPolling())
                {
                    await Task.Run(() => _mc2900.ClearAllRecords());
                }

                if (_processes != null && _processes.Length > 2)
                {
                    string frameText = ModbusDataLogger.FramesToText(ModbusDataLogger.GetFramesAfter(startSequence, ModbusDataLogger.PortType.MC2900));
                    _processes[2].AppendRuntimeRawRecord(50,
                        "工序3-清除所有记录原始记录",
                        frameText);
                }

                AppendLog(2, "工序3清除记录成功，设备已返回确认回包");
                MessageBox.Show("清除所有记录成功", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                AppendLog(2, "工序3清除记录失败: " + ex.Message);
                MessageBox.Show("清除所有记录失败", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                UpdateProcess3ClearActionButtonsState();
            }
        }

        private async void BtnClearModuleAlarmRecord_Click(object sender, EventArgs e)
        {
            if (_btnClearModuleAlarmRecord == null)
                return;

            if (_currentRunningProcess != 2 || _processes == null || _processes.Length <= 2 || !_processes[2].HasActiveResult())
            {
                MessageBox.Show("请先启动工序3，再执行清除模块告警记录，确保原始记录写入测试文件", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                UpdateProcess3ClearActionButtonsState();
                return;
            }

            if (!EnsureMcBusReady("工序3清除模块告警记录", true))
            {
                MessageBox.Show("请先连接MC2900", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                UpdateProcess3ClearActionButtonsState();
                return;
            }

            _btnClearModuleAlarmRecord.Enabled = false;
            if (_btnClearRecord != null)
                _btnClearRecord.Enabled = false;

            try
            {
                AppendLog(2, "开始清除工序3模块告警记录");
                long startSequence = ModbusDataLogger.GetLatestSequence();
                using (_mc2900.SuspendQueryPolling())
                {
                    await Task.Run(() => _mc2900.ClearModuleAlarmRecords());
                }

                if (_processes != null && _processes.Length > 2)
                {
                    string frameText = ModbusDataLogger.FramesToText(ModbusDataLogger.GetFramesAfter(startSequence, ModbusDataLogger.PortType.MC2900));
                    _processes[2].AppendRuntimeRawRecord(51,
                        "工序3-清除模块告警记录原始记录",
                        frameText);
                }

                AppendLog(2, "工序3清除模块告警记录成功，设备已返回确认回包");
                MessageBox.Show("清除模块告警记录成功", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                AppendLog(2, "工序3清除模块告警记录失败: " + ex.Message);
                MessageBox.Show("清除模块告警记录失败", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                UpdateProcess3ClearActionButtonsState();
            }
        }

        private void ProcessTab_Selecting(object sender, TabControlCancelEventArgs e)
        {
            if (_tabProcesses == null)
                return;

            if ((_currentRunningProcess == 1 && e.TabPageIndex != 1)
                || (_currentRunningProcess == 2 && e.TabPageIndex != 2)
                || (_currentRunningProcess == 3 && e.TabPageIndex != 3))
            {
                e.Cancel = true;
                MessageBox.Show("当前工序运行中或停止处理中，必须先点击停止测试并等待停止完成后，才可以切换到其他工序。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async void StopProcess(int idx)
        {
            if (_currentRunningProcess != idx)
                return;

            _stopRequested = true;
            _btnStop[idx].Enabled = false;
            SetResult(idx, "停止中...", Color.Gray);
            AppendLog(idx, "[用户请求停止]");

            // 工序4手动校准：无后台 Run 线程，直接收尾
            if (idx == 3 && _paramCalibrationManualMode)
            {
                UnlockDeviceIp();
                ResetProcess4CalibrationControls();
                SetCalibrationMode(false);
                _paramCalibrationManualMode = false;
                AppendLog(idx, "[用户请求停止] 正在保存工序4会话结果...");
                SaveProcess4SessionResultFile();
                FlushProcess4LogToFile();
                UpdateProcess4IpInputState();
                _currentRunningProcess = -1;
                _btnStart[idx].Enabled = true;
                _btnStop[idx].Enabled = false;
                SetResult(idx, "已停止", Color.Gray);
                HighlightModuleForProcess(idx, false);
                _stopRequested = false;
                UpdateAllRunStatus();
                UpdateSyncTimeButtonState();
                UpdateProcess3ControlButtonsState();
                UpdateSafetyStandaloneButtonsState();
                return;
            }

            // 1) 先取消令牌，让工序循环尽快退出
            try { _processStopCts?.Cancel(); } catch { }

            // 2) 暂停总线，打断正在等待的收发（避免与停机指令抢锁卡死）
            if (idx == 0)
            {
                try { _safetyBus?.Pause(); } catch { }
            }
            else if (idx == 1 || idx == 2)
            {
                try { _moduleRefreshTimer?.Stop(); } catch { }
                CancelMcBackgroundOperations();
                try { _mc2900Bus?.Pause(); } catch { }
            }

            // 给工序线程一点时间释放串口锁
            await Task.Delay(80);

            // 3) 再发设备侧停止指令（临时恢复总线）
            if (idx == 1 || idx == 2)
            {
                try
                {
                    try { _mc2900Bus?.Resume(); } catch { }

                    if (!EnsureMcBusReady(idx == 1 ? "工序2停止" : "工序3停止", true))
                        throw new InvalidOperationException("MC2900通信未就绪，无法发送停止数据包");

                    long stopStartSequence = ModbusDataLogger.GetLatestSequence();
                    using (_mc2900.SuspendQueryPolling())
                    {
                        var stopTask = Task.Run(() => _mc2900.SetSystemStopMode());
                        var finished = await Task.WhenAny(stopTask, Task.Delay(4000)).ConfigureAwait(true);
                        if (finished != stopTask)
                            throw new TimeoutException("发送停止包超时(4s)");
                        await stopTask.ConfigureAwait(true);
                    }

                    string stopFrameText = ModbusDataLogger.FramesToText(
                        ModbusDataLogger.GetFramesAfter(stopStartSequence, ModbusDataLogger.PortType.MC2900));
                    string stopStatus = idx == 1
                        ? "模块通信停止: 退出手动模式(FC05 0x0015=0x0001) Result=成功"
                        : $"工序{idx + 1}停止: 退出手动模式(FC05 0x0015=0x0001) Result=成功";

                    if (idx == 1)
                    {
                        AttachPendingProcess2StartRawIfReady();
                        AppendProcess2ModulePacketLog(71, "工序2-模块通信停止原始记录", stopStatus, stopFrameText);
                        StopModuleCommunicationTest(true, "模块通信测试已停止，停止数据包已记录");
                    }
                    else if (_processes != null && _processes.Length > idx && _processes[idx].HasActiveResult())
                    {
                        _processes[idx].AppendRuntimeRawRecord(71,
                            $"工序{idx + 1}-停止原始记录",
                            $"{stopStatus}{Environment.NewLine}{stopFrameText}");
                    }

                    AppendLog(idx, $"工序{idx + 1}停止包发送成功，已收到设备确认回包");
                }
                catch (Exception ex)
                {
                    AppendLog(idx, $"工序{idx + 1}停止包发送失败(流程仍会停止): {ex.Message}");
                    if (idx == 1)
                    {
                        try { StopModuleCommunicationTest(false, "模块通信测试已停止"); } catch { }
                    }
                }
                finally
                {
                    // 若工序线程尚未结束，保持暂停避免继续跑；结束后 OnProcessFinished 会 Resume
                    if (_currentRunningProcess == idx)
                    {
                        try { _mc2900Bus?.Pause(); } catch { }
                    }
                }
            }
            else if (idx == 0 && _safety != null)
            {
                _ = Task.Run(() =>
                {
                    bool acquiredHere = false;
                    try
                    {
                        try { _safetyBus?.Resume(); } catch { }

                        if (!EnsureSafetyBusReady("工序1停止", out acquiredHere))
                            return;

                        SafetyAsciiProtocol.TryStopAndExitRemote(_safety.GetBus(), msg => AppendLog(0, msg));
                    }
                    catch (Exception ex)
                    {
                        AppendLog(0, "工序1停止指令异常: " + ex.Message);
                    }
                    finally
                    {
                        if (acquiredHere)
                        {
                            try { _safety.GetBus()?.Release(); } catch { }
                        }
                    }
                });
            }

            UpdateSyncTimeButtonState();
            UpdateProcess3ControlButtonsState();
            UpdateSafetyStandaloneButtonsState();
            ModbusDataLogger.LogInfo($"工序{idx + 1}停止请求已发出");

            // 若工序线程已结束但 Finished 回调未把 UI 收尾，兜底复位
            await Task.Delay(200);
            if (_stopRequested && _currentRunningProcess == idx
                && (_processes == null || ProcessIdxInactive(idx)))
            {
                FinalizeStoppedProcessUi(idx);
            }
        }

        private bool ProcessIdxInactive(int idx)
        {
            try
            {
                return _processes == null
                    || idx < 0
                    || idx >= _processes.Length
                    || _processes[idx] == null
                    || !_processes[idx].HasActiveResult();
            }
            catch
            {
                return true;
            }
        }

        private void FinalizeStoppedProcessUi(int idx)
        {
            if (_currentRunningProcess != idx)
                return;

            if (idx > 0 && _mc2900Bus != null && _mc2900Bus.IsOpen)
            {
                try { _mc2900Bus.Resume(); } catch { }
            }
            if (idx == 0 && _safetyBus != null)
            {
                try { _safetyBus.Resume(); } catch { }
            }

            if (idx == 1)
            {
                try { StopModuleCommunicationTest(false); } catch { }
            }

            _currentRunningProcess = -1;
            _btnStart[idx].Enabled = true;
            _btnStop[idx].Enabled = false;
            SetResult(idx, "已停止", Color.Gray);
            HighlightModuleForProcess(idx, false);
            _stopRequested = false;
            UpdateAllRunStatus();
            UpdateSyncTimeButtonState();
            UpdateProcess3ControlButtonsState();
            UpdateSafetyStandaloneButtonsState();
        }

        private void OnProcessFinished(int idx, bool pass)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<int, bool>(OnProcessFinished), idx, pass);
                return;
            }

            if (_currentRunningProcess != idx)
                return;

            if (_stopRequested)
            {
                FinalizeStoppedProcessUi(idx);
                return;
            }

            if (idx == 3 && _paramCalibrationManualMode)
            {
                ResetProcess4CalibrationControls();
                SetCalibrationMode(false);
                _paramCalibrationManualMode = false;
                try { SaveProcess4SessionResultFile(); } catch { }
                FlushProcess4LogToFile();
            }

            if (idx > 0 && _mc2900Bus != null && _mc2900Bus.IsOpen)
                _mc2900Bus.Resume();

            _currentRunningProcess = -1;
            _btnStart[idx].Enabled = true;
            _btnStop[idx].Enabled  = false;
            SetResult(idx, pass ? "PASS" : "FAIL", pass ? Color.LimeGreen : Color.OrangeRed);

            HighlightModuleForProcess(idx, false);
            UpdateAllRunStatus();
            UpdateSyncTimeButtonState();
            UpdateProcess3ControlButtonsState();
            UpdateSafetyStandaloneButtonsState();
        }
        
        /// <summary>
        /// 更新所有工序的运行状态标签
        /// </summary>
        private void UpdateAllRunStatus()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(UpdateAllRunStatus));
                return;
            }
            
            for (int i = 0; i < _lblRunStatus.Length; i++)
            {
                if (_lblRunStatus[i] != null)
                {
                    if (i == _currentRunningProcess)
                    {
                        _lblRunStatus[i].Text = "● 运行中";
                        _lblRunStatus[i].ForeColor = Color.Green;
                    }
                    else
                    {
                        _lblRunStatus[i].Text = "● 空闲";
                        _lblRunStatus[i].ForeColor = Color.Gray;
                    }
                }
            }

            UpdateSafetyStandaloneButtonsState();
        }

        // ════════════════════════════════════════════════════════
        //  辅助方法
        // ════════════════════════════════════════════════════════
        private void AppendLog(int idx, string msg)
        {
            AppendLog(idx, msg, true);
        }

        private void AppendLog(int idx, string msg, bool mirrorToRuntimeLog)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<int, string, bool>(AppendLog), idx, msg, mirrorToRuntimeLog);
                return;
            }

            var rtb = _rtbLog[idx];
            if (rtb != null)
            {
                rtb.AppendText(msg + "\n");
                rtb.ScrollToCaret();
            }

            if (idx == 3 && _process4LogBuffer != null)
            {
                lock (_process4LogLock)
                {
                    _process4LogBuffer.AppendLine(msg);
                }
            }

            // 工序1~3手动操作等 UI 日志同步写入测试结果文件的 ---运行日志---
            if (mirrorToRuntimeLog
                && idx >= 0 && idx <= 2
                && _processes != null
                && idx < _processes.Length
                && _processes[idx] != null)
            {
                _processes[idx].AppendExternalRuntimeLog(msg);
            }
        }

        private void BeginProcess4Log(string barcode)
        {
            lock (_process4LogLock)
            {
                _process4LogBuffer = new StringBuilder();
                _process4Barcode = string.IsNullOrWhiteSpace(barcode) ? "-" : barcode.Trim();
                _process4StartTime = DateTime.Now;
                _process4SaveOkCount = 0;
                _process4SaveFailCount = 0;
                _process4SaveDetails.Clear();

                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "Process4");
                Directory.CreateDirectory(dir);
                _process4LogPath = Path.Combine(dir, $"{_process4Barcode}_{_process4StartTime:yyyyMMdd_HHmmss}_process4.log");

                _process4LogBuffer.AppendLine("===工序4运行日志开始===");
                _process4LogBuffer.AppendLine($"条码: {_process4Barcode}");
                _process4LogBuffer.AppendLine($"开始: {_process4StartTime:yyyy-MM-dd HH:mm:ss}");
            }
        }

        private void FlushProcess4LogToFile()
        {
            lock (_process4LogLock)
            {
                if (_process4LogBuffer == null || string.IsNullOrWhiteSpace(_process4LogPath))
                    return;

                try
                {
                    File.WriteAllText(_process4LogPath, _process4LogBuffer.ToString(), Encoding.UTF8);
                }
                catch { }

                _process4LogBuffer = null;
                _process4LogPath = null;
            }
        }

        /// <summary>
        /// 工序4停止时生成与工序1~3格式一致的标准结果 txt，并写入 MES上传汇总。
        /// </summary>
        private string SaveProcess4SessionResultFile()
        {
            string barcode = string.IsNullOrWhiteSpace(_process4Barcode) ? "-" : _process4Barcode;
            DateTime start = _process4StartTime == default(DateTime) ? DateTime.Now : _process4StartTime;
            DateTime end = DateTime.Now;
            bool noSaveAction = _process4SaveOkCount == 0 && _process4SaveFailCount == 0;
            bool pass = _process4SaveFailCount == 0 && _process4SaveOkCount > 0;
            string resultText = pass ? "PASS" : "FAIL";

            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "Process4");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"{barcode}_{start:yyyyMMdd_HHmmss}.txt");

            var detailsSb = new StringBuilder();
            int step = 1;
            foreach (string line in _process4SaveDetails)
            {
                detailsSb.Append(MesClient.BuildDetail(step++, 0, 0, 0,
                    line.IndexOf("Result=FAIL", StringComparison.OrdinalIgnoreCase) < 0,
                    line, 0));
            }

            string runtimeLog;
            lock (_process4LogLock)
            {
                runtimeLog = _process4LogBuffer != null ? _process4LogBuffer.ToString() : string.Empty;
            }

            using (var w = new StreamWriter(path, false, Encoding.UTF8))
            {
                w.WriteLine($"条码: {barcode}");
                w.WriteLine("工序: 4");
                w.WriteLine($"结果: {resultText}");
                w.WriteLine($"开始: {start:yyyy-MM-dd HH:mm:ss}");
                w.WriteLine($"结束: {end:yyyy-MM-dd HH:mm:ss}");
                w.WriteLine($"保存成功: {_process4SaveOkCount}");
                w.WriteLine($"保存失败: {_process4SaveFailCount}");
                w.WriteLine("---测试项目---");
                foreach (string line in _process4SaveDetails)
                    w.WriteLine(line);
                w.WriteLine("---运行日志---");
                w.Write(runtimeLog);
            }

            try
            {
                var header = TryBuildMesHeaderForWorkbook(barcode, resultText);
                string workbookPath = MesUploadWorkbookWriter.SaveUploadWorkbook(
                    barcode,
                    "参数校准",
                    resultText,
                    detailsSb.ToString(),
                    path,
                    runtimeLog,
                    _lockedDeviceIp,
                    null,
                    4,
                    null,
                    _mesCheckEnabled,
                    header);
                AppendLog(3, "工序4会话结果已写入MES上传汇总: " + workbookPath);
            }
            catch (Exception ex)
            {
                AppendLog(3, "工序4会话结果写入MES上传汇总失败: " + ex.Message);
            }

            try
            {
                if (noSaveAction)
                {
                    AppendLog(3, $"工序4会话未执行任何保存动作，结果记为FAIL，跳过MES上传。条码={barcode}");
                }
                else if (!_mesCheckEnabled)
                {
                    AppendLog(3, $"已关闭 MES 检查，跳过 SaveTestData 上传。条码={barcode}");
                }
                else
                {
                    bool uploaded = _mes != null && _mes.SaveTestData(barcode, resultText, detailsSb.ToString(), path);
                    AppendLog(3, uploaded
                        ? $"工序4会话 MES上传成功, 条码={barcode}"
                        : $"工序4会话 MES上传失败或未启用, 条码={barcode}");
                }
            }
            catch (Exception ex)
            {
                AppendLog(3, $"工序4会话 MES上传异常: {ex.Message}");
            }

            AppendLog(3, "工序4会话结果文件: " + path);
            return path;
        }

        /// <summary>
        /// 工序4保存按钮专用日志：立即追加写入文件。
        /// 格式：时间 条码 IP snmpOID 参数名 Measure Actual Point K [Result]
        /// </summary>
        private void WriteProcess4SaveLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            try
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "Process4");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "CalibrationSave.log");
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>
        /// 将工序4保存记录整理写入 MES上传汇总.xlsx
        /// </summary>
        private void AppendProcess4SaveToMesWorkbook(
            string barcode,
            string ip,
            string snmpOid,
            string itemName,
            string measure,
            string actual,
            string point,
            string k,
            string result,
            string setReply)
        {
            string detailLine =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {barcode} {ip} {snmpOid} {itemName} " +
                $"Measure={measure} Actual={actual} Point={point} K={k} Result={result}" +
                (string.IsNullOrWhiteSpace(setReply) ? string.Empty : " " + setReply);

            lock (_process4LogLock)
            {
                if (string.Equals(result, "PASS", StringComparison.OrdinalIgnoreCase))
                    _process4SaveOkCount++;
                else
                    _process4SaveFailCount++;
                _process4SaveDetails.Add(detailLine);
            }

            try
            {
                // 允许在后台线程调用，避免 Excel 独占写卡死 UI
                var header = TryBuildMesHeaderForWorkbook(barcode, result);
                string workbookPath = MesUploadWorkbookWriter.SaveCalibrationRecord(
                    barcode, ip, snmpOid, itemName, measure, actual, point, k, result, setReply,
                    _mesCheckEnabled, header);
                AppendLog(3, "已写入MES上传汇总: " + workbookPath);
            }
            catch (Exception ex)
            {
                AppendLog(3, "写入MES上传汇总失败: " + ex.Message);
            }
        }

        // 在UI上显示安规测试数据：row为0-4，col为0或1
        private void SetSafetyMeasure(int row, int col, string value)
        {
            if (InvokeRequired) { BeginInvoke(new Action<int, int, string>(SetSafetyMeasure), row, col, value); return; }
            if (row >= 0 && row < 5 && (col == 0 || col == 1) && _txtSafetyMeasure[row, col] != null)
                _txtSafetyMeasure[row, col].Text = value;
        }

        private void SetResult(int idx, string text, Color color)
        {
            if (_lblResult == null || idx < 0 || idx >= _lblResult.Length || _lblResult[idx] == null)
                return;

            // 工序1~3顶栏不显示结果徽章，仅工序4可见；后台仍可更新文字
            _lblResult[idx].Text      = text;
            _lblResult[idx].ForeColor = color;
            _lblResult[idx].BackColor = GetSoftStatusBackColor(color);
        }

        private void SetSafetyStatus(bool ok, string msg)
        {
            _lblSafetyStatus.Text      = msg;
            _lblSafetyStatus.ForeColor = ok ? UiTheme.SuccessGreen : UiTheme.DisconnectedRed;
        }

        private void SetMcStatus(bool ok, string msg)
        {
            _lblMcStatus.Text      = msg;
            _lblMcStatus.ForeColor = ok ? UiTheme.SuccessGreen : UiTheme.DisconnectedRed;
        }

        private void SetMesStatus(bool ok, string msg)
        {
            _lblMesStatus.Text      = msg;
            _lblMesStatus.ForeColor = ok ? UiTheme.SuccessGreen : UiTheme.DisconnectedRed;
        }

        private static void ApplyButtonStyle(Button button, Color baseColor)
        {
            if (button == null)
                return;

            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(baseColor, 0.1f);
            button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(baseColor, 0.1f);
        }

        private static Color GetSoftStatusBackColor(Color statusColor)
        {
            return Color.FromArgb(
                245,
                (statusColor.R + 255) / 2,
                (statusColor.G + 255) / 2,
                (statusColor.B + 255) / 2);
        }

        private static void CenterAlarmIndicator(Control indicator, Control host)
        {
            if (indicator == null || host == null)
                return;

            indicator.Left = Math.Max(0, (host.ClientSize.Width - indicator.Width) / 2);
            indicator.Top = Math.Max(0, (host.ClientSize.Height - indicator.Height) / 2);
        }

        private static void ApplyAlarmIndicatorStyle(Control indicator, Color color)
        {
            if (indicator == null)
                return;

            indicator.BackColor = color;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(0, 0, Math.Max(1, indicator.Width - 1), Math.Max(1, indicator.Height - 1));
                indicator.Region = new Region(path);
            }
        }

        private void UpdateAlarmIndicator(Label[] indicators, int index, string value)
        {
            if (indicators == null || index < 0 || index >= indicators.Length)
                return;

            UpdateAlarmIndicator(indicators[index], value);
        }

        private void UpdateAlarmIndicator(Label indicator, string value)
        {
            if (indicator == null)
                return;

            if (string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                ApplyAlarmIndicatorStyle(indicator, Color.Gray);
                return;
            }

            bool alarmActive = false;
            bool.TryParse(value, out alarmActive);
            ApplyAlarmIndicatorStyle(indicator, alarmActive ? Color.OrangeRed : Color.FromArgb(34, 139, 34));
        }

        private void UpdateDoControlState(int index, string value)
        {
            if (index < 0 || index >= _doOpenStates.Length)
                return;

            bool isOpen = false;
            bool.TryParse(value, out isOpen);
            _doOpenStates[index] = isOpen;

            if (_lblDoStates[index] != null)
            {
                _lblDoStates[index].Text = isOpen ? "当前: 常开" : "当前: 常闭";
                _lblDoStates[index].ForeColor = isOpen ? Color.OrangeRed : Color.DimGray;
            }

            if (_btnDoToggle[index] != null)
                _btnDoToggle[index].Text = isOpen ? "切常闭" : "切常开";

            UpdateProcess3ControlButtonsState();
        }

        private void UpdateLvdControlState(int index, string value)
        {
            if (index < 0 || index >= _lvdOffStates.Length)
                return;

            bool isOff = false;
            bool.TryParse(value, out isOff);
            _lvdOffStates[index] = isOff;

            if (_lblLvdStates[index] != null)
            {
                _lblLvdStates[index].Text = isOff ? "当前: OFF" : "当前: ON";
                _lblLvdStates[index].ForeColor = isOff ? Color.OrangeRed : Color.DimGray;
            }

            if (_btnLvdToggle[index] != null)
                _btnLvdToggle[index].Text = isOff ? "切ON" : "切OFF";

            UpdateProcess3ControlButtonsState();
        }

        private void UpdateBlvdControlState(string value)
        {
            bool isOff = false;
            bool.TryParse(value, out isOff);
            _blvdOffState = isOff;

            if (_lblBlvdState != null)
            {
                _lblBlvdState.Text = isOff ? "当前: OFF" : "当前: ON";
                _lblBlvdState.ForeColor = isOff ? Color.OrangeRed : Color.DimGray;
            }

            if (_btnBlvdToggle != null)
                _btnBlvdToggle.Text = isOff ? "切ON" : "切OFF";

            UpdateProcess3ControlButtonsState();
        }

        private void UpdateProcess3ControlButtonsState()
        {
            bool canOperate =
                _currentRunningProcess == 2 &&
                !_stopRequested &&
                _mc2900 != null &&
                _mc2900Bus != null &&
                _mc2900Bus.IsOpen &&
                _mc2900Bus.Enabled &&
                !_mc2900Bus.Paused &&
                Interlocked.CompareExchange(ref _process3ControlRunning, 0, 0) == 0;

            for (int i = 0; i < _btnDoToggle.Length; i++)
            {
                if (_btnDoToggle[i] != null)
                    _btnDoToggle[i].Enabled = canOperate;
            }

            if (_btnDoBatchOpen != null)
                _btnDoBatchOpen.Enabled = canOperate;

            if (_btnDoBatchClose != null)
                _btnDoBatchClose.Enabled = canOperate;

            for (int i = 0; i < _btnLvdToggle.Length; i++)
            {
                if (_btnLvdToggle[i] != null)
                    _btnLvdToggle[i].Enabled = canOperate;
            }

            if (_btnBlvdToggle != null)
                _btnBlvdToggle.Enabled = canOperate;
        }

        private GroupBox BuildNamedAlarmGroup(string groupTitle, string[] names, Label[] indicatorStore)
        {
            if (names == null || indicatorStore == null || names.Length != indicatorStore.Length)
                throw new ArgumentException("告警组名称与指示灯数组长度不一致。");

            int columnCount = Math.Min(4, Math.Max(1, names.Length));
            int rowCount = Math.Max(1, (names.Length + columnCount - 1) / columnCount);

            var group = new GroupBox
            {
                Text = groupTitle,
                Dock = DockStyle.Top,
                Height = rowCount == 1 ? 150 : 220,
                Font = new Font("微软雅黑", 10f, FontStyle.Bold),
                Padding = new Padding(8),
                Margin = new Padding(0, 10, 0, 0),
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = columnCount,
                RowCount = rowCount,
                Padding = rowCount == 1 ? new Padding(8, 4, 8, 16) : new Padding(8, 6, 8, 12),
            };

            for (int col = 0; col < columnCount; col++)
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columnCount));

            for (int row = 0; row < rowCount; row++)
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rowCount));

            for (int i = 0; i < names.Length; i++)
            {
                var itemPanel = new Panel
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(8, 4, 8, 4),
                };

                var title = new Label
                {
                    Text = names[i],
                    Dock = DockStyle.Top,
                    Height = rowCount == 1 ? 18 : 20,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("微软雅黑", 9f, FontStyle.Bold),
                };

                var indicator = new Label
                {
                    Text = string.Empty,
                    Size = new Size(54, 54),
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = Color.FromArgb(34, 139, 34),
                    BorderStyle = BorderStyle.FixedSingle,
                };
                indicator.SizeChanged += (s, e) => ApplyAlarmIndicatorStyle(indicator, indicator.BackColor);
                ApplyAlarmIndicatorStyle(indicator, Color.FromArgb(34, 139, 34));
                indicatorStore[i] = indicator;

                var host = new Panel
                {
                    Dock = DockStyle.Fill,
                    Padding = rowCount == 1 ? new Padding(0, 8, 0, 10) : new Padding(0, 12, 0, 4),
                };
                host.Controls.Add(indicator);
                host.Resize += (s, e) => CenterAlarmIndicator(indicator, host);
                CenterAlarmIndicator(indicator, host);

                itemPanel.Controls.Add(host);
                itemPanel.Controls.Add(title);
                layout.Controls.Add(itemPanel, i % columnCount, i / columnCount);
            }

            group.Controls.Add(layout);
            return group;
        }

        private GroupBox BuildIoAlarmGroup(string groupTitle, string indicatorPrefix, Label[] indicatorStore)
        {
            int columnCount = 4;
            int rowCount = Math.Max(1, (indicatorStore.Length + columnCount - 1) / columnCount);

            var group = new GroupBox
            {
                Text = groupTitle,
                Dock = DockStyle.Top,
                Height = rowCount == 1 ? 150 : 220,
                Font = new Font("微软雅黑", 10f, FontStyle.Bold),
                Padding = new Padding(8),
                Margin = new Padding(0, 10, 0, 0),
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = columnCount,
                RowCount = rowCount,
                Padding = rowCount == 1 ? new Padding(8, 4, 8, 16) : new Padding(8, 6, 8, 12),
            };

            for (int col = 0; col < columnCount; col++)
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columnCount));

            for (int row = 0; row < rowCount; row++)
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rowCount));

            for (int i = 0; i < indicatorStore.Length; i++)
            {
                var itemPanel = new Panel
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(8, 4, 8, 4),
                };

                var title = new Label
                {
                    Text = indicatorPrefix + (i + 1).ToString(),
                    Dock = DockStyle.Top,
                    Height = rowCount == 1 ? 18 : 20,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("微软雅黑", 10f, FontStyle.Bold),
                };

                var indicator = new Label
                {
                    Text = string.Empty,
                    Size = new Size(54, 54),
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = Color.FromArgb(34, 139, 34),
                    BorderStyle = BorderStyle.FixedSingle,
                };
                indicator.SizeChanged += (s, e) => ApplyAlarmIndicatorStyle(indicator, indicator.BackColor);
                ApplyAlarmIndicatorStyle(indicator, Color.FromArgb(34, 139, 34));
                indicatorStore[i] = indicator;

                var host = new Panel
                {
                    Dock = DockStyle.Fill,
                    Padding = rowCount == 1 ? new Padding(0, 8, 0, 10) : new Padding(0, 12, 0, 4),
                };
                host.Controls.Add(indicator);
                host.Resize += (s, e) => CenterAlarmIndicator(indicator, host);
                CenterAlarmIndicator(indicator, host);

                itemPanel.Controls.Add(host);
                itemPanel.Controls.Add(title);
                layout.Controls.Add(itemPanel, i % columnCount, i / columnCount);
            }

            group.Controls.Add(layout);
            return group;
        }

        private GroupBox BuildDoAlarmGroup()
        {
            var group = new GroupBox
            {
                Text = string.Empty,
                Dock = DockStyle.Top,
                Height = 300,
                Font = new Font("微软雅黑", 10f, FontStyle.Bold),
                Padding = new Padding(8),
                Margin = new Padding(0, 10, 0, 0),
            };

            var headerPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 34,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = false,
                Padding = new Padding(0, 2, 0, 2),
                Margin = new Padding(0),
            };

            var titleLabel = new Label
            {
                Text = "DO口告警测试:",
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("微软雅黑", 10f, FontStyle.Bold),
                Padding = new Padding(4, 0, 0, 0),
                Margin = new Padding(0, 4, 0, 0),
            };

            var buttonHost = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0),
                Margin = new Padding(8, 0, 0, 0),
            };

            headerPanel.Controls.Add(titleLabel);
            headerPanel.Controls.Add(buttonHost);

            _btnDoBatchOpen = new Button
            {
                Text = "全部常开",
                Width = 90,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                Enabled = false,
                Margin = new Padding(4, 0, 0, 0),
            };
            _btnDoBatchOpen.Click += (s, e) => OnDoBatchControlButtonClick(true);

            _btnDoBatchClose = new Button
            {
                Text = "全部常闭",
                Width = 90,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(196, 43, 28),
                ForeColor = Color.White,
                Enabled = false,
                Margin = new Padding(4, 0, 0, 0),
            };
            _btnDoBatchClose.Click += (s, e) => OnDoBatchControlButtonClick(false);

            buttonHost.Controls.Add(_btnDoBatchOpen);
            buttonHost.Controls.Add(_btnDoBatchClose);
            group.Controls.Add(headerPanel);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 2,
                Padding = new Padding(8, 6, 8, 12),
            };

            for (int col = 0; col < 4; col++)
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

            for (int row = 0; row < 2; row++)
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            for (int i = 0; i < 8; i++)
            {
                int doIndex = i;
                var itemPanel = new Panel
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(8, 4, 8, 4),
                };

                var title = new Label
                {
                    Text = "DO" + (i + 1).ToString(),
                    Dock = DockStyle.Top,
                    Height = 20,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("微软雅黑", 10f, FontStyle.Bold),
                };

                var stateLabel = new Label
                {
                    Text = "当前: 常闭",
                    Dock = DockStyle.Bottom,
                    Height = 18,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("微软雅黑", 8.5f),
                    ForeColor = Color.DimGray,
                };
                _lblDoStates[i] = stateLabel;

                var actionButton = new Button
                {
                    Text = "切常开",
                    Dock = DockStyle.Bottom,
                    Height = 28,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(0, 120, 215),
                    ForeColor = Color.White,
                    Enabled = false,
                };
                actionButton.Click += (s, e) => OnDoControlButtonClick(doIndex);
                _btnDoToggle[i] = actionButton;

                var indicator = new Label
                {
                    Text = string.Empty,
                    Size = new Size(54, 54),
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = Color.FromArgb(34, 139, 34),
                    BorderStyle = BorderStyle.FixedSingle,
                };
                indicator.SizeChanged += (s, e) => ApplyAlarmIndicatorStyle(indicator, indicator.BackColor);
                ApplyAlarmIndicatorStyle(indicator, Color.FromArgb(34, 139, 34));
                _lblDoAlarms[i] = indicator;

                var host = new Panel
                {
                    Dock = DockStyle.Fill,
                    Padding = new Padding(0, 6, 0, 6),
                };
                host.Controls.Add(indicator);
                host.Resize += (s, e) => CenterAlarmIndicator(indicator, host);
                CenterAlarmIndicator(indicator, host);

                itemPanel.Controls.Add(host);
                itemPanel.Controls.Add(actionButton);
                itemPanel.Controls.Add(stateLabel);
                itemPanel.Controls.Add(title);
                layout.Controls.Add(itemPanel, i % 4, i / 4);
            }

            group.Controls.Add(headerPanel);
            group.Controls.Add(layout);
            return group;
        }

        private GroupBox BuildLvdAlarmGroup()
        {
            var group = new GroupBox
            {
                Text = "下电（LLVD）支路告警测试",
                Dock = DockStyle.Top,
                Height = 235,
                Font = new Font("微软雅黑", 10f, FontStyle.Bold),
                Padding = new Padding(8),
                Margin = new Padding(0, 10, 0, 0),
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 1,
                Padding = new Padding(8, 8, 8, 16),
            };

            for (int col = 0; col < 5; col++)
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));

            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            for (int i = 0; i < _lblLvdAlarms.Length; i++)
            {
                var itemPanel = new Panel
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(4, 4, 4, 4),
                };

                var title = new Label
                {
                    Text = "LVD" + (i + 1).ToString(),
                    Dock = DockStyle.Top,
                    Height = 18,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("微软雅黑", 10f, FontStyle.Bold),
                };

                var indicator = new Label
                {
                    Text = string.Empty,
                    Size = new Size(54, 54),
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = Color.FromArgb(34, 139, 34),
                    BorderStyle = BorderStyle.FixedSingle,
                };
                indicator.SizeChanged += (s, e) => ApplyAlarmIndicatorStyle(indicator, indicator.BackColor);
                ApplyAlarmIndicatorStyle(indicator, Color.FromArgb(34, 139, 34));
                _lblLvdAlarms[i] = indicator;

                var stateLabel = new Label
                {
                    Text = "当前: --",
                    Dock = DockStyle.Bottom,
                    Height = 18,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("微软雅黑", 8.5f),
                    ForeColor = Color.DimGray,
                };
                _lblLvdStates[i] = stateLabel;

                var lvdIndex = i;
                var actionButton = new Button
                {
                    Text = "切OFF",
                    Dock = DockStyle.Bottom,
                    Height = 28,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(0, 120, 215),
                    ForeColor = Color.White,
                    Enabled = false,
                };
                actionButton.Click += (s, e) => OnLvdControlButtonClick(lvdIndex);
                _btnLvdToggle[i] = actionButton;

                var host = new Panel
                {
                    Dock = DockStyle.Fill,
                    Padding = new Padding(0, 8, 0, 6),
                };
                host.Controls.Add(indicator);
                host.Resize += (s, e) => CenterAlarmIndicator(indicator, host);
                CenterAlarmIndicator(indicator, host);

                itemPanel.Controls.Add(host);
                itemPanel.Controls.Add(actionButton);
                itemPanel.Controls.Add(stateLabel);
                itemPanel.Controls.Add(title);
                layout.Controls.Add(itemPanel, i, 0);
            }

            var blvoPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(4, 4, 4, 4),
            };

            var blvoTitle = new Label
            {
                Text = "BLVO",
                Dock = DockStyle.Top,
                Height = 18,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("微软雅黑", 10f, FontStyle.Bold),
            };

            _lblBlvoAlarm = new Label
            {
                Text = string.Empty,
                Size = new Size(54, 54),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(34, 139, 34),
                BorderStyle = BorderStyle.FixedSingle,
            };
            _lblBlvoAlarm.SizeChanged += (s, e) => ApplyAlarmIndicatorStyle(_lblBlvoAlarm, _lblBlvoAlarm.BackColor);
            ApplyAlarmIndicatorStyle(_lblBlvoAlarm, Color.FromArgb(34, 139, 34));

            _lblBlvdState = new Label
            {
                Text = "当前: --",
                Dock = DockStyle.Bottom,
                Height = 18,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("微软雅黑", 8.5f),
                ForeColor = Color.DimGray,
            };

            _btnBlvdToggle = new Button
            {
                Text = "切OFF",
                Dock = DockStyle.Bottom,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                Enabled = false,
            };
            _btnBlvdToggle.Click += (s, e) => OnBlvdControlButtonClick();

            var blvoHost = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 8, 0, 6),
            };
            blvoHost.Controls.Add(_lblBlvoAlarm);
            blvoHost.Resize += (s, e) => CenterAlarmIndicator(_lblBlvoAlarm, blvoHost);
            CenterAlarmIndicator(_lblBlvoAlarm, blvoHost);

            blvoPanel.Controls.Add(blvoHost);
            blvoPanel.Controls.Add(_btnBlvdToggle);
            blvoPanel.Controls.Add(_lblBlvdState);
            blvoPanel.Controls.Add(blvoTitle);
            layout.Controls.Add(blvoPanel, 4, 0);

            var blvoHeader = new Label
            {
                Text = "电池下电(BLVD)告警测试",
                AutoSize = true,
                BackColor = group.BackColor,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("微软雅黑", 10f, FontStyle.Bold),
                Padding = new Padding(4, 0, 4, 0),
            };
            blvoHeader.Location = new Point(Math.Max(8, group.Width - blvoHeader.PreferredWidth - 18), 0);
            group.Resize += (s, e) =>
            {
                blvoHeader.Location = new Point(Math.Max(8, group.Width - blvoHeader.PreferredWidth - 18), 0);
            };

            group.Controls.Add(blvoHeader);
            group.Controls.Add(layout);
            blvoHeader.BringToFront();
            return group;
        }

        private async void OnDoControlButtonClick(int doIndex)
        {
            if (_currentRunningProcess != 2)
            {
                AppendLog(2, "请先启动工序3告警测试");
                return;
            }

            if (_mc2900 == null || !EnsureMcBusReady("工序3 DO控制", true))
            {
                AppendLog(2, "MC2900通信未就绪，无法执行DO控制");
                return;
            }

            if (Interlocked.Exchange(ref _process3ControlRunning, 1) == 1)
            {
                AppendLog(2, "工序3控制命令执行中，请稍后重试");
                return;
            }

            UpdateProcess3ControlButtonsState();

            try
            {
                bool nextOpen = !_doOpenStates[doIndex];
                AppendLog(2, $"开始DO{doIndex + 1}手动控制");
                long startSequence = ModbusDataLogger.GetLatestSequence();
                using (_mc2900.SuspendQueryPolling())
                {
                    await Task.Run(() => _mc2900.SetDO(doIndex + 1, nextOpen));
                }
                await RefreshProcess3ControlStatesAsync();

                string frameText = ModbusDataLogger.FramesToText(ModbusDataLogger.GetFramesAfter(startSequence, ModbusDataLogger.PortType.MC2900));
                if (_processes != null && _processes.Length > 2)
                {
                    string status = $"目标状态={(nextOpen ? "常开" : "常闭")} Result=成功";
                    _processes[2].AppendRuntimeRawRecord(60 + doIndex,
                        $"工序3-DO{doIndex + 1}手动控制原始记录",
                        $"{status}{Environment.NewLine}{frameText}");
                    _processes[2].AppendAlarmCaptureToMes(
                        $"DO{doIndex + 1}切换",
                        status,
                        frameText);
                }

                AppendLog(2, $"DO{doIndex + 1}已切换为{(nextOpen ? "常开" : "常闭")}");
            }
            catch (Exception ex)
            {
                AppendLog(2, $"DO{doIndex + 1}控制失败: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _process3ControlRunning, 0);
                UpdateProcess3ControlButtonsState();
            }
        }

        private async void OnDoBatchControlButtonClick(bool openAll)
        {
            if (_currentRunningProcess != 2)
            {
                AppendLog(2, "请先启动工序3告警测试");
                return;
            }

            if (_mc2900 == null || !EnsureMcBusReady("工序3 DO批量控制", true))
            {
                AppendLog(2, "MC2900通信未就绪，无法执行DO批量控制");
                return;
            }

            if (Interlocked.Exchange(ref _process3ControlRunning, 1) == 1)
            {
                AppendLog(2, "工序3控制命令执行中，请稍后重试");
                return;
            }

            UpdateProcess3ControlButtonsState();

            try
            {
                AppendLog(2, openAll ? "开始DO1-DO8全部常开控制" : "开始DO1-DO8全部常闭控制");
                long startSequence = ModbusDataLogger.GetLatestSequence();
                using (_mc2900.SuspendQueryPolling())
                {
                    for (int i = 0; i < 8; i++)
                    {
                        await Task.Run(() => _mc2900.SetDO(i + 1, openAll));
                        _doOpenStates[i] = openAll;
                    }
                }

                await RefreshProcess3ControlStatesAsync();

                string frameText = ModbusDataLogger.FramesToText(ModbusDataLogger.GetFramesAfter(startSequence, ModbusDataLogger.PortType.MC2900));
                if (_processes != null && _processes.Length > 2)
                {
                    string status = $"目标状态={(openAll ? "常开" : "常闭")} Result=成功";
                    _processes[2].AppendRuntimeRawRecord(openAll ? 73 : 74,
                        $"工序3-DO批量控制原始记录",
                        $"{status}{Environment.NewLine}{frameText}");
                    _processes[2].AppendAlarmCaptureToMes(
                        openAll ? "DO1-DO8全部常开" : "DO1-DO8全部常闭",
                        status,
                        frameText);
                }

                AppendLog(2, openAll ? "DO1-DO8已全部切换为常开" : "DO1-DO8已全部切换为常闭");
            }
            catch (Exception ex)
            {
                AppendLog(2, "DO批量控制失败: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _process3ControlRunning, 0);
                UpdateProcess3ControlButtonsState();
            }
        }

        private async void OnLvdControlButtonClick(int lvdIndex)
        {
            if (_currentRunningProcess != 2)
            {
                AppendLog(2, "请先启动工序3告警测试");
                return;
            }

            if (lvdIndex < 0 || lvdIndex >= _lvdOffStates.Length)
                return;

            if (_mc2900 == null || !EnsureMcBusReady($"工序3 LVD{lvdIndex + 1}控制", true))
            {
                AppendLog(2, $"MC2900通信未就绪，无法执行LVD{lvdIndex + 1}控制");
                return;
            }

            if (Interlocked.Exchange(ref _process3ControlRunning, 1) == 1)
            {
                AppendLog(2, "工序3控制命令执行中，请稍后重试");
                return;
            }

            UpdateProcess3ControlButtonsState();

            try
            {
                bool nextPowerOn = _lvdOffStates[lvdIndex];
                AppendLog(2, $"开始LVD{lvdIndex + 1}控制");
                long startSequence = ModbusDataLogger.GetLatestSequence();
                using (_mc2900.SuspendQueryPolling())
                {
                    await Task.Run(() => _mc2900.SetLVDPowerState(lvdIndex + 1, nextPowerOn));
                }
                await RefreshProcess3ControlStatesAsync();

                string frameText = ModbusDataLogger.FramesToText(ModbusDataLogger.GetFramesAfter(startSequence, ModbusDataLogger.PortType.MC2900));
                if (_processes != null && _processes.Length > 2)
                {
                    _processes[2].AppendRuntimeRawRecord(68 + lvdIndex,
                        $"工序3-LVD{lvdIndex + 1}手动控制原始记录",
                        $"目标状态={(nextPowerOn ? "ON" : "OFF")}{Environment.NewLine}{frameText}");
                }

                AppendLog(2, $"LVD{lvdIndex + 1}已切换为{(nextPowerOn ? "ON" : "OFF")}");
            }
            catch (Exception ex)
            {
                AppendLog(2, $"LVD{lvdIndex + 1}控制失败: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _process3ControlRunning, 0);
                UpdateProcess3ControlButtonsState();
            }
        }

        private async void OnBlvdControlButtonClick()
        {
            if (_currentRunningProcess != 2)
            {
                AppendLog(2, "请先启动工序3告警测试");
                return;
            }

            if (_mc2900 == null || !EnsureMcBusReady("工序3 BLVD控制", true))
            {
                AppendLog(2, "MC2900通信未就绪，无法执行BLVD控制");
                return;
            }

            if (Interlocked.Exchange(ref _process3ControlRunning, 1) == 1)
            {
                AppendLog(2, "工序3控制命令执行中，请稍后重试");
                return;
            }

            UpdateProcess3ControlButtonsState();

            try
            {
                bool nextPowerOn = _blvdOffState;
                AppendLog(2, "开始BLVD控制");
                long startSequence = ModbusDataLogger.GetLatestSequence();
                using (_mc2900.SuspendQueryPolling())
                {
                    await Task.Run(() => _mc2900.SetBLVDPowerState(nextPowerOn));
                }
                await RefreshProcess3ControlStatesAsync();

                string frameText = ModbusDataLogger.FramesToText(ModbusDataLogger.GetFramesAfter(startSequence, ModbusDataLogger.PortType.MC2900));
                if (_processes != null && _processes.Length > 2)
                {
                    _processes[2].AppendRuntimeRawRecord(72,
                        "工序3-BLVD手动控制原始记录",
                        $"目标状态={(nextPowerOn ? "ON" : "OFF")}{Environment.NewLine}{frameText}");
                }

                AppendLog(2, $"BLVD已切换为{(nextPowerOn ? "ON" : "OFF")}");
            }
            catch (Exception ex)
            {
                AppendLog(2, "BLVD控制失败: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _process3ControlRunning, 0);
                UpdateProcess3ControlButtonsState();
            }
        }

        private async Task RefreshProcess3ControlStatesAsync()
        {
            if (_mc2900 == null || !EnsureMcBusReady("工序3状态刷新", true))
                return;

            try
            {
                bool[] doStates;
                bool[] lvdBlvdStates;
                using (_mc2900.SuspendQueryPolling())
                {
                    doStates = await Task.Run(() => _mc2900.ReadProcess3DoStates(allowWhenQueryPollingSuspended: true));

                    lvdBlvdStates = await Task.Run(() => _mc2900.ReadProcess3LvdBlvdAlarmCoils(allowWhenQueryPollingSuspended: true));
                }

                for (int i = 0; i < doStates.Length; i++)
                    UpdateDoControlState(i, doStates[i].ToString());

                for (int i = 0; i < 4; i++)
                {
                    bool isOff = lvdBlvdStates != null && lvdBlvdStates.Length > i && lvdBlvdStates[i];
                    UpdateLvdControlState(i, isOff.ToString());
                }

                bool blvdIsOff = lvdBlvdStates != null && lvdBlvdStates.Length > 4 && lvdBlvdStates[4];
                UpdateBlvdControlState(blvdIsOff.ToString());
            }
            catch (Exception ex)
            {
                AppendLog(2, "刷新DO/LLVD1/BLVD当前状态失败: " + ex.Message);
            }
        }

        private void RefreshPorts()
        {
            string[] ports = SerialPort.GetPortNames();
            Array.Sort(ports);
            
            // 保存当前选择
            string safetySelected = _cmbSafetyPort.SelectedItem as string;
            string mcSelected = _cmbMcPort.SelectedItem as string;
            
            // 检查是否有新端口
            bool hasNewPort = false;
            foreach (var p in ports)
            {
                if (!_cmbSafetyPort.Items.Contains(p))
                {
                    hasNewPort = true;
                    break;
                }
            }
            
            // 检查是否有端口被移除
            bool hasRemovedPort = false;
            for (int i = _cmbSafetyPort.Items.Count - 1; i >= 0; i--)
            {
                string item = _cmbSafetyPort.Items[i] as string;
                if (Array.IndexOf(ports, item) < 0)
                {
                    hasRemovedPort = true;
                    break;
                }
            }
            
            // 如果有变化才更新
            if (hasNewPort || hasRemovedPort)
            {
                _cmbSafetyPort.Items.Clear();
                _cmbMcPort.Items.Clear();
                
                foreach (var p in ports)
                {
                    _cmbSafetyPort.Items.Add(p);
                    _cmbMcPort.Items.Add(p);
                }
                
                // 恢复选择，如果之前的端口仍然存在
                if (!string.IsNullOrEmpty(safetySelected) && _cmbSafetyPort.Items.Contains(safetySelected))
                {
                    _cmbSafetyPort.SelectedItem = safetySelected;
                }
                else if (_cmbSafetyPort.Items.Count > 0)
                {
                    _cmbSafetyPort.SelectedIndex = 0;
                }
                
                if (!string.IsNullOrEmpty(mcSelected) && _cmbMcPort.Items.Contains(mcSelected))
                {
                    _cmbMcPort.SelectedItem = mcSelected;
                }
                else if (_cmbMcPort.Items.Count > 0)
                {
                    _cmbMcPort.SelectedIndex = 0;
                }
                
                // 记录日志
                if (hasNewPort)
                {
                    ModbusDataLogger.LogInfo($"发现新串口: {string.Join(", ", ports)}");
                }
            }
        }

        // ── 控件工厂 ──────────────────────────────────────────────
        private static GroupBox MakeGroupBox(string text, int x, int y, int w, int h, Color fore)
        {
            return new GroupBox
            {
                Text      = text,
                Location  = new Point(x, y),
                Size      = new Size(w, h),
                ForeColor = fore,
                Font      = new Font("微软雅黑", 9f),
            };
        }

        private static ComboBox MakeComboBox(int x, int y, int w)
        {
            return new ComboBox
            {
                Location        = new Point(x, y),
                Size            = new Size(w, 24),
                DropDownStyle   = ComboBoxStyle.DropDownList,
            };
        }

        private static Button MakeButton(string text, int x, int y, int w, Color back)
        {
            return new Button
            {
                Text      = text,
                Location  = new Point(x, y),
                Size      = new Size(w, 30),
                BackColor = back,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("微软雅黑", 10f),
            };
        }

        private static Label MakeLabel(String text, int x, int y, int w, Color fore)
        {
            return new Label
            {
                Text      = text,
                Location  = new Point(x, y),
                Size      = new Size(w, 20),
                ForeColor = fore,
                Font      = new Font("微软雅黑", 9f),
            };
        }

        private static TextBox MakeTextBox(int width = 100)
        {
            return new TextBox
            {
                Font = new Font("Consolas", 11f),
                Size = new Size(width, 30),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.WhiteSmoke,
                TextAlign = HorizontalAlignment.Center,
            };
        }

        // ── 缺失的方法实现 ─────────────────────────────────────────

        /// <summary>
        /// 显示Modbus监控窗口
        /// </summary>
        private void ShowModbusMonitor()
        {
            if (_modbusMonitor == null || _modbusMonitor.IsDisposed)
            {
                _modbusMonitor = new ModbusMonitorForm();
            }
            
            if (_modbusMonitor.Visible)
            {
                _modbusMonitor.Activate();
            }
            else
            {
                _modbusMonitor.Show(this);
            }
            
            ModbusDataLogger.LogInfo("Modbus监控窗口已打开");
        }

        /// <summary>
        /// 显示串口调试窗口
        /// </summary>
        private void ShowSerialDebug()
        {
            // 此方法不再用于打开独立窗口。串口调试已直接嵌入工序2页面。
            ModbusDataLogger.LogInfo("串口调试已嵌入工序2页面");
        }

        // ----------------- 嵌入式南向串口逻辑 -----------------
        private void RefreshSouthPortList()
        {
            try
            {
                var names = SerialPort.GetPortNames();
                Array.Sort(names);
                var prev = _cmbSouthPorts?.SelectedItem as string;
                _cmbSouthPorts.Items.Clear();
                _cmbSouthPorts.Items.AddRange(names);
                if (!string.IsNullOrEmpty(prev) && _cmbSouthPorts.Items.Contains(prev))
                    _cmbSouthPorts.SelectedItem = prev;
                else if (_cmbSouthPorts.Items.Count > 0)
                    _cmbSouthPorts.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("刷新串口列表失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnSouthConnect_Click(object sender, EventArgs e)
        {
            if (_southSerialPort != null && _southSerialPort.IsOpen)
            {
                try { _southSerialPort.DataReceived -= SouthPort_DataReceived; } catch { }
                try { _southSerialPort.Close(); } catch { }
                try { _southSerialPort.Dispose(); } catch { }
                _southSerialPort = null;
                lock (_southPendingTxLock) { _southPendingTxQueue.Clear(); }
                SetSouthConnected(false);
                return;
            }

            if (_cmbSouthPorts.SelectedItem == null)
            {
                MessageBox.Show("请先选择串口", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                _southSerialPort = new SerialPort((string)_cmbSouthPorts.SelectedItem, int.Parse((string)_cmbSouthBaud.SelectedItem ?? _cmbSouthBaud.Text, System.Globalization.CultureInfo.InvariantCulture));
                _southSerialPort.Encoding = System.Text.Encoding.UTF8;
                _southSerialPort.ReadBufferSize = 4096;  // 拃插接暥缓冲区
                _southSerialPort.Open();
                System.Threading.Thread.Sleep(100);  // 串口打开后等待稳定
                _southSerialPort.DiscardInBuffer();   // 清空接收串口旧数据
                _southSerialPort.DiscardOutBuffer();  // 清空发送串口旧数据
                _southSerialPort.DataReceived += SouthPort_DataReceived;
                lock (_southPendingTxLock) { _southPendingTxQueue.Clear(); }
                SetSouthConnected(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开串口失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                try { _southSerialPort?.Dispose(); } catch { }
                _southSerialPort = null;
                lock (_southPendingTxLock) { _southPendingTxQueue.Clear(); }
                SetSouthConnected(false);
            }
        }

        private void SetSouthConnected(bool connected)
        {
            if (connected)
            {
                _lblSouthSerialStatus.Text = "已连接";
                _lblSouthSerialStatus.ForeColor = UiTheme.ConnectedGreen;
                _btnSouthConnect.Text = "断开";
                _btnSouthConnect.BackColor = UiTheme.DangerRed;
            }
            else
            {
                _lblSouthSerialStatus.Text = "未连接";
                _lblSouthSerialStatus.ForeColor = UiTheme.DisconnectedRed;
                _btnSouthConnect.Text = "连接";
                _btnSouthConnect.BackColor = UiTheme.PrimaryBlue;
            }
        }

        private async void BeginSouthResponseTimeoutWatch(PendingSouthTx pendingTx)
        {
            if (pendingTx == null)
                return;

            await Task.Delay(SouthResponseTimeoutMs).ConfigureAwait(false);

            bool stillPending = false;
            lock (_southPendingTxLock)
            {
                foreach (var queued in _southPendingTxQueue)
                {
                    if (ReferenceEquals(queued, pendingTx))
                    {
                        stillPending = true;
                        break;
                    }
                }
            }

            if (!stillPending)
                return;

            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (_southSerialPort == null || !_southSerialPort.IsOpen)
                        return;

                    _lblSouthSerialStatus.Text = $"已连接(等待回包超时>{SouthResponseTimeoutMs}ms)";
                    _lblSouthSerialStatus.ForeColor = Color.OrangeRed;
                }));
            }
            catch { }
        }

        private void BtnSouthSend_Click(object sender, EventArgs e)
        {
            if (_southSerialPort == null || !_southSerialPort.IsOpen)
            {
                MessageBox.Show("串口未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                string text = _txtSouthSend.Text ?? string.Empty;
                byte[] data = ParseHexBytes(text);
                _southSerialPort.DiscardInBuffer();   // 发送前清空接收缓冲，不扨操旧数据
                _southSerialPort.Write(data, 0, data.Length);
                string txHex = FormatHex(data);
                AppendSouthLog($"TX[HEX]: {txHex}");
                // 仅收到回包后才记录到测试日志/MES汇总
                PendingSouthTx pendingTx;
                lock (_southPendingTxLock)
                {
                    pendingTx = new PendingSouthTx(txHex);
                    _southPendingTxQueue.Enqueue(pendingTx);
                }
                BeginSouthResponseTimeoutWatch(pendingTx);
            }
            catch (Exception ex)
            {
                MessageBox.Show("发送失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SouthPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                int available = _southSerialPort.BytesToRead;
                if (available <= 0) return;
                byte[] buffer = new byte[available];
                int read = _southSerialPort.Read(buffer, 0, available);
                if (read <= 0) return;

                BeginInvoke(new Action(() =>
                {
                    // 只显示十六进制，不显示 ASCII（避免二进制数据的乱码问题）
                    string rxHex = FormatHex(buffer, read);
                    AppendSouthLog($"RX[HEX]: {rxHex}");
                    // 工序2南向485：只有有回包时才写 ---原始记录--- 和 MES上传汇总
                    PendingSouthTx pendingTx = null;
                    lock (_southPendingTxLock)
                    {
                        if (_southPendingTxQueue.Count > 0)
                            pendingTx = _southPendingTxQueue.Dequeue();
                    }

                    string txHex = pendingTx?.TxHex;
                    if (!string.IsNullOrWhiteSpace(txHex))
                    {
                        AppendSouth485PacketLog("TX", txHex);
                        AppendSouth485PacketLog("RX", rxHex);
                    }

                    if (_southSerialPort != null && _southSerialPort.IsOpen)
                    {
                        _lblSouthSerialStatus.Text = "已连接";
                        _lblSouthSerialStatus.ForeColor = Color.LimeGreen;
                    }
                }));
            }
            catch { }
        }

        private static byte[] ParseHexBytes(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<byte>();

            string normalized = text.Replace("0x", "");
            normalized = normalized.Replace("0X", "");
            var sb = new StringBuilder();
            foreach (char c in normalized)
            {
                if (char.IsWhiteSpace(c) || c == ',' || c == ':' || c == ';')
                    continue;
                if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'))
                    sb.Append(c);
                else
                    throw new FormatException("数据，如 01 03 00 01");
            }

            if (sb.Length % 2 != 0)
                throw new FormatException("十六进制长度必须为偶数");

            byte[] data = new byte[sb.Length / 2];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = Convert.ToByte(sb.ToString(i * 2, 2), 16);
            }
            return data;
        }

        private static string FormatHex(byte[] data, int length = -1)
        {
            if (data == null || data.Length == 0)
                return string.Empty;
            int count = length < 0 ? data.Length : Math.Min(length, data.Length);
            return BitConverter.ToString(data, 0, count).Replace("-", " ");
        }

        private void AppendSouthLog(string text)
        {
            if (_rtbSouthLog == null || _rtbSouthLog.IsDisposed) return;
            
            // 限制最大行数，根据实际换行符计数（不受WordWrap视觉折行影响）
            const int maxLines = 1000;
            int currentLineCount = _rtbSouthLog.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.None).Length;
            if (currentLineCount >= maxLines)
            {
                // 计算需要删除的字符数
                int lineCount = 0;
                int charIndex = 0;
                foreach (char c in _rtbSouthLog.Text)
                {
                    if (c == '\n')
                        lineCount++;
                    charIndex++;
                    if (lineCount >= 100)
                        break;
                }
                if (charIndex > 0)
                {
                    _rtbSouthLog.Select(0, charIndex);
                    _rtbSouthLog.SelectedText = string.Empty;
                }
            }
            
            // 每条消息添加时间戳，一行显示完整消息
            _rtbSouthLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
            _rtbSouthLog.ScrollToCaret();
        }

        /// <summary>
        /// MES参数设置按钮点击事件
        /// </summary>
        private void BtnMesParams_Click(object sender, EventArgs e)
        {
            try
            {
                if (!_mes.IsLoaded)
                {
                    if (!_mes.Load())
                    {
                        MessageBox.Show("加载 mes.dll 失败: " + _mes.LastError, "MES", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        SetMesStatus(false, "mes.dll: " + _mes.LastError);
                        return;
                    }
                    SetMesStatus(true, "mes.dll 已加载");
                }
                _mes.GetProgram();
            }
            catch (Exception ex)
            {
                MessageBox.Show("调用 MES GetProgram 出错: " + ex.Message, "MES", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetMesStatus(false, "调用失败");
            }
        }

        // ════════════════════════════════════════════════════════
        //  测试控制 - 独立工序模式（一次只能运行一个工序）
        // ════════════════════════════════════════════════════════

        private async void StartProcess(int idx)
        {
            string barcode = _txtBarcode[idx].Text.Trim();
            if (string.IsNullOrEmpty(barcode))
            {
                MessageBox.Show("请输入或扫描条码", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 最小改动：仅工序4在 StartProcess 前做 MES 检查；
            // 工序1/2/3继续走 ProcessBase.Run() 内部校验，避免重复 CheckBarcode。
            if (idx == 3 && _mesCheckEnabled)
            {
                try
                {
                    if (!_mes.IsLoaded && !_mes.Load())
                    {
                        MessageBox.Show("MES未就绪，无法校验条码: " + _mes.LastError, "MES",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    if (!_mes.CheckBarcode(barcode))
                    {
                        MessageBox.Show("条码不可入测，流程终止。", "MES",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("MES条码校验失败: " + ex.Message, "MES",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            // 检查是否有其他工序正在运行
            if (_currentRunningProcess != -1 && _currentRunningProcess != idx)
            {
                MessageBox.Show($"工序{_currentRunningProcess + 1}正在运行中，请先停止当前工序", 
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 工序1必须安规仪已连接
            if (idx == 0 && _safety == null)
            {
                MessageBox.Show("请先连接安规测试仪", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            // 清除该工序的日志
            _rtbLog[idx]?.Clear();
            _btnStart[idx].Enabled = false;
            _btnStop[idx].Enabled  = true;
            _currentRunningProcess = idx;
            _stopRequested = false;
            ModbusUserAlert.ResetCrcAlert();
            SetResult(idx, "测试中...", Color.Orange);

            if (idx == 1)
            {
                if (_mc2900 == null || _mc2900Bus == null || !_mc2900Bus.IsOpen)
                {
                    MessageBox.Show("请先连接MC2900", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _btnStart[idx].Enabled = true;
                    _btnStop[idx].Enabled = false;
                    _currentRunningProcess = -1;
                    SetResult(idx, "等待测试", Color.Gray);
                    UpdateAllRunStatus();
                    UpdateSyncTimeButtonState();
                    return;
                }

                if (!EnsureMcBusReady("工序2开始", true))
                {
                    AppendLog(1, "MC2900通信未就绪，无法启动工序2");
                    _btnStart[idx].Enabled = true;
                    _btnStop[idx].Enabled = false;
                    _currentRunningProcess = -1;
                    SetResult(idx, "等待测试", Color.Gray);
                    UpdateAllRunStatus();
                    UpdateSyncTimeButtonState();
                    return;
                }

                try
                {
                    long startSequence = ModbusDataLogger.GetLatestSequence();
                    using (_mc2900.SuspendQueryPolling())
                    {
                        await Task.Run(() => _mc2900.SetSystemManualMode());
                    }

                    string frameText = ModbusDataLogger.FramesToText(
                        ModbusDataLogger.GetFramesAfter(startSequence, ModbusDataLogger.PortType.MC2900));
                    string status = "模块通信启动: 设置系统手动模式(FC05 0x0015=0x0000) Result=成功";
                    _process2PendingStartRaw = $"{status}{Environment.NewLine}{frameText}";
                    AppendProcess2ModulePacketLog(70, "工序2-模块通信启动原始记录", status, frameText);
                    AppendLog(1, "模块通信启动成功，已发送切手动模式数据包");
                }
                catch (Exception ex)
                {
                    AppendLog(1, "设置系统为手动模式失败: " + ex.Message);
                    _btnStart[idx].Enabled = true;
                    _btnStop[idx].Enabled = false;
                    _currentRunningProcess = -1;
                    SetResult(idx, "等待测试", Color.Gray);
                    UpdateAllRunStatus();
                    UpdateSyncTimeButtonState();
                    return;
                }

                _moduleTestActive = true;
                ResetMcBackgroundOperations();
                ResetModuleStates();
                SetModuleTestStatus(true, "测试中");
            }
            else if (idx == 2)
            {
                if (_mc2900 == null || _mc2900Bus == null || !_mc2900Bus.IsOpen)
                {
                    MessageBox.Show("请先连接MC2900", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _btnStart[idx].Enabled = true;
                    _btnStop[idx].Enabled = false;
                    _currentRunningProcess = -1;
                    SetResult(idx, "等待测试", Color.Gray);
                    UpdateAllRunStatus();
                    UpdateSyncTimeButtonState();
                    return;
                }

                if (!EnsureMcBusReady("工序3开始", true))
                {
                    AppendLog(idx, "MC2900通信未就绪，无法启动工序3");
                    _btnStart[idx].Enabled = true;
                    _btnStop[idx].Enabled = false;
                    _currentRunningProcess = -1;
                    SetResult(idx, "等待测试", Color.Gray);
                    UpdateAllRunStatus();
                    UpdateSyncTimeButtonState();
                    UpdateProcess3ControlButtonsState();
                    return;
                }

                try
                {
                    using (_mc2900.SuspendQueryPolling())
                    {
                        await Task.Run(() => _mc2900.SetSystemManualMode());
                    }
                    await RefreshProcess3ControlStatesAsync();
                    AppendLog(idx, "工序3已切换到手动模式，可执行DO/LLVD1/BLVD控制测试");
                }
                catch (Exception ex)
                {
                    AppendLog(idx, "设置系统为手动模式失败: " + ex.Message);
                    _btnStart[idx].Enabled = true;
                    _btnStop[idx].Enabled = false;
                    _currentRunningProcess = -1;
                    SetResult(idx, "等待测试", Color.Gray);
                    UpdateAllRunStatus();
                    UpdateSyncTimeButtonState();
                    UpdateProcess3ControlButtonsState();
                    return;
                }
            }
            else if (idx == 3)
            {
                ResetProcess4CalibrationControls();
                SetCalibrationMode(true);
                _paramCalibrationManualMode = true;
                UpdateProcess4IpInputState();
                SetResult(idx, "校准模式", Color.Orange);
                AppendLog(idx, "工序4手动参数校准模式已启用，可输入 Measure 和 Actual 数据");
                BeginProcess4Log(barcode);
            }
            
            // 更新所有工序的运行状态标签
            UpdateAllRunStatus();
            UpdateSyncTimeButtonState();
            UpdateProcess3ControlButtonsState();

            // 高亮与本工序相关的模块按钮
            HighlightModuleForProcess(idx, true);

            int processIdx = idx; // 捕获索引

            if (idx != 3)
            {
                _processStopCts = new CancellationTokenSource();
                _ = Task.Factory.StartNew(() =>
                {
                    try
                    {
                        _processes[processIdx].Run(barcode, _processStopCts.Token);
                    }
                    catch (Exception ex)
                    {
                        ModbusDataLogger.LogInfo($"工序{processIdx + 1}异常: {ex.Message}");
                    }
                }, _processStopCts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }

            if (idx == 1)
            {
                // Run 启动后把启动数据包补挂到本次测试原始记录
                _ = Task.Run(async () =>
                {
                    for (int i = 0; i < 50; i++)
                    {
                        await Task.Delay(100).ConfigureAwait(false);
                        if (IsDisposed) return;
                        try
                        {
                            BeginInvoke(new Action(AttachPendingProcess2StartRawIfReady));
                        }
                        catch { return; }

                        if (string.IsNullOrWhiteSpace(_process2PendingStartRaw))
                            break;
                    }
                });

                RefreshModuleStatus();
                _moduleRefreshTimer?.Start();
            }
            
            ModbusDataLogger.LogInfo($"工序{idx + 1}已启动，条码: {barcode}");
        }

        private void UpdateSafetyStandaloneButtonsState()
        {
            bool canOperate =
                !_safetyStandaloneRunning &&
                _currentRunningProcess == -1 &&
                !_stopRequested &&
                _safety != null &&
                _safetyBus != null &&
                _safetyBus.IsOpen;

            if (_btnSafetyStandaloneTest == null)
                return;

            foreach (var button in _btnSafetyStandaloneTest)
            {
                if (button != null)
                    button.Enabled = canOperate;
            }
        }
        private async void BtnSafetyStandaloneTest_Click(object sender, EventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is int row))
                return;

            if (_currentRunningProcess != -1 || _stopRequested || _safetyStandaloneRunning)
            {
                MessageBox.Show("当前有工序在运行或停止中，无法执行独立安规测试", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string testName = row == 2 ? "交流对直流" : "直流对地";
            string stepCode = row == 2 ? "04" : "05";
            _safetyStandaloneRunning = true;
            UpdateSafetyStandaloneButtonsState();

            try
            {
                SetSafetyMeasure(row, 0, "--");
                SetSafetyMeasure(row, 1, "--");

                var sample = await Task.Run(() => RunSafetyStandaloneTest(row, testName, stepCode));
                SetSafetyMeasure(row, 0, sample.LeftDisplayValue);
                SetSafetyMeasure(row, 1, sample.RightDisplayValue);
                await Task.Run(() => UploadStandaloneSafetyResult(row, testName, sample));
            }
            catch (Exception ex)
            {
                MessageBox.Show(testName + "启动测试失败: " + ex.Message, "安规测试", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _safetyStandaloneRunning = false;
                UpdateSafetyStandaloneButtonsState();
            }
        }
        private SafetyAsciiProtocol.SafetyAsciiSample RunSafetyStandaloneTest(int row, string testName, string expectedStepCode)
        {
            bool acquiredHere = false;
            try
            {
                if (!EnsureSafetyBusReady(testName, out acquiredHere))
                    throw new InvalidOperationException("安规测试仪未连接或串口未打开");

                var bus = _safety.GetBus();
                SafetyAsciiProtocol.SendRemoteMode(bus, msg => AppendLog(0, testName + ": " + msg));
                SafetyAsciiProtocol.SendStart(bus, msg => AppendLog(0, testName + ": " + msg));
                var sample = SafetyAsciiProtocol.QueryLatestSample(
                    bus,
                    expectedStepCode,
                    8,
                    msg => AppendLog(0, testName + ": " + msg),
                    latest =>
                    {
                        SetSafetyMeasure(row, 0, latest.LeftDisplayValue);
                        SetSafetyMeasure(row, 1, latest.RightDisplayValue);
                    });

                return sample;
            }
            finally
            {
                var bus = _safety.GetBus();
                if (bus != null)
                {
                    try
                    {
                        SafetyAsciiProtocol.TryExitRemote(bus, msg => AppendLog(0, testName + ": " + msg));
                        AppendLog(0, testName + ": 测试完成后已发送一次解除远程命令");
                    }
                    catch { }
                }

                if (acquiredHere)
                {
                    try { _safety.GetBus()?.Release(); } catch { }
                }
            }
        }

        private void UploadStandaloneSafetyResult(int row, string testName, SafetyAsciiProtocol.SafetyAsciiSample sample)
        {
            string barcode = _txtBarcode[0]?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(barcode))
            {
                AppendLog(0, testName + ": MES上传失败，工序1条码为空");
                return;
            }

            int leftStep = row == 2 ? 7 : 9;
            int rightStep = row == 2 ? 8 : 10;
            string leftName = row == 2 ? "交流对直流(KV)" : "直流对地(KV)";
            string rightName = "漏电流(mA)";
            string details =
                MesClient.BuildDetail(leftStep, 0, 0, ParseStandaloneValue(sample.LeftDisplayValue), true, leftName, 0) +
                MesClient.BuildDetail(rightStep, 0, 0, ParseStandaloneValue(sample.RightDisplayValue), true, rightName, 0) +
                MesClient.BuildDetail(row == 2 ? 14 : 15, 0, 0, 0, true, testName + "原始回包HEX=" + sample.RawResponseHex, 0);

            string filePath = SaveStandaloneSafetyFile(barcode, testName, sample, details);

            AppendLog(0, testName + $": MES上传开始, 条码={barcode}");
            try
            {
                var header = TryBuildMesHeaderForWorkbook(barcode, "PASS");
                string workbookPath = MesUploadWorkbookWriter.SaveUploadWorkbook(
                    barcode, testName, "PASS", details, filePath, sample.RawResponseHex, 1, _mesCheckEnabled, header);
                AppendLog(0, testName + ": 上传快照已保存: " + workbookPath);

                if (!_mesCheckEnabled)
                {
                    AppendLog(0, testName + ": 已关闭 MES 检查，跳过 SaveTestData 上传");
                    return;
                }

                bool uploaded = _mes != null && _mes.SaveTestData(barcode, "PASS", details, filePath);
                AppendLog(0, testName + (uploaded ? $": MES上传成功, 条码={barcode}" : $": MES上传失败, 条码={barcode}"));
            }
            catch (Exception ex)
            {
                AppendLog(0, testName + $": MES上传失败, 条码={barcode}, 原因={ex.Message}");
            }
        }

        private MesHeaderInfo TryBuildMesHeaderForWorkbook(string barcode, string result)
        {
            if (!_mesCheckEnabled)
                return null;

            if (_mes != null && _mes.IsLoaded)
            {
                try { return _mes.BuildHeaderInfo(barcode, result); }
                catch { }
            }

            return MesHeaderInfo.CreateEmpty(barcode, result);
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // MainForm
            // 
            this.ClientSize = new System.Drawing.Size(1184, 568);
            this.Name = "MainForm";
            this.ResumeLayout(false);

        }

        private string SaveStandaloneSafetyFile(string barcode, string testName, SafetyAsciiProtocol.SafetyAsciiSample sample, string details)
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "Process1");
            Directory.CreateDirectory(dir);
            string safeName = testName.Replace("/", "_").Replace("\\", "_").Replace(":", "_");
            string path = Path.Combine(dir, $"{barcode}_{DateTime.Now:yyyyMMdd_HHmmss}_{safeName}.txt");

            using (var writer = new StreamWriter(path))
            {
                writer.WriteLine("条码: " + barcode);
                writer.WriteLine("工序: 1");
                writer.WriteLine("测试项: " + testName);
                writer.WriteLine("结果: PASS");
                writer.WriteLine("左值: " + sample.LeftDisplayValue);
                writer.WriteLine("右值: " + sample.RightDisplayValue);
                writer.WriteLine("原始回包HEX: " + sample.RawResponseHex);
                writer.WriteLine("---MES Details---");
                writer.Write(details);
            }

            return path;
        }

        private float ParseStandaloneValue(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                return 0f;

            string text = rawValue.Trim();
            if (text.StartsWith(">", StringComparison.Ordinal) || text.StartsWith("<", StringComparison.Ordinal))
                text = text.Substring(1);

            if (!float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value))
                return 0f;

            return value;
        }
    }
}

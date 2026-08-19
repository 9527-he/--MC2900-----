using System;
using System.Drawing;
using System.IO.Ports;
using System.Text;
using System.Windows.Forms;

namespace InsertFrameTest.Forms
{
    public class SerialDebugForm : Form
    {
        private ComboBox _cmbPorts;
        private ComboBox _cmbBaud;
        private Button _btnRefreshPorts;
        private Button _btnConnect;
        private TextBox _txtSend;
        private Button _btnSend;
        private RichTextBox _rtbLog;
        private StatusStrip _statusStrip;
        private ToolStripStatusLabel _lblStatus;

        private SerialPort _port;

        public SerialDebugForm()
        {
            Text = "串口调试工具";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(720, 480);
            Font = new Font("微软雅黑", 9f);

            BuildUi();
            RefreshPortList();
        }

        private void BuildUi()
        {
            var topPanel = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };

            _cmbPorts = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 }; 
            _cmbPorts.Margin = new Padding(4);
            _cmbPorts.DropDown += (s, e) => RefreshPortList();

            _btnRefreshPorts = new Button { Text = "刷新", Width = 60, Height = 28, Margin = new Padding(6,0,6,0) };
            _btnRefreshPorts.Click += (s, e) => RefreshPortList();

            _cmbBaud = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            _cmbBaud.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200" });
            _cmbBaud.SelectedIndex = 0;

            _btnConnect = new Button { Text = "连接", Width = 100, Height = 30, BackColor = Color.FromArgb(0,120,215), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _btnConnect.Click += BtnConnect_Click;

            var sendPanel = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8) };
            _txtSend = new TextBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 10f) };
            _btnSend = new Button { Text = "发送", Width = 90, Dock = DockStyle.Right, BackColor = Color.FromArgb(0,120,215), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _btnSend.Click += BtnSend_Click;

            sendPanel.Controls.Add(_txtSend);
            sendPanel.Controls.Add(_btnSend);

            _rtbLog = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 10f) };

            _statusStrip = new StatusStrip();
            _lblStatus = new ToolStripStatusLabel { Text = "未连接" };
            _statusStrip.Items.Add(_lblStatus);

            // topPanel layout
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            flow.Controls.Add(new Label { Text = "串口:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6,6,6,6) });
            flow.Controls.Add(_cmbPorts);
            flow.Controls.Add(_btnRefreshPorts);
            flow.Controls.Add(new Label { Text = "波特率:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6,6,6,6) });
            flow.Controls.Add(_cmbBaud);
            flow.Controls.Add(_btnConnect);

            topPanel.Controls.Add(flow);

            Controls.Add(_rtbLog);
            Controls.Add(sendPanel);
            Controls.Add(topPanel);
            Controls.Add(_statusStrip);

            FormClosing += (s, e) => { try { _port?.Close(); } catch { } };
        }

        private void RefreshPortList()
        {
            try
            {
                var names = SerialPort.GetPortNames();
                Array.Sort(names);
                var prev = _cmbPorts.SelectedItem as string;
                _cmbPorts.Items.Clear();
                _cmbPorts.Items.AddRange(names);
                if (!string.IsNullOrEmpty(prev) && _cmbPorts.Items.Contains(prev))
                    _cmbPorts.SelectedItem = prev;
                else if (_cmbPorts.Items.Count > 0)
                    _cmbPorts.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("刷新串口列表失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnConnect_Click(object sender, EventArgs e)
        {
            if (_port != null && _port.IsOpen)
            {
                try
                {
                    _port.Close();
                }
                catch { }
                SetConnected(false);
                return;
            }

            if (_cmbPorts.SelectedItem == null)
            {
                MessageBox.Show("请先选择串口", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                _port = new SerialPort((string)_cmbPorts.SelectedItem, int.Parse((string)_cmbBaud.SelectedItem ?? _cmbBaud.Text, System.Globalization.CultureInfo.InvariantCulture));
                _port.Encoding = Encoding.UTF8;
                _port.DataReceived += Port_DataReceived;
                _port.Open();
                SetConnected(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开串口失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetConnected(false);
            }
        }

        private void SetConnected(bool connected)
        {
            if (connected)
            {
                _lblStatus.Text = "已连接";
                _btnConnect.Text = "断开";
                _btnConnect.BackColor = Color.FromArgb(196, 43, 28);
                _cmbPorts.Enabled = false;
                _cmbBaud.Enabled = false;
                _btnRefreshPorts.Enabled = false;
            }
            else
            {
                _lblStatus.Text = "未连接";
                _btnConnect.Text = "连接";
                _btnConnect.BackColor = Color.FromArgb(0,120,215);
                _cmbPorts.Enabled = true;
                _cmbBaud.Enabled = true;
                _btnRefreshPorts.Enabled = true;
            }
        }

        private void BtnSend_Click(object sender, EventArgs e)
        {
            if (_port == null || !_port.IsOpen)
            {
                MessageBox.Show("串口未连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                string text = _txtSend.Text ?? string.Empty;
                _port.Write(text);
                AppendLog($"TX: {text}");
            }
            catch (Exception ex)
            {
                MessageBox.Show("发送失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string data = _port.ReadExisting();
                if (string.IsNullOrEmpty(data)) return;
                BeginInvoke(new Action(() => AppendLog($"RX: {data}")));
            }
            catch { }
        }

        private void AppendLog(string text)
        {
            if (_rtbLog.IsDisposed) return;
            _rtbLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
            _rtbLog.ScrollToCaret();
        }
    }
}

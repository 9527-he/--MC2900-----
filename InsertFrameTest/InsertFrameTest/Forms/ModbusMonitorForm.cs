using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using InsertFrameTest.Communication;

namespace InsertFrameTest.Forms
{
    /// <summary>
    /// Modbus数据流监控窗口
    /// </summary>
    public class ModbusMonitorForm : Form
    {
        private RichTextBox _rtbData;
        private Button _btnClear;
        private Button _btnSave;
        private Button _btnPause;
        private CheckBox _chkAutoScroll;
        private Label _lblCount;
        private ComboBox _cmbPortFilter;
        private bool _isPaused = false;
        private int _frameCount = 0;
        private ModbusDataLogger.PortType _currentFilter = ModbusDataLogger.PortType.All;
        private readonly List<ModbusDataLogger.DataFrame> _sessionFrames = new List<ModbusDataLogger.DataFrame>();
        private bool _isMonitoringActive;

        public ModbusMonitorForm()
        {
            Text = "数据流监控";
            Size = new Size(1000, 600);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Consolas", 9f);

            BuildUI();
            
            // 订阅数据事件
            ModbusDataLogger.OnDataLogged += OnDataLogged;
        }

        private void BuildUI()
        {
            // 顶部工具栏
            var toolPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
                BackColor = Color.FromArgb(240, 240, 240),
                Padding = new Padding(5)
            };

            _btnClear = new Button
            {
                Text = "清空",
                Size = new Size(70, 28),
                Location = new Point(10, 6),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(220, 220, 220)
            };
            _btnClear.Click += (s, e) => ClearData();

            _btnSave = new Button
            {
                Text = "保存",
                Size = new Size(70, 28),
                Location = new Point(90, 6),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(220, 220, 220)
            };
            _btnSave.Click += (s, e) => SaveData();

            _btnPause = new Button
            {
                Text = "暂停",
                Size = new Size(70, 28),
                Location = new Point(170, 6),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(255, 200, 100)
            };
            _btnPause.Click += (s, e) => TogglePause();

            _chkAutoScroll = new CheckBox
            {
                Text = "自动滚动",
                AutoSize = true,
                Location = new Point(260, 10),
                Checked = true
            };

            // 端口选择下拉框
            var lblFilter = new Label
            {
                Text = "监控端口:",
                AutoSize = true,
                Location = new Point(350, 10),
                Font = new Font("微软雅黑", 9f)
            };

            _cmbPortFilter = new ComboBox
            {
                Size = new Size(120, 24),
                Location = new Point(410, 6),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("微软雅黑", 9f)
            };
            _cmbPortFilter.Items.Add("全部端口");
            _cmbPortFilter.Items.Add("安规测试仪");
            _cmbPortFilter.Items.Add("MC2900");
            _cmbPortFilter.Items.Add("SNMP");
            _cmbPortFilter.SelectedIndex = 0;
            _cmbPortFilter.SelectedIndexChanged += (s, e) => ChangePortFilter();

            _lblCount = new Label
            {
                Text = "帧数: 0",
                AutoSize = true,
                Location = new Point(550, 10),
                Font = new Font("微软雅黑", 9f)
            };

            toolPanel.Controls.AddRange(new Control[] {
                _btnClear, _btnSave, _btnPause, _chkAutoScroll, 
                lblFilter, _cmbPortFilter, _lblCount
            });

            // 数据显示区域
            _rtbData = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.LightGreen,
                Font = new Font("Consolas", 10f),
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Both
            };

            // 图例说明
            var legendPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 30,
                BackColor = Color.FromArgb(240, 240, 240),
                Padding = new Padding(5)
            };

            var lblSafety = new Label
            {
                Text = "■ 安规测试仪",
                ForeColor = Color.Orange,
                AutoSize = true,
                Location = new Point(10, 7),
                Font = new Font("微软雅黑", 9f)
            };

            var lblMC2900 = new Label
            {
                Text = "■ MC2900",
                ForeColor = Color.Cyan,
                AutoSize = true,
                Location = new Point(100, 7),
                Font = new Font("微软雅黑", 9f)
            };

            var lblSNMP = new Label
            {
                Text = "■ SNMP",
                ForeColor = Color.MediumPurple,
                AutoSize = true,
                Location = new Point(180, 7),
                Font = new Font("微软雅黑", 9f)
            };

            var lblSend = new Label
            {
                Text = "■ 发送",
                ForeColor = Color.LightGreen,
                AutoSize = true,
                Location = new Point(250, 7),
                Font = new Font("微软雅黑", 9f)
            };

            var lblReceive = new Label
            {
                Text = "■ 接收",
                ForeColor = Color.LightBlue,
                AutoSize = true,
                Location = new Point(310, 7),
                Font = new Font("微软雅黑", 9f)
            };

            var lblInfo = new Label
            {
                Text = "■ 信息",
                ForeColor = Color.Yellow,
                AutoSize = true,
                Location = new Point(370, 7),
                Font = new Font("微软雅黑", 9f)
            };

            legendPanel.Controls.AddRange(new Control[] { lblSafety, lblMC2900, lblSNMP, lblSend, lblReceive, lblInfo });

            Controls.Add(_rtbData);
            Controls.Add(legendPanel);
            Controls.Add(toolPanel);
        }

        private void ChangePortFilter()
        {
            switch (_cmbPortFilter.SelectedIndex)
            {
                case 0:
                    _currentFilter = ModbusDataLogger.PortType.All;
                    break;
                case 1:
                    _currentFilter = ModbusDataLogger.PortType.Safety;
                    break;
                case 2:
                    _currentFilter = ModbusDataLogger.PortType.MC2900;
                    break;
                case 3:
                    _currentFilter = ModbusDataLogger.PortType.SNMP;
                    break;
            }
            
            // 重新加载显示
            ReloadDisplay();
            
            string filterName = _cmbPortFilter.SelectedItem.ToString();
            ModbusDataLogger.LogInfo($"切换到监控: {filterName}");
        }

        private void ReloadDisplay()
        {
            _rtbData.Clear();
            _frameCount = 0;
            
            foreach (var frame in _sessionFrames)
            {
                if (ShouldDisplay(frame))
                {
                    DisplayFrame(frame);
                }
            }
        }

        private bool ShouldDisplay(ModbusDataLogger.DataFrame frame)
        {
            if (_currentFilter == ModbusDataLogger.PortType.All)
                return true;
            return frame.Port == _currentFilter;
        }

        private void OnDataLogged(ModbusDataLogger.DataFrame frame)
        {
            // 必须用 BeginInvoke：同步 Invoke 会在 Modbus 总线锁内阻塞，导致整机卡死
            if (InvokeRequired)
            {
                BeginInvoke(new Action<ModbusDataLogger.DataFrame>(OnDataLogged), frame);
                return;
            }

            if (!_isMonitoringActive || !Visible)
                return;

            if (_isPaused)
                return;

            _sessionFrames.Add(frame);

            // 根据过滤条件决定是否显示
            if (!ShouldDisplay(frame))
                return;

            DisplayFrame(frame);
        }

        private void DisplayFrame(ModbusDataLogger.DataFrame frame)
        {
            // 格式化显示
            string text = ModbusDataLogger.FrameToString(frame);
            Color color = GetFrameColor(frame);

            // 追加到RichTextBox
            int start = _rtbData.TextLength;
            _rtbData.AppendText(text + "\n");
            int end = _rtbData.TextLength;

            // 设置颜色
            _rtbData.Select(start, end - start);
            _rtbData.SelectionColor = color;
            _rtbData.SelectionLength = 0;

            _frameCount++;
            _lblCount.Text = $"帧数: {_frameCount}";

            // 自动滚动
            if (_chkAutoScroll.Checked)
            {
                _rtbData.ScrollToCaret();
            }

            // 限制行数（最多显示5000行）
            if (_rtbData.Lines.Length > 5000)
            {
                int removeIndex = _rtbData.Text.IndexOf('\n');
                if (removeIndex > 0)
                {
                    _rtbData.Select(0, removeIndex + 1);
                    _rtbData.SelectedText = "";
                }
            }
        }

        private Color GetFrameColor(ModbusDataLogger.DataFrame frame)
        {
            // 信息类型用黄色
            if (frame.Direction == ModbusDataLogger.Direction.Info)
                return Color.Yellow;
            
            // 根据端口类型设置颜色
            if (frame.Port == ModbusDataLogger.PortType.Safety)
            {
                // 安规仪：橙色发送，浅橙色接收
                return frame.Direction == ModbusDataLogger.Direction.Send 
                    ? Color.FromArgb(255, 180, 100) // 橙色
                    : Color.FromArgb(255, 200, 150);
            }
            else if (frame.Port == ModbusDataLogger.PortType.MC2900)
            {
                // MC2900：青色发送，浅青色接收
                return frame.Direction == ModbusDataLogger.Direction.Send 
                    ? Color.FromArgb(100, 200, 255) // 青色
                    : Color.FromArgb(150, 220, 255);
            }
            else if (frame.Port == ModbusDataLogger.PortType.SNMP)
            {
                // SNMP：紫色发送，浅紫色接收
                return frame.Direction == ModbusDataLogger.Direction.Send
                    ? Color.FromArgb(180, 140, 255)
                    : Color.FromArgb(210, 180, 255);
            }
            
            // 默认：绿色发送，蓝色接收
            return frame.Direction == ModbusDataLogger.Direction.Send 
                ? Color.LightGreen 
                : Color.LightBlue;
        }

        private void ClearData()
        {
            _sessionFrames.Clear();
            _rtbData.Clear();
            _frameCount = 0;
            _lblCount.Text = "帧数: 0";
        }

        private void SaveData()
        {
            var sfd = new SaveFileDialog
            {
                Filter = "文本文件|*.txt|所有文件|*.*",
                FileName = $"Modbus数据流_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };

            if (sfd.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    string content = ModbusDataLogger.FramesToText(_sessionFrames);
                    File.WriteAllText(sfd.FileName, content, Encoding.UTF8);
                    MessageBox.Show("保存成功！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void TogglePause()
        {
            _isPaused = !_isPaused;
            _btnPause.Text = _isPaused ? "继续" : "暂停";
            _btnPause.BackColor = _isPaused ? Color.LightGreen : Color.FromArgb(255, 200, 100);
            
            if (_isPaused)
            {
                ModbusDataLogger.LogInfo("数据监控已暂停");
            }
            else
            {
                ModbusDataLogger.LogInfo("数据监控已恢复");
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 取消订阅事件，但不关闭窗口，只是隐藏
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                _isMonitoringActive = false;
                Hide();
            }
            else
            {
                ModbusDataLogger.OnDataLogged -= OnDataLogged;
                base.OnFormClosing(e);
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);

            if (Visible)
            {
                _sessionFrames.Clear();
                _rtbData.Clear();
                _frameCount = 0;
                _lblCount.Text = "帧数: 0";
                _isPaused = false;
                _btnPause.Text = "暂停";
                _btnPause.BackColor = Color.FromArgb(255, 200, 100);
                _isMonitoringActive = true;
                ModbusDataLogger.LogInfo("数据监控已开启（仅记录开启后的数据）");
            }
            else
            {
                _isMonitoringActive = false;
            }
        }
    }
}

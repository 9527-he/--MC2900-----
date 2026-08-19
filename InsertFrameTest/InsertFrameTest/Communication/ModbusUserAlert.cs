using System;
using System.Threading;
using System.Windows.Forms;

namespace InsertFrameTest.Communication
{
    /// <summary>
    /// Modbus 通信类用户提示（防刷屏）。
    /// </summary>
    public static class ModbusUserAlert
    {
        private static readonly object Sync = new object();
        private static DateTime _lastCrcPopupUtc = DateTime.MinValue;
        private static DateTime _lastCommPopupUtc = DateTime.MinValue;
        private const int PopupCooldownSeconds = 60;

        /// <summary>
        /// 重新连接或开始新一轮测试时调用，允许再次提示。
        /// </summary>
        public static void ResetCrcAlert()
        {
            ResetAlerts();
        }

        public static void ResetAlerts()
        {
            lock (Sync)
            {
                _lastCrcPopupUtc = DateTime.MinValue;
                _lastCommPopupUtc = DateTime.MinValue;
            }
        }

        /// <summary>
        /// 连续尝试多次仍无法通信时的提示（BeginInvoke，不阻塞总线锁）。
        /// </summary>
        public static void TryShowCommAbnormal(string portName, int attempts, string detail)
        {
            lock (Sync)
            {
                if ((DateTime.UtcNow - _lastCommPopupUtc).TotalSeconds < PopupCooldownSeconds)
                    return;
                _lastCommPopupUtc = DateTime.UtcNow;
            }

            string port = string.IsNullOrWhiteSpace(portName) ? "MC2900" : portName;
            string title = "通信异常";
            string body =
                $"{port} 已连续尝试 {attempts} 次仍无法正常通信。\n\n" +
                "点击「确定」后程序仍会继续发送数据尝试恢复通信。\n" +
                "（60 秒内如仍失败将不再重复弹窗）\n\n" +
                "请检查：\n" +
                "1. 停止测试后可断开并重新连接串口\n" +
                "2. 确认设备已开机，波特率与设备一致（一般为 8N1）\n" +
                "3. 检查 RS485 的 A/B 是否接反，GND 是否共地\n" +
                "4. 更换短屏蔽线或换一个 USB 串口再试\n" +
                "5. 通信线远离强电/变频器，必要时重启设备\n\n" +
                "最近一次错误：\n" +
                (string.IsNullOrWhiteSpace(detail) ? "（无详细信息）" : detail.Trim());

            ShowOnUi(title, body);
        }

        /// <summary>
        /// CRC 失败提示。同一时间窗口内最多弹一次。
        /// </summary>
        public static void TryShowCrcError(string portName, ushort crcCalc, ushort crcRecv)
        {
            lock (Sync)
            {
                if ((DateTime.UtcNow - _lastCrcPopupUtc).TotalSeconds < PopupCooldownSeconds)
                    return;
                _lastCrcPopupUtc = DateTime.UtcNow;
            }

            string title = "通信校验失败";
            string body =
                (string.IsNullOrWhiteSpace(portName) ? "MC2900" : portName) +
                " 数据校验失败（CRC错误），通信数据可能受干扰或接线异常。\n\n" +
                "点击「确定」后程序仍会继续发送数据尝试恢复通信。\n" +
                "（60 秒内如仍失败将不再重复弹窗）\n\n" +
                "请检查：\n" +
                "1. 停止测试后可断开并重新连接串口\n" +
                "2. 确认波特率与设备一致（一般为 8N1）\n" +
                "3. 检查 RS485 的 A/B 是否接反，GND 是否共地\n" +
                "4. 更换短屏蔽线或换一个 USB 串口再试\n" +
                "5. 通信线远离强电/变频器，必要时重启设备\n\n" +
                $"技术信息：计算CRC=0x{crcCalc:X4}，接收CRC=0x{crcRecv:X4}";

            ShowOnUi(title, body);
        }

        private static void ShowOnUi(string title, string body)
        {
            try
            {
                Form target = null;
                if (Application.OpenForms != null && Application.OpenForms.Count > 0)
                    target = Application.OpenForms[0];

                Action show = () =>
                {
                    try
                    {
                        MessageBox.Show(body, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    catch { }
                };

                if (target != null && !target.IsDisposed)
                {
                    if (target.InvokeRequired)
                        target.BeginInvoke(show);
                    else
                        show();
                }
                else
                {
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try { MessageBox.Show(body, title, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                        catch { }
                    });
                }
            }
            catch { }
        }
    }
}

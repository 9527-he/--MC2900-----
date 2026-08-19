using System;
using System.Windows.Forms;

namespace InsertFrameTest
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new Forms.MainForm());
            }
            catch (Exception ex)
            {
                // 将详细信息显示出来，便于诊断缺失的文件或程序集
                System.Diagnostics.Debug.WriteLine(ex.ToString());
                MessageBox.Show("启动失败: " + ex.Message + "\n\n详细: " + ex.ToString(), "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}

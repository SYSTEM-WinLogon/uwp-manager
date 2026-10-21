using System;
using System.Windows.Forms;

internal static class TestApp
{
    [STAThread]
    private static void Main()
    {
        MessageBox.Show("这是 UWP 管家的自动化测试包，可以安全卸载。", "UwpManager 测试应用",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}

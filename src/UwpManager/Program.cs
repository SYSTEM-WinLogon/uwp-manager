using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace UwpManager;

internal static class Program
{
    private static Mutex _instanceMutex;

    /// <summary>当前进程是否是从受限目录切换到本地副本后启动的。</summary>
    internal static bool Relocated { get; private set; }

    private static readonly string TargetDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "UWP Manager");

    [STAThread]
    private static void Main()
    {
        try
        {
            Relocated = Environment.GetCommandLineArgs()
                .Any(a => string.Equals(a, "--relocated", StringComparison.OrdinalIgnoreCase));

            // 某些目录被系统策略标记（应用包接口会返回 0x80070005），
            // 此时自动把自身复制到本地目录再从那里启动，保证“放在任何文件夹都能用”。
            if (!Relocated && NeedsRelocation() && TryRelocate()) return;

            _instanceMutex = new Mutex(true, @"Global\UwpManager.SingleInstance", out bool isNew);
            if (!isNew)
            {
                MessageBox.Show("UWP 管家已在运行中。", "UWP 管家", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Theme.Init();


            Application.ThreadException += (_, e) => ShowCrash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowCrash(e.ExceptionObject as Exception);

            Application.Run(new Ui.MainForm());

            ReleaseSingleInstance();
        }
        catch (Exception ex)
        {
            CrashLog.Write("main", ex);
            throw;
        }
    }

    private static bool NeedsRelocation()
    {
        try
        {
            var pm = new Windows.Management.Deployment.PackageManager();
            foreach (var _ in pm.FindPackagesForUser("")) return false;
            return false;
        }
        catch (UnauthorizedAccessException) { return true; }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x80070005)) { return true; }
        catch { return false; }
    }


    private static bool TryRelocate()
    {
        try
        {
            string current = Application.ExecutablePath;
            string target = Path.Combine(TargetDir, "UwpManager.exe");
            if (string.Equals(current, target, StringComparison.OrdinalIgnoreCase)) return false;

            Directory.CreateDirectory(TargetDir);
            File.Copy(current, target, true);

            Process.Start(new ProcessStartInfo(target, "--relocated")
            {
                UseShellExecute = true,
                WorkingDirectory = TargetDir,
            });
            return true;
        }
        catch (Exception ex)
        {
            CrashLog.Write("relocate", ex);
            return false;
        }
    }

    /// <summary>提前释放单实例锁（切换副本或重启时使用）。</summary>
    internal static void ReleaseSingleInstance()
    {
        try
        {
            _instanceMutex?.ReleaseMutex();
            _instanceMutex?.Dispose();
        }
        catch { }
        _instanceMutex = null;
    }

    private static void ShowCrash(Exception ex)
    {
        CrashLog.Write("unhandled", ex);
        try
        {
            MessageBox.Show(ex?.ToString() ?? "未知错误", "UWP 管家 - 发生错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch { }
    }
}

internal static class CrashLog
{
    public static void Write(string context, Exception ex)
    {
        try
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UwpManager-error.log");
            System.IO.File.AppendAllText(path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{ex}{Environment.NewLine}{new string('-', 60)}{Environment.NewLine}");
        }
        catch { }
    }
}

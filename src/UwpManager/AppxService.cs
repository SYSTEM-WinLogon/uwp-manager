using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace UwpManager;

public sealed class AppxOperationException : Exception
{
    public int Code { get; }
    public string Hint { get; }

    public AppxOperationException(int code, string message, string hint = null) : base(message)
    {
        Code = code;
        Hint = hint;
    }

    public string CodeText => "0x" + Code.ToString("X8");

    public static AppxOperationException From(Exception ex, string action)
    {
        int code = ex.HResult;
        string hint = code switch
        {
            unchecked((int)0x80073CF3) => "缺少依赖包：请一并安装该应用的依赖框架包（Dependencies 文件夹中的 .appx/.msix）。",
            unchecked((int)0x80073D02) => "该应用仍有进程在运行，请先关闭应用窗口后重试。",
            unchecked((int)0x80073CF9) => "安装包无效或已损坏，请确认文件完整。",
            unchecked((int)0x80073CF1) => "找不到指定的应用包，可能已被卸载。",
            unchecked((int)0x80073CFB) => "已安装相同版本，或版本低于已安装版本。",
            unchecked((int)0x80073CFD) => "应用与当前系统不兼容（体系结构不匹配）。",
            unchecked((int)0x80070005) => "权限不足，请以管理员身份运行本工具。",
            unchecked((int)0x80073D06) => "该应用包依赖更高版本的 Windows。",
            unchecked((int)0x80073CF0) => "无法打开安装包文件。",
            unchecked((int)0x80073B06) => "安装包未签名，请先在系统中启用旁加载（Sideloading）。",
            unchecked((int)0x80073CFA) => "无法卸载：该包是系统的一部分，或被其他应用 / 系统组件依赖。",
            unchecked((int)0x80073CF5) => "无法卸载：该包正被系统占用或受系统保护。",
            unchecked((int)0x80073CFE) => "无法卸载：仍有应用依赖此框架包，请先卸载依赖它的应用。",
            _ => "操作系统返回错误，请查看错误代码。",
        };
        string message = string.IsNullOrWhiteSpace(ex.Message) ? $"{action}失败" : ex.Message;
        return new AppxOperationException(code, $"{action}失败（0x{code:X8}）", hint);
    }
}

public static class AppxService
{
    /// <summary>直接调用 WinRT 接口被系统拒绝时，自动改用系统 PowerShell 读取（兼容模式）。</summary>
    public static bool CompatMode { get; private set; }

    private static bool IsAccessDenied(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
            if (e.HResult == unchecked((int)0x80070005)) return true;
        return false;
    }

    public static async Task<List<AppPackage>> LoadAsync(IProgress<LoadProgress> progress, CancellationToken token)
    {
        List<AppPackage> list;
        try
        {
            list = await Task.Run(() => LoadDirect(token), token);
        }
        catch (Exception ex) when (IsAccessDenied(ex))
        {
            CompatMode = true;
            list = await Task.Run(() => LoadViaShell(token), token);
        }

        await Task.Run(() => ComputeSizes(list, progress, token), token);
        return list;
    }

    // ---------------- 直接 WinRT 路径 ----------------
    private static List<AppPackage> LoadDirect(CancellationToken token)
    {
        var pm = new PackageManager();
        var result = new List<AppPackage>();
        foreach (var p in pm.FindPackagesForUser(""))
        {
            token.ThrowIfCancellationRequested();
            try { result.Add(Map(p)); }
            catch { }
        }
        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return result;
    }

    private static AppPackage Map(Package p)
    {
        bool isSystem = p.SignatureKind == PackageSignatureKind.System ||
                        (!string.IsNullOrEmpty(p.InstalledPath) &&
                         p.InstalledPath.StartsWith(@"C:\Windows\SystemApps", StringComparison.OrdinalIgnoreCase));

        var kind = PackageKind.App;
        if (p.IsFramework) kind = PackageKind.Framework;
        else if (p.IsResourcePackage) kind = PackageKind.Resource;
        else if (isSystem) kind = PackageKind.System;

        string logo = "";
        try { if (p.Logo != null && p.Logo.IsFile) logo = p.Logo.LocalPath; } catch { }

        var v = p.Id.Version;
        return new AppPackage
        {
            DisplayName = Safe(() => p.DisplayName),
            IdName = Safe(() => p.Id.Name),
            FullName = Safe(() => p.Id.FullName),
            FamilyName = Safe(() => p.Id.FamilyName),
            Publisher = Safe(() => p.PublisherDisplayName),
            PublisherId = Safe(() => p.Id.Publisher),
            Version = new Version((int)v.Major, (int)v.Minor, (int)v.Build, (int)v.Revision),
            Architecture = ArchText(Safe(() => p.Id.Architecture.ToString())),
            InstallPath = Safe(() => p.InstalledPath),
            InstalledDate = SafeDate(() => p.InstalledDate),
            Description = Safe(() => p.Description),
            LogoPath = logo,
            IsBundle = SafeBool(() => p.IsBundle),
            IsDevelopmentMode = SafeBool(() => p.IsDevelopmentMode),
            Kind = kind,
        };
    }

    // ---------------- 兼容模式（系统 PowerShell） ----------------
    private const string EnumScript = @"
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$start = @{}
try {
  foreach ($a in Get-StartApps) {
    $i = $a.AppID.IndexOf('!')
    if ($i -gt 0) {
      $k = $a.AppID.Substring(0, $i)
      if (-not $start.ContainsKey($k)) { $start[$k] = [pscustomobject]@{ id = $a.AppID; name = $a.Name } }
    }
  }
} catch { }
$apps = New-Object System.Collections.ArrayList
foreach ($p in Get-AppxPackage) {
  [void]$apps.Add([pscustomobject]@{
    name = $p.Name
    publisher = $p.Publisher
    full = $p.PackageFullName
    version = $p.Version.ToString()
    path = $p.InstallLocation
    framework = [bool]$p.IsFramework
    resource = [bool]$p.IsResourcePackage
    signature = [int]$p.SignatureKind
    arch = [int]$p.Architecture
    dev = [bool]$p.IsDevelopmentMode
  })
}
[pscustomobject]@{ apps = $apps; start = $start } | ConvertTo-Json -Depth 5 -Compress
";

    private static List<AppPackage> LoadViaShell(CancellationToken token)
    {
        var (code, stdout, stderr) = RunPowerShell(EnumScript, 180000);
        if (code != 0 || string.IsNullOrWhiteSpace(stdout))
            throw new AppxOperationException(code == 0 ? -1 : code,
                "无法读取已安装的应用包", string.IsNullOrWhiteSpace(stderr) ? "系统 PowerShell 未返回数据。" : stderr.Trim());

        var startMap = new Dictionary<string, (string Aumid, string Name)>(StringComparer.OrdinalIgnoreCase);
        var apps = new List<AppPackage>();

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;

        if (root.TryGetProperty("start", out var start) && start.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in start.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                string id = Str(prop.Value, "id");
                string nm = Str(prop.Value, "name");
                if (!string.IsNullOrEmpty(id)) startMap[prop.Name] = (id, nm);
            }
        }

        if (!root.TryGetProperty("apps", out var appsElem)) return apps;
        var items = appsElem.ValueKind == JsonValueKind.Array
            ? appsElem.EnumerateArray().ToList()
            : new List<JsonElement> { appsElem };

        foreach (var a in items)
        {
            token.ThrowIfCancellationRequested();
            try { apps.Add(MapShell(a, startMap)); } catch { }
        }

        apps.Sort((x, y) => string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase));
        return apps;
    }

    private static AppPackage MapShell(JsonElement a, Dictionary<string, (string Aumid, string Name)> startMap)
    {
        string idName = Str(a, "name");
        string full = Str(a, "full");
        string family = FamilyFromFull(full);
        string publisherDn = Str(a, "publisher");

        bool framework = a.TryGetProperty("framework", out var fe) && fe.ValueKind == JsonValueKind.True;
        bool resource = a.TryGetProperty("resource", out var re) && re.ValueKind == JsonValueKind.True;
        int signature = a.TryGetProperty("signature", out var se) && se.TryGetInt32(out var sv) ? sv : 0;
        int arch = a.TryGetProperty("arch", out var ae) && ae.TryGetInt32(out var av) ? av : 11;
        bool dev = a.TryGetProperty("dev", out var de) && de.ValueKind == JsonValueKind.True;
        string path = Str(a, "path");
        string versionText = Str(a, "version");

        Version version = Version.TryParse(versionText, out var v) ? v : new Version(0, 0, 0, 0);

        bool isSystem = signature == 4 || path.StartsWith(@"C:\Windows\SystemApps", StringComparison.OrdinalIgnoreCase);
        var kind = PackageKind.App;
        if (framework) kind = PackageKind.Framework;
        else if (resource) kind = PackageKind.Resource;
        else if (isSystem) kind = PackageKind.System;

        string display = null, logo = null;
        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
        {
            (display, logo) = ReadManifest(path);
        }

        if (string.IsNullOrWhiteSpace(display) && startMap.TryGetValue(family, out var entry))
            display = entry.Name;

        startMap.TryGetValue(family, out var startEntry);

        return new AppPackage
        {
            DisplayName = display ?? "",
            IdName = idName,
            FullName = full,
            FamilyName = family,
            Publisher = PublisherDisplay(publisherDn),
            PublisherId = publisherDn,
            Version = version,
            Architecture = ArchText(ArchFromInt(arch)),
            InstallPath = path,
            InstalledDate = default,
            Description = "",
            LogoPath = logo ?? "",
            IsBundle = false,
            IsDevelopmentMode = dev,
            Kind = kind,
            Aumid = startEntry.Aumid ?? "",
        };
    }

    private static (string Display, string Logo) ReadManifest(string packagePath)
    {
        try
        {
            string manifest = Path.Combine(packagePath, "AppxManifest.xml");
            if (!File.Exists(manifest)) return (null, null);

            string text = File.ReadAllText(manifest);
            string display = null, logo = null;

            foreach (Match m in Regex.Matches(text, "<DisplayName>([^<]*)</DisplayName>"))
            {
                var value = m.Groups[1].Value.Trim();
                if (value.Length == 0) continue;
                if (value.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) continue;
                display = value;
                break;
            }

            var lm = Regex.Match(text, "<Logo>([^<]*)</Logo>");
            if (lm.Success)
            {
                var value = lm.Groups[1].Value.Trim();
                if (!value.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                {
                    var candidate = Path.Combine(packagePath, value.Replace('/', '\\'));
                    if (File.Exists(candidate)) logo = candidate;
                }
            }

            return (display, logo);
        }
        catch { return (null, null); }
    }

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";

    private static string FamilyFromFull(string full)
    {
        if (string.IsNullOrEmpty(full)) return "";
        var parts = full.Split('_');
        return parts.Length >= 2 ? parts[0] + "_" + parts[parts.Length - 1] : full;
    }

    private static string PublisherDisplay(string dn)
    {
        if (string.IsNullOrWhiteSpace(dn)) return "";
        var m = Regex.Match(dn, @"CN=(""([^""]*)""|([^,]*))");
        string cn = m.Success ? (m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value) : dn;
        cn = cn.Trim();
        if (cn.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)) return "Microsoft Corporation";
        return cn.Length == 0 ? dn : cn;
    }

    private static string ArchFromInt(int arch) => arch switch
    {
        0 => "X86",
        5 => "Arm",
        9 => "X64",
        11 => "Neutral",
        12 => "Arm64",
        14 => "X86OnArm64",
        _ => "Neutral",
    };

    // ---------------- 体积 ----------------
    private static void ComputeSizes(List<AppPackage> list, IProgress<LoadProgress> progress, CancellationToken token)
    {
        int total = list.Count;
        int done = 0;
        var options = new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = token };
        try
        {
            Parallel.ForEach(list, options, pkg =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(pkg.InstallPath) && Directory.Exists(pkg.InstallPath))
                        pkg.Size = DirSize(pkg.InstallPath);
                }
                catch { }
                progress?.Report(new LoadProgress(Interlocked.Increment(ref done), total));
            });
        }
        catch (OperationCanceledException) { }
    }

    public static long DirSize(string path)
    {
        long total = 0;
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false,
        };
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", options))
            {
                try { total += new FileInfo(file).Length; } catch { }
            }
        }
        catch { }
        return total;
    }

    // ---------------- 卸载 / 安装 ----------------
    public static async Task UninstallAsync(string fullName)
    {
        try
        {
            var pm = new PackageManager();
            var result = await pm.RemovePackageAsync(fullName, RemovalOptions.None);
            ThrowIfFailed(result, "卸载");
            return;
        }
        catch (AppxOperationException) { throw; }
        catch (Exception ex) when (IsAccessDenied(ex))
        {
            CompatMode = true;
        }
        catch (Exception ex) { throw AppxOperationException.From(ex, "卸载"); }

        string script = @"
$ErrorActionPreference = 'Stop'
try { Remove-AppxPackage -Package '" + Escape(fullName) + @"' -ErrorAction Stop; 'OK' }
catch { Write-Output ('ERR:' + $_.Exception.Message); exit 1 }
";
        RunShellCommand(script, "卸载");
    }

    public static async Task InstallAsync(string path)
    {
        try
        {
            var pm = new PackageManager();
            var result = await pm.AddPackageAsync(new Uri(path), null, DeploymentOptions.None);
            ThrowIfFailed(result, "安装");
            return;
        }
        catch (AppxOperationException) { throw; }
        catch (Exception ex) when (IsAccessDenied(ex))
        {
            CompatMode = true;
        }
        catch (Exception ex) { throw AppxOperationException.From(ex, "安装"); }

        string script = @"
$ErrorActionPreference = 'Stop'
try { Add-AppxPackage -Path '" + Escape(path) + @"' -ForceApplicationShutdown -ErrorAction Stop; 'OK' }
catch { Write-Output ('ERR:' + $_.Exception.Message); exit 1 }
";
        RunShellCommand(script, "安装");
    }

    private static void RunShellCommand(string script, string action)
    {
        var (code, stdout, stderr) = RunPowerShell(script, 300000);
        string outText = (stdout ?? "").Trim();
        if (code == 0 && outText.Contains("OK", StringComparison.Ordinal)) return;

        string detail = outText.StartsWith("ERR:", StringComparison.Ordinal) ? outText.Substring(4).Trim() : outText;
        if (string.IsNullOrWhiteSpace(detail)) detail = (stderr ?? "").Trim();
        if (string.IsNullOrWhiteSpace(detail)) detail = action + "失败";

        throw new AppxOperationException(-1, $"{action}失败", detail);
    }

    private static void ThrowIfFailed(DeploymentResult result, string action)
    {
        if (result == null) return;
        var err = result.ExtendedErrorCode;
        if (err != null && err.HResult != 0)
        {
            var mapped = AppxOperationException.From(err, action);
            throw new AppxOperationException(mapped.Code, mapped.Message,
                string.IsNullOrWhiteSpace(result.ErrorText) ? mapped.Hint : $"{mapped.Hint}\n{result.ErrorText}");
        }
    }

    // ---------------- 启动 / 定位 ----------------
    public static async Task<bool> LaunchAsync(AppPackage pkg)
    {
        if (pkg == null) return false;

        if (!string.IsNullOrEmpty(pkg.Aumid))
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + pkg.Aumid) { UseShellExecute = true });
                return true;
            }
            catch { }
        }

        if (CompatMode) return false;

        try
        {
            var pm = new PackageManager();
            var package = pm.FindPackageForUser("", pkg.FullName);
            if (package == null) return false;
            var entries = await package.GetAppListEntriesAsync();
            var entry = entries.FirstOrDefault();
            if (entry == null) return false;
            Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + entry.AppUserModelId) { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    public static void RevealInExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true }); }
        catch { }
    }

    // ---------------- 工具 ----------------
    private static (int Code, string Stdout, string Stderr) RunPowerShell(string script, int timeoutMs)
    {
        try
        {
            // 用 -EncodedCommand 传递脚本：某些受限目录下进程无法写入 %TEMP% 临时文件
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script ?? ""));

            var psi = new ProcessStartInfo("powershell.exe")
            {
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            };

            using var process = Process.Start(psi);
            if (process == null) return (-1, "", "无法启动 powershell.exe");

            var outTask = process.StandardOutput.ReadToEndAsync();
            var errTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(true); } catch { }
                return (-1, outTask.Result, "powershell 执行超时");
            }

            return (process.ExitCode, outTask.Result, errTask.Result);
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }

    private static string Escape(string text) => (text ?? "").Replace("'", "''");

    private static string Safe(Func<string> f)
    {
        try { return f() ?? ""; } catch { return ""; }
    }

    private static bool SafeBool(Func<bool> f)
    {
        try { return f(); } catch { return false; }
    }

    private static DateTimeOffset SafeDate(Func<DateTimeOffset> f)
    {
        try { return f(); } catch { return default; }
    }

    private static string ArchText(string arch) => arch switch
    {
        "X64" => "x64",
        "X86" => "x86",
        "Arm" => "ARM",
        "Arm64" => "ARM64",
        "Neutral" => "中性",
        "X86OnArm64" => "x86 (ARM64)",
        _ => string.IsNullOrEmpty(arch) ? "—" : arch,
    };

    public static string FormatSize(long? bytes)
    {
        if (bytes is null) return "计算中…";
        long b = bytes.Value;
        if (b <= 0) return "0 KB";
        if (b < 1024L * 1024L) return $"{b / 1024.0:0.#} KB";
        if (b < 1024L * 1024L * 1024L) return $"{b / (1024.0 * 1024.0):0.#} MB";
        return $"{b / (1024.0 * 1024.0 * 1024.0):0.##} GB";
    }
}

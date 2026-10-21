using System;

namespace UwpManager;

public enum PackageKind
{
    App = 0,
    Framework = 1,
    Resource = 2,
    System = 3,
}

public sealed class AppPackage
{
    public string DisplayName { get; init; } = "";
    public string IdName { get; init; } = "";
    public string FullName { get; init; } = "";
    public string FamilyName { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string PublisherId { get; init; } = "";
    public Version Version { get; init; } = new Version(0, 0, 0, 0);
    public string Architecture { get; init; } = "";
    public string InstallPath { get; init; } = "";
    public DateTimeOffset InstalledDate { get; init; }
    public string Description { get; init; } = "";
    public string LogoPath { get; init; } = "";
    public bool IsBundle { get; init; }
    public bool IsDevelopmentMode { get; init; }
    public PackageKind Kind { get; init; }
    public string Aumid { get; init; } = "";
    public long? Size { get; set; }

    public string Name
    {
        get
        {
            var name = DisplayName;
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) || LooksLikeGuid(name))
                name = IdName;
            if (string.IsNullOrWhiteSpace(name)) name = FullName;
            return name;
        }
    }

    private static bool LooksLikeGuid(string s)
    {
        if (s.Length < 32) return false;
        int dashes = 0;
        foreach (var c in s) if (c == '-') dashes++;
        return dashes >= 3 && Guid.TryParse(s, out _);
    }

    public string VersionText => Version.Revision > 0
        ? $"{Version.Major}.{Version.Minor}.{Version.Build}.{Version.Revision}"
        : $"{Version.Major}.{Version.Minor}.{Version.Build}";

    public string SizeText => AppxService.FormatSize(Size);

    public string InstallDateText => InstalledDate == default ? "—" : InstalledDate.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string Subtitle
    {
        get
        {
            var p = string.IsNullOrWhiteSpace(Publisher) ? "未知发布者" : Publisher;
            return $"{p} · v{VersionText}";
        }
    }

    public string KindText => Kind switch
    {
        PackageKind.Framework => "框架包",
        PackageKind.Resource => "资源包",
        PackageKind.System => "系统组件",
        _ => "应用",
    };

    public string SearchBlob =>
        (Name + " " + IdName + " " + Publisher + " " + FullName + " " + PublisherId).ToLowerInvariant();

    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var q = query.Trim().ToLowerInvariant();
        foreach (var token in q.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (!SearchBlob.Contains(token, StringComparison.Ordinal)) return false;
        return true;
    }
}

public readonly record struct LoadProgress(int Done, int Total);

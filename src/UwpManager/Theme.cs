using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using Microsoft.Win32;

namespace UwpManager;

/// <summary>Fluent Design 视觉规范：颜色、字体、绘制辅助。</summary>
internal static class Theme
{
    public static bool Dark { get; private set; }

    public static event Action Changed;

    private static readonly bool HasVarText = HasFamily("Segoe UI Variable Text");
    private static readonly bool HasVarDisplay = HasFamily("Segoe UI Variable Display");
    private static readonly bool HasFluentIcons = HasFamily("Segoe Fluent Icons");

    public static readonly Font Body = Make(HasVarText ? "Segoe UI Variable Text" : "Segoe UI", 10.5f, FontStyle.Regular);
    public static readonly Font BodyStrong = Make(HasVarText ? "Segoe UI Variable Text" : "Segoe UI", 10.5f, FontStyle.Bold);
    public static readonly Font Small = Make(HasVarText ? "Segoe UI Variable Text" : "Segoe UI", 9f, FontStyle.Regular);
    public static readonly Font SmallStrong = Make(HasVarText ? "Segoe UI Variable Text" : "Segoe UI", 9f, FontStyle.Bold);
    public static readonly Font Tiny = Make(HasVarText ? "Segoe UI Variable Text" : "Segoe UI", 8.25f, FontStyle.Regular);
    public static readonly Font Subtitle = Make(HasVarDisplay ? "Segoe UI Variable Display" : "Segoe UI", 15f, FontStyle.Bold);
    public static readonly Font Title = Make(HasVarDisplay ? "Segoe UI Variable Display" : "Segoe UI", 20f, FontStyle.Bold);
    public static readonly Font DialogTitle = Make(HasVarDisplay ? "Segoe UI Variable Display" : "Segoe UI", 13.5f, FontStyle.Bold);

    public static readonly Font GlyphSm = MakeGlyph(9.5f);
    public static readonly Font Glyph = MakeGlyph(11.5f);
    public static readonly Font GlyphLg = MakeGlyph(14f);
    public static readonly Font GlyphXl = MakeGlyph(20f);
    public static readonly Font GlyphHuge = MakeGlyph(30f);

    public static Color Accent { get; private set; } = Color.FromArgb(0x00, 0x67, 0xC0);

    // ---------- 调色板 ----------
    private static Color C(int r, int g, int b) => Color.FromArgb(r, g, b);

    public static Color AppBg => Dark ? C(0x1F, 0x1F, 0x1F) : C(0xF3, 0xF3, 0xF3);
    public static Color Surface => Dark ? C(0x2A, 0x2A, 0x2A) : Color.White;
    public static Color SurfaceAlt => Dark ? C(0x25, 0x25, 0x25) : C(0xFA, 0xFA, 0xFA);
    public static Color Stroke => Dark ? C(0x3A, 0x3A, 0x3A) : C(0xE2, 0xE2, 0xE2);
    public static Color StrokeSoft => Dark ? C(0x33, 0x33, 0x33) : C(0xED, 0xED, 0xED);
    public static Color Text => Dark ? Color.White : C(0x1A, 0x1A, 0x1A);
    public static Color Text2 => Dark ? C(0xC7, 0xC7, 0xC7) : C(0x5B, 0x5B, 0x5B);
    public static Color Text3 => Dark ? C(0x91, 0x91, 0x91) : C(0x8A, 0x8A, 0x8A);
    public static Color Hover => Dark ? C(0x32, 0x32, 0x32) : C(0xEA, 0xEA, 0xEA);
    public static Color Pressed => Dark ? C(0x3B, 0x3B, 0x3B) : C(0xE0, 0xE0, 0xE0);
    public static Color Danger => Dark ? C(0xFF, 0x99, 0xA4) : C(0xC4, 0x2B, 0x1C);
    public static Color DangerSolid => C(0xC4, 0x2B, 0x1C);
    public static Color DangerSolidHover => C(0xAF, 0x27, 0x1A);
    public static Color DangerSolidPressed => C(0x9B, 0x23, 0x17);
    public static Color DangerFill => Dark ? C(0x44, 0x22, 0x24) : C(0xFD, 0xE7, 0xE9);
    public static Color Warning => Dark ? C(0xFC, 0xE1, 0x00) : C(0x9D, 0x5D, 0x00);

    public static Color AccentHover => Blend(Accent, Dark ? Color.White : Color.Black, 0.13f);
    public static Color AccentPressed => Blend(Accent, Dark ? Color.White : Color.Black, 0.24f);
    public static Color AccentText => Luminance(Accent) > 0.62f ? Color.Black : Color.White;
    public static Color AccentSoft => Blend(Accent, AppBg, 0.86f);
    public static Color AccentSoftStrong => Blend(Accent, AppBg, 0.74f);
    public static Color SelectedFill => Blend(Accent, AppBg, 0.88f);
    public static Color SelectedFillHover => Blend(Accent, AppBg, 0.82f);

    // ---------- 初始化 ----------
    public static void Init()
    {
        Dark = !IsLightTheme();
        Accent = ReadAccent();
    }

    public static void ToggleDark() => SetDark(!Dark);

    public static void SetDark(bool dark)
    {
        if (Dark == dark) return;
        Dark = dark;
        Changed?.Invoke();
    }

    public static void RefreshAccent()
    {
        var a = ReadAccent();
        if (a != Accent) { Accent = a; Changed?.Invoke(); }
    }

    private static bool IsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int v) return v != 0;
        }
        catch { }
        return true;
    }

    private static Color ReadAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int v && v != unchecked((int)0xFF000000))
            {
                int c = v;
                var col = Color.FromArgb(c & 0xFF, (c >> 8) & 0xFF, (c >> 16) & 0xFF);
                if (col.GetSaturation() > 0.15f) return col;
            }
        }
        catch { }
        return Color.FromArgb(0x00, 0x67, 0xC0);
    }

    // ---------- 绘制辅助 ----------
    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        if (r.Width <= 0 || r.Height <= 0) return path;
        float d = Math.Max(0.1f, Math.Min(radius, Math.Min(r.Width, r.Height) / 2f)) * 2f;
        path.AddArc(r.X, r.Y, d, d, 180f, 90f);
        path.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
        path.AddArc(r.X, r.Bottom - d, d, d, 90f, 90f);
        path.CloseFigure();
        return path;
    }

    public static void FillRound(Graphics g, RectangleF r, float radius, Color color)
    {
        using var path = Round(r, radius);
        using var brush = new SolidBrush(color);
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillPath(brush, path);
        g.SmoothingMode = old;
    }

    public static void DrawRound(Graphics g, RectangleF r, float radius, Color color, float width = 1f)
    {
        using var path = Round(r, radius);
        using var pen = new Pen(color, width);
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawPath(pen, path);
        g.SmoothingMode = old;
    }

    public static Color Blend(Color a, Color b, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)(a.A + (b.A - a.A) * amount),
            (int)(a.R + (b.R - a.R) * amount),
            (int)(a.G + (b.G - a.G) * amount),
            (int)(a.B + (b.B - a.B) * amount));
    }

    public static float Luminance(Color c) => (0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B) / 255f;

    public const TextFormatFlags SingleLineLeft =
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
        TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

    public const TextFormatFlags SingleLineRight =
        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
        TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

    public const TextFormatFlags Wrap =
        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;

    public static void DrawText(Graphics g, string text, Font font, Color color, Rectangle bounds, TextFormatFlags flags)
        => TextRenderer.DrawText(g, text, font, bounds, color, flags);

    public static void DrawText(Graphics g, string text, Font font, Color color, Rectangle bounds)
        => TextRenderer.DrawText(g, text, font, bounds, color, SingleLineLeft);

    public static void GlyphDraw(Graphics g, string glyph, Font font, Color color, Rectangle bounds)
    {
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(g, glyph, font, bounds, color, flags);
    }

    public static int Dip(this Control c, double value) => (int)Math.Round(value * c.DeviceDpi / 96.0);

    private static bool HasFamily(string name)
    {
        try
        {
            return FontFamily.Families.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private static Font Make(string family, float size, FontStyle style)
    {
        try { return new Font(family, size, style, GraphicsUnit.Point); }
        catch { return new Font("Segoe UI", size, style, GraphicsUnit.Point); }
    }

    private static Font MakeGlyph(float size)
    {
        try
        {
            return new Font(HasFluentIcons ? "Segoe Fluent Icons" : "Segoe MDL2 Assets", size, FontStyle.Regular, GraphicsUnit.Point);
        }
        catch { return new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Point); }
    }
}

internal static class Glyphs
{
    public const string Search = "\uE721";
    public const string Add = "\uE710";
    public const string Refresh = "\uE72C";
    public const string Delete = "\uE74D";
    public const string OpenFolder = "\uE838";
    public const string Copy = "\uE8C8";
    public const string Favorite = "\uE734";
    public const string AppList = "\uE71D";
    public const string AllApps = "\uE71D";
    public const string Grid = "\uF0E2";
    public const string Settings = "\uE713";
    public const string Info = "\uE946";
    public const string Moon = "\uE708";
    public const string Sunny = "\uE706";
    public const string Package = "\uE7B8";
    public const string Shield = "\uE83D";
    public const string Close = "\uE8BB";
    public const string Minimize = "\uE921";
    public const string Maximize = "\uE922";
    public const string Restore = "\uE923";
    public const string ChevronRight = "\uE76C";
    public const string Check = "\uE73E";
    public const string Warning = "\uE7BA";
    public const string Error = "\uEA39";
    public const string Play = "\uE768";
    public const string Clock = "\uE823";
    public const string Storage = "\uEDA2";
    public const string Sort = "\uE8CB";
    public const string Apps = "\uECAA";
    public const string Blocks = "\uE8F1";
    public const string Library = "\uE8F1";
    public const string Windows = "\uE782";
}

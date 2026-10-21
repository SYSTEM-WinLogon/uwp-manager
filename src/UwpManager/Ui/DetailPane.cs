using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace UwpManager.Ui;

/// <summary>右侧详情卡片：包信息、复制、打开、卸载。</summary>
public class DetailPane : FluentControl
{
    private AppPackage _package;
    private Bitmap _icon;
    private string _iconKey;
    private int _hoverAction = -1;

    private Rectangle _openRect;
    private Rectangle _uninstallRect;
    private Rectangle _revealRect;
    private readonly List<(Rectangle Rect, string Action, string Payload)> _hitAreas = new();

    public event EventHandler OpenRequested;
    public event EventHandler UninstallRequested;
    public event EventHandler<string> CopyRequested;
    public event EventHandler<string> RevealRequested;

    public DetailPane()
    {
        BackColor = Theme.AppBg;
    }

    public AppPackage Package
    {
        get => _package;
        set
        {
            if (ReferenceEquals(_package, value)) return;
            _package = value;
            LoadIcon();
            Invalidate();
        }
    }

    public bool CanUninstall => _package != null;

    private void LoadIcon()
    {
        if (_icon != null)
        {
            _icon.Dispose();
            _icon = null;
        }
        if (_package == null) return;
        if (string.Equals(_iconKey, _package.FullName, StringComparison.Ordinal) && _icon != null) return;

        try
        {
            if (!string.IsNullOrEmpty(_package.LogoPath) && File.Exists(_package.LogoPath))
            {
                using var src = Image.FromFile(_package.LogoPath);
                int s = U(64);
                _icon = new Bitmap(s, s);
                using var g = Graphics.FromImage(_icon);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float scale = Math.Min((float)s / src.Width, (float)s / src.Height);
                int w = Math.Max(1, (int)(src.Width * scale));
                int h = Math.Max(1, (int)(src.Height * scale));
                g.DrawImage(src, new Rectangle((s - w) / 2, (s - h) / 2, w, h));
            }
        }
        catch
        {
            if (_icon != null) { _icon.Dispose(); _icon = null; }
        }
        _iconKey = _package.FullName;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int hover = -1;
        for (int i = 0; i < _hitAreas.Count; i++)
            if (_hitAreas[i].Rect.Contains(e.Location)) { hover = i; break; }
        if (hover != _hoverAction)
        {
            _hoverAction = hover;
            Cursor = hover >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverAction = -1;
        Cursor = Cursors.Default;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        for (int i = 0; i < _hitAreas.Count; i++)
        {
            var area = _hitAreas[i];
            if (!area.Rect.Contains(e.Location)) continue;
            switch (area.Action)
            {
                case "open": OpenRequested?.Invoke(this, EventArgs.Empty); break;
                case "uninstall":
                    if (CanUninstall) UninstallRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case "copy": CopyRequested?.Invoke(this, area.Payload); break;
                case "reveal": RevealRequested?.Invoke(this, area.Payload); break;
            }
            return;
        }
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackgroundColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        _hitAreas.Clear();

        var card = new RectangleF(0, 0, Width - 1, Height - 1);
        Theme.FillRound(g, card, U(10), Theme.Surface);
        Theme.DrawRound(g, card, U(10), Theme.Stroke);

        if (_package == null)
        {
            DrawEmpty(g);
            _openRect = Rectangle.Empty;
            _uninstallRect = Rectangle.Empty;
            _revealRect = Rectangle.Empty;
            return;
        }

        int pad = U(20);
        int iconSize = U(64);

        int badgeH = U(20);
        int nameTop = pad;
        int nameWidth = Width - pad * 2 - iconSize - U(16);

        var nameRect = new Rectangle(pad + iconSize + U(16), nameTop + U(4), nameWidth, U(26));
        TextRenderer.DrawText(g, _package.Name, Theme.Subtitle, nameRect, Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        var pubRect = new Rectangle(pad + iconSize + U(16), nameTop + U(32), nameWidth, U(18));
        TextRenderer.DrawText(g, string.IsNullOrWhiteSpace(_package.Publisher) ? "未知发布者" : _package.Publisher,
            Theme.Small, pubRect, Theme.Text3,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        // 类型徽章
        var badgeRect = new Rectangle(pad + iconSize + U(16), nameTop + U(52), U(64), badgeH);
        DrawChip(g, badgeRect, _package.KindText,
            _package.Kind == PackageKind.App ? Theme.AccentSoftStrong : Theme.Hover,
            _package.Kind == PackageKind.App ? Theme.Accent : Theme.Text2);

        var iconRect = new Rectangle(pad, pad, iconSize, iconSize);
        if (_icon != null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(_icon, iconRect);
        }
        else
        {
            Theme.FillRound(g, iconRect, U(12), Theme.AccentSoft);
            Theme.GlyphDraw(g, Glyphs.Apps, Theme.GlyphXl, Theme.Accent, iconRect);
        }

        int y = pad + iconSize + U(18);
        g.DrawLine(new Pen(Theme.StrokeSoft, 1f), pad, y, Width - pad, y);

        y += U(12);
        AddField(g, pad, ref y, "版本号", _package.VersionText, null);
        AddField(g, pad, ref y, "占用空间", _package.SizeText, null);
        AddField(g, pad, ref y, "体系结构", _package.Architecture ?? "—", null);
        AddField(g, pad, ref y, "安装日期", _package.InstallDateText, null);
        AddField(g, pad, ref y, "包全名（PackageFullName）", _package.FullName, _package.FullName);
        AddField(g, pad, ref y, "包家族名（PackageFamilyName）", _package.FamilyName, _package.FamilyName);
        AddField(g, pad, ref y, "安装位置", _package.InstallPath, _package.InstallPath, true);

        // 底部操作按钮
        int btnH = U(34);
        int btnY = Height - pad - btnH;
        int btnW = U(88);

        _uninstallRect = new Rectangle(pad, btnY, btnW, btnH);
        DrawButton(g, _uninstallRect, "卸载", ButtonVariant.Danger, CanUninstall, CanUninstall ? "uninstall" : null);

        _openRect = new Rectangle(pad + btnW + U(8), btnY, btnW, btnH);
        DrawButton(g, _openRect, "打开", ButtonVariant.Accent, true, "open");

        _revealRect = new Rectangle(pad + btnW * 2 + U(16), btnY, btnH, btnH);
        bool revealHover = _hoverAction >= 0 && _hitAreas.Exists(a => a.Rect == _revealRect);
        Theme.FillRound(g, _revealRect, U(6), revealHover ? Theme.Hover : Color.Transparent);
        Theme.GlyphDraw(g, Glyphs.OpenFolder, Theme.GlyphSm, Theme.Text2, _revealRect);
        _hitAreas.Add((_revealRect, "reveal", _package.InstallPath));

        if (_package.Kind != PackageKind.App)
        {
            var note = new Rectangle(pad, btnY - U(24), Width - pad * 2, U(18));
            TextRenderer.DrawText(g, "系统内置组件：卸载需二次确认，可能导致功能异常", Theme.Tiny, note, Theme.Danger,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    private void DrawEmpty(Graphics g)
    {
        var iconRect = new Rectangle(0, Height / 2 - U(80), Width, U(64));
        Theme.GlyphDraw(g, Glyphs.Info, Theme.GlyphHuge, Theme.Blend(Theme.Text3, Theme.Surface, 0.55f), iconRect);

        var t1 = new Rectangle(0, Height / 2 - U(4), Width, U(26));
        TextRenderer.DrawText(g, "未选择应用", Theme.BodyStrong, t1, Theme.Text2,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.SingleLine);

        var t2 = new Rectangle(U(30), Height / 2 + U(24), Width - U(60), U(40));
        TextRenderer.DrawText(g, "在左侧列表中选择一个 UWP 应用，即可查看包全名、版本号、占用空间与发布者。", Theme.Small, t2,
            Theme.Text3, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak);
    }

    private void AddField(Graphics g, int pad, ref int y, string label, string value, string copyPayload, bool reveal = false)
    {
        int width = Width - pad * 2;
        var labelRect = new Rectangle(pad, y, width, U(15));
        TextRenderer.DrawText(g, label, Theme.Tiny, labelRect, Theme.Text3,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

        int valueWidth = width - (copyPayload != null ? U(26) : 0);
        var valueRect = new Rectangle(pad, y + U(15), valueWidth, U(20));
        TextRenderer.DrawText(g, string.IsNullOrEmpty(value) ? "—" : value, Theme.Body, valueRect, Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        if (copyPayload != null)
        {
            var copyRect = new Rectangle(pad + width - U(24), y + U(13), U(24), U(22));
            bool hover = _hoverAction >= 0 && _hitAreas.Exists(a => a.Rect == copyRect);
            if (hover) Theme.FillRound(g, copyRect, U(4), Theme.Hover);
            Theme.GlyphDraw(g, Glyphs.Copy, Theme.GlyphSm, hover ? Theme.Text : Theme.Text3, copyRect);
            _hitAreas.Add((copyRect, "copy", copyPayload));
        }

        y += U(48);
    }

    private void DrawButton(Graphics g, Rectangle rect, string text, ButtonVariant variant, bool enabled, string action)
    {
        bool hover = enabled && action != null && _hoverAction >= 0 && _hitAreas.Exists(a => a.Rect == rect);
        var r = new RectangleF(rect.X, rect.Y, rect.Width - 1, rect.Height - 1);

        Color fill, fg;
        if (!enabled)
        {
            fill = Theme.Hover;
            fg = Theme.Text3;
        }
        else if (variant == ButtonVariant.Accent)
        {
            fill = hover ? Theme.AccentHover : Theme.Accent;
            fg = Theme.AccentText;
        }
        else
        {
            fill = hover ? Theme.DangerSolidHover : Theme.DangerSolid;
            fg = Color.White;
        }

        Theme.FillRound(g, r, U(6), fill);
        TextRenderer.DrawText(g, text, Theme.Body, rect, fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

        if (enabled && action != null) _hitAreas.Add((rect, action, null));
    }

    private static void DrawChip(Graphics g, Rectangle rect, string text, Color bg, Color fg)
    {
        Theme.FillRound(g, new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), 4, bg);
        TextRenderer.DrawText(g, text, Theme.Tiny, rect, fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _icon?.Dispose();
        base.Dispose(disposing);
    }
}

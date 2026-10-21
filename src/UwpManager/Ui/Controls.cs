using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace UwpManager.Ui;

/// <summary>所有自绘控件的基类，提供 DIP 换算。</summary>
public class FluentControl : Control
{
    public FluentControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        Font = Theme.Body;
        ForeColor = Theme.Text;
        DoubleBuffered = true;
        Theme.Changed += OnThemeInvalidate;
    }

    public int U(double dip) => (int)Math.Round(dip * DeviceDpi / 96.0);

    private void OnThemeInvalidate()
    {
        if (IsDisposed) return;
        ForeColor = Theme.Text;
        Invalidate(true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Theme.Changed -= OnThemeInvalidate;
        base.Dispose(disposing);
    }

    protected Color BackgroundColor => BackColor.A == 0 ? Theme.AppBg : BackColor;
}

public enum ButtonVariant
{
    Standard,
    Accent,
    Subtle,
    Danger,
    DangerSubtle,
}

public class FluentButton : FluentControl
{
    private bool _hover;
    private bool _pressed;

    public ButtonVariant Variant { get; set; } = ButtonVariant.Standard;
    public string Glyph { get; set; } = "";
    public int CornerRadius { get; set; } = 6;
    public int GlyphSize { get; set; } = 0;

    public FluentButton()
    {
        Cursor = Cursors.Hand;
        Height = 34;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackgroundColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        int rad = U(CornerRadius);

        Color fill, text, border = Color.Transparent;
        bool hasBorder = false;

        if (!Enabled)
        {
            fill = Theme.Hover;
            text = Theme.Text3;
        }
        else
        {
            switch (Variant)
            {
                case ButtonVariant.Accent:
                    fill = _pressed ? Theme.AccentPressed : _hover ? Theme.AccentHover : Theme.Accent;
                    text = Theme.AccentText;
                    break;
                case ButtonVariant.Danger:
                    fill = _pressed ? Theme.DangerSolidPressed : _hover ? Theme.DangerSolidHover : Theme.DangerSolid;
                    text = Color.White;
                    break;
                case ButtonVariant.DangerSubtle:
                    fill = _pressed ? Theme.Pressed : _hover ? Theme.DangerFill : Color.Transparent;
                    text = Theme.Danger;
                    break;
                case ButtonVariant.Subtle:
                    fill = _pressed ? Theme.Pressed : _hover ? Theme.Hover : Color.Transparent;
                    text = Theme.Text2;
                    break;
                default:
                    fill = _pressed ? Theme.Pressed : _hover ? Theme.Hover : Theme.Surface;
                    text = Theme.Text;
                    border = Theme.Stroke;
                    hasBorder = true;
                    break;
            }
        }

        if (fill.A > 0) Theme.FillRound(g, r, rad, fill);
        if (hasBorder) Theme.DrawRound(g, r, rad, border);

        if (!string.IsNullOrEmpty(Glyph))
        {
            var glyphFont = GlyphSize > 0 ? new Font(Theme.Glyph.FontFamily, GlyphSize, FontStyle.Regular, GraphicsUnit.Point) : Theme.GlyphSm;
            int gw = U(18);
            var measured = string.IsNullOrEmpty(Text)
                ? Size.Empty
                : TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            int total = gw + (measured.IsEmpty ? 0 : measured.Width + U(6));
            int x = (Width - total) / 2;
            Theme.GlyphDraw(g, Glyph, glyphFont, text, new Rectangle(x, 0, gw, Height));
            if (!measured.IsEmpty)
            {
                var textRect = new Rectangle(x + gw + U(6), 0, Width - (x + gw + U(6)), Height);
                TextRenderer.DrawText(g, Text, Font, textRect, text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
            if (GlyphSize > 0) glyphFont.Dispose();
        }
        else
        {
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, text, flags);
        }
    }
}

/// <summary>窗口右上角的最小化 / 最大化 / 关闭按钮。</summary>
public class WindowButtons : FluentControl
{
    private int _hover = -1;

    public Form Host { get; set; }

    public WindowButtons()
    {
        BackColor = Color.Transparent;
        Cursor = Cursors.Arrow;
        Height = 44;
        Width = 138;
    }

    private int ButtonWidth => U(46);

    private int HitTest(Point p)
    {
        if (p.Y < 0 || p.Y > Height) return -1;
        int i = p.X / ButtonWidth;
        return i < 0 || i > 2 ? -1 : i;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int i = HitTest(e.Location);
        if (i != _hover)
        {
            _hover = i;
            Invalidate();
        }
        Cursor = i >= 0 ? Cursors.Hand : Cursors.Arrow;
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (Host == null) { base.OnMouseUp(e); return; }
        int i = HitTest(e.Location);
        if (i == 0) Host.WindowState = FormWindowState.Minimized;
        else if (i == 1) Host.WindowState = Host.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        else if (i == 2) Host.Close();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackgroundColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        bool maximized = Host?.WindowState == FormWindowState.Maximized;
        string[] glyphs = { Glyphs.Minimize, maximized ? Glyphs.Restore : Glyphs.Maximize, Glyphs.Close };

        for (int i = 0; i < 3; i++)
        {
            var cell = new Rectangle(i * ButtonWidth, 0, ButtonWidth, Height);
            if (_hover == i)
            {
                var fill = i == 2 ? Color.FromArgb(0xC4, 0x2B, 0x1C) : Theme.Hover;
                Theme.FillRound(g, new RectangleF(cell.X + U(2), cell.Y + U(4), cell.Width - U(4), cell.Height - U(8)), U(6), fill);
            }
            var color = _hover == i && i == 2 ? Color.White : Theme.Text2;
            Theme.GlyphDraw(g, glyphs[i], Theme.GlyphSm, color, cell);
        }
    }
}

public class SearchBox : FluentControl
{
    private readonly TextBox _box = new TextBox();
    private bool _focused;

    public event EventHandler QueryChanged;

    private string _placeholder = "搜索名称、包全名或发布者";

    public string Placeholder
    {
        get => _placeholder;
        set
        {
            _placeholder = value ?? "";
            ApplyCue();
        }
    }

    public string Query
    {
        get => _box.Text;
        set => _box.Text = value;
    }

    public SearchBox()
    {
        Height = 36;
        _box.BorderStyle = BorderStyle.None;
        _box.Font = Theme.Body;
        _box.BackColor = Theme.Surface;
        _box.ForeColor = Theme.Text;
        _box.TextChanged += (_, _) => { Invalidate(); QueryChanged?.Invoke(this, EventArgs.Empty); };
        _box.GotFocus += (_, _) => { _focused = true; Invalidate(); };
        _box.LostFocus += (_, _) => { _focused = false; Invalidate(); };
        _box.HandleCreated += (_, _) => ApplyCue();
        Controls.Add(_box);
        Theme.Changed += SyncColors;
    }

    private void ApplyCue()
    {
        if (_box != null && _box.IsHandleCreated)
            Native.SendMessage(_box.Handle, Native.EM_SETCUEBANNER, (IntPtr)1, _placeholder ?? "");
    }

    public void FocusInput()
    {
        _box.Focus();
        _box.SelectionStart = _box.TextLength;
    }

    private void SyncColors()
    {
        if (IsDisposed) return;
        _box.BackColor = Theme.Surface;
        _box.ForeColor = Theme.Text;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        int x = U(34);
        _box.SetBounds(x, (Height - _box.PreferredHeight) / 2 + U(1), Math.Max(U(10), Width - x - U(12)), _box.PreferredHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackgroundColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        Theme.FillRound(g, r, U(6), Theme.Surface);
        Theme.DrawRound(g, r, U(6), _focused ? Theme.Accent : Theme.Stroke, _focused ? 1.6f : 1f);
        if (_focused)
        {
            var underline = new RectangleF(U(6), Height - U(5), Width - U(12), U(2));
            Theme.FillRound(g, underline, U(1), Theme.Accent);
        }

        Theme.GlyphDraw(g, Glyphs.Search, Theme.GlyphSm, Theme.Text3, new Rectangle(U(10), 0, U(18), Height));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Theme.Changed -= SyncColors;
        base.Dispose(disposing);
    }
}

public sealed class NavItem
{
    public string Glyph { get; init; } = "";
    public string Title { get; init; } = "";
    public int Count { get; set; }
    public object Tag { get; init; }
}

public class NavList : FluentControl
{
    private int _hover = -1;
    private int _selected;

    public List<NavItem> Items { get; } = new();
    public int ItemHeight { get; set; } = 40;
    public event EventHandler SelectionChanged;

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (value < 0 || value >= Items.Count || value == _selected) return;
            _selected = value;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public NavList()
    {
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
    }

    private int IndexAt(Point p)
    {
        int h = U(ItemHeight);
        int i = p.Y / h;
        return i >= 0 && i < Items.Count ? i : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int i = IndexAt(e.Location);
        if (i != _hover) { _hover = i; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        int i = IndexAt(e.Location);
        if (i >= 0) SelectedIndex = i;
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackgroundColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int h = U(ItemHeight);
        for (int i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            var rect = new RectangleF(U(4), i * h + U(2), Width - U(8), h - U(4));
            bool selected = i == _selected;

            if (selected)
                Theme.FillRound(g, rect, U(6), Theme.SelectedFill);
            else if (i == _hover)
                Theme.FillRound(g, rect, U(6), Theme.Hover);

            if (selected)
                Theme.FillRound(g, new RectangleF(U(4), rect.Y + (rect.Height - U(16)) / 2, U(3), U(16)), U(1.5f), Theme.Accent);

            var fg = selected ? Theme.Text : Theme.Text2;
            Theme.GlyphDraw(g, item.Glyph, Theme.GlyphSm, selected ? Theme.Accent : Theme.Text3,
                new Rectangle((int)rect.X + U(12), (int)rect.Y, U(20), (int)rect.Height));

            var textRect = new Rectangle((int)rect.X + U(38), (int)rect.Y, (int)rect.Width - U(38) - U(44), (int)rect.Height);
            TextRenderer.DrawText(g, item.Title, selected ? Theme.BodyStrong : Theme.Body, textRect, fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

            if (item.Count > 0)
            {
                var countRect = new Rectangle((int)rect.Right - U(40), (int)rect.Y, U(30), (int)rect.Height);
                TextRenderer.DrawText(g, item.Count.ToString(), Theme.Small, countRect,
                    selected ? Theme.Accent : Theme.Text3,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
        }
    }
}

/// <summary>分段选择器（排序方式）。</summary>
public class Segmented : FluentControl
{
    private int _hover = -1;
    private int _selected;

    public string[] Options { get; set; } = Array.Empty<string>();
    public event EventHandler SelectionChanged;

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (value < 0 || value >= Options.Length || value == _selected) return;
            _selected = value;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Segmented()
    {
        Height = 34;
        Cursor = Cursors.Hand;
    }

    private int IndexAt(Point p)
    {
        if (Options.Length == 0) return -1;
        int w = Width / Options.Length;
        int i = p.X / w;
        return i < 0 || i >= Options.Length ? -1 : i;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int i = IndexAt(e.Location);
        if (i != _hover) { _hover = i; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (IndexAt(e.Location) >= 0) SelectedIndex = IndexAt(e.Location);
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackgroundColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        Theme.FillRound(g, r, U(6), Theme.AppBg);
        Theme.DrawRound(g, r, U(6), Theme.Stroke);

        if (Options.Length == 0) return;
        int w = (Width - U(6)) / Options.Length;
        for (int i = 0; i < Options.Length; i++)
        {
            var cell = new RectangleF(U(3) + i * w, U(3), w, Height - U(6));
            if (i == _selected)
            {
                Theme.FillRound(g, cell, U(4), Theme.Surface);
                Theme.DrawRound(g, cell, U(4), Theme.Stroke);
            }
            else if (i == _hover)
            {
                Theme.FillRound(g, cell, U(4), Theme.Hover);
            }
            TextRenderer.DrawText(g, Options[i], i == _selected ? Theme.SmallStrong : Theme.Small,
                Rectangle.Round(cell), i == _selected ? Theme.Text : Theme.Text2,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }
}

public class ToggleSwitch : FluentControl
{
    public event EventHandler Toggled;

    private bool _checked;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            Toggled?.Invoke(this, EventArgs.Empty);
        }
    }

    public ToggleSwitch()
    {
        Size = new Size(40, 20);
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        Checked = !Checked;
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackgroundColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int h = Height;
        int w = Width;
        var track = new RectangleF(0, 0, w - 1, h - 1);
        Theme.FillRound(g, track, h / 2f, _checked ? Theme.Accent : Theme.Stroke);

        int knob = h - U(6);
        float kx = _checked ? w - knob - U(3) : U(3);
        Theme.FillRound(g, new RectangleF(kx, U(3), knob, knob), knob / 2f,
            _checked ? Theme.AccentText : Theme.Text3);
    }
}

/// <summary>多选后出现的操作条：批量卸载 / 取消选择。</summary>
public sealed class SelectionBar : FluentControl
{
    private readonly FluentButton _delete = new();
    private readonly FluentButton _clear = new();
    private int _count;

    public event EventHandler DeleteRequested;
    public event EventHandler ClearRequested;

    public SelectionBar()
    {
        BackColor = Theme.AppBg;
        Size = new Size(430, 54);

        _delete.Text = "批量卸载";
        _delete.Variant = ButtonVariant.Danger;
        _delete.BackColor = Theme.Surface;
        _delete.Click += (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty);

        _clear.Text = "取消选择";
        _clear.Variant = ButtonVariant.Standard;
        _clear.BackColor = Theme.Surface;
        _clear.Click += (_, _) => ClearRequested?.Invoke(this, EventArgs.Empty);

        Controls.Add(_delete);
        Controls.Add(_clear);
        LayoutItems();
    }

    public int Count
    {
        get => _count;
        set { _count = value; Invalidate(); }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutItems();
    }

    private void LayoutItems()
    {
        int pad = U(10);
        int h = U(34);
        int clearW = U(96);
        int deleteW = U(112);
        _clear.Size = new Size(clearW, h);
        _delete.Size = new Size(deleteW, h);
        _clear.Location = new Point(Width - pad - clearW, (Height - h) / 2);
        _delete.Location = new Point(_clear.Left - U(8) - deleteW, (Height - h) / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackgroundColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var card = new RectangleF(0, 0, Width - 1, Height - 1);
        Theme.FillRound(g, card, U(10), Theme.Surface);
        Theme.DrawRound(g, card, U(10), Theme.Stroke);
        Theme.FillRound(g, new RectangleF(U(14), (Height - U(22)) / 2f, U(3), U(22)), U(1.5f), Theme.Accent);

        var label = new Rectangle(U(26), 0, Math.Max(U(40), Width - U(26) - U(230)), Height);
        TextRenderer.DrawText(g, $"已选择 {_count} 项", Theme.BodyStrong, label, Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace UwpManager.Ui;

/// <summary>已安装 UWP 应用列表（虚拟绘制）。</summary>
public class PackageList : FluentControl
{
    private readonly List<AppPackage> _items = new();
    private readonly Dictionary<string, Bitmap> _icons = new(StringComparer.OrdinalIgnoreCase);

    private int _scroll;
    private int _hover = -1;
    private int _selected = -1;
    private bool _draggingThumb;
    private int _dragStartY;
    private int _dragStartScroll;

    private readonly HashSet<string> _checked = new(StringComparer.OrdinalIgnoreCase);
    private int _anchor = -1;

    public event EventHandler SelectionChanged;
    public event EventHandler ItemActivated;
    public event EventHandler CheckedChanged;

    public string EmptyText { get; set; } = "没有找到匹配的应用";

    public IReadOnlyList<AppPackage> Items => _items;
    public int SelectedIndex => _selected;
    public AppPackage SelectedItem => _selected >= 0 && _selected < _items.Count ? _items[_selected] : null;

    private bool _selectionMode;

    /// <summary>多选模式：只在用户主动进入后显示复选框。</summary>
    public bool SelectionMode
    {
        get => _selectionMode;
        set
        {
            if (_selectionMode == value) return;
            _selectionMode = value;
            if (!value) { _checked.Clear(); _anchor = -1; }
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public int CheckedCount => _checked.Count;
    public List<AppPackage> CheckedItems => _items.Where(p => _checked.Contains(p.FullName)).ToList();
    public bool IsChecked(AppPackage p) => p != null && _checked.Contains(p.FullName);

    public PackageList()
    {
        BackColor = Theme.AppBg;
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
        MouseWheel += HandleWheel;
    }

    private int RowHeight => U(62);
    private int PaddingX => U(8);
    private int ScrollBarWidth => U(10);

    private int ViewHeight => Height - U(8);
    private int ContentHeight => _items.Count * RowHeight + U(6);

    private int MaxScroll => Math.Max(0, ContentHeight - (Height - U(8)));

    public void SetItems(List<AppPackage> items, string selectFullName = null)
    {
        _items.Clear();
        _items.AddRange(items);
        _checked.Clear();
        _anchor = -1;
        _hover = -1;
        _selected = -1;
        _scroll = 0;

        if (!string.IsNullOrEmpty(selectFullName))
        {
            int idx = _items.FindIndex(p => string.Equals(p.FullName, selectFullName, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) _selected = idx;
        }

        Invalidate();
        CheckedChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleChecked(int index)
    {
        if (index < 0 || index >= _items.Count) return;
        var pkg = _items[index];
        if (!_checked.Remove(pkg.FullName)) _checked.Add(pkg.FullName);
        _anchor = index;
        Invalidate();
        CheckedChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetCheckedRange(int from, int to)
    {
        if (_items.Count == 0) return;
        int a = Math.Clamp(Math.Min(from, to), 0, _items.Count - 1);
        int b = Math.Clamp(Math.Max(from, to), 0, _items.Count - 1);
        for (int i = a; i <= b; i++) _checked.Add(_items[i].FullName);
        Invalidate();
        CheckedChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CheckAll()
    {
        foreach (var p in _items) _checked.Add(p.FullName);
        Invalidate();
        CheckedChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearChecked()
    {
        if (_checked.Count == 0) return;
        _checked.Clear();
        _anchor = -1;
        Invalidate();
        CheckedChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool AllChecked => _items.Count > 0 && _items.All(p => _checked.Contains(p.FullName));

    public void Select(AppPackage package)
    {
        int idx = package == null ? -1 : _items.IndexOf(package);
        SetSelectedIndex(idx, true);
    }

    public void ClearSelection() => SetSelectedIndex(-1, false);

    public void RefreshItem(AppPackage package)
    {
        Invalidate();
    }

    private void SetSelectedIndex(int index, bool raiseEvent)
    {
        if (index < -1 || index >= _items.Count) return;
        if (index == _selected) return;
        _selected = index;
        if (index >= 0) EnsureVisible(index);
        Invalidate();
        if (raiseEvent) SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureVisible(int index)
    {
        int top = index * RowHeight;
        int bottom = top + RowHeight;
        if (top < _scroll) _scroll = top;
        else if (bottom > _scroll + Height - U(8)) _scroll = bottom - (Height - U(8));
        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
    }

    private int IndexAt(Point p)
    {
        if (p.X > Width - ScrollBarWidth) return -1;
        int i = (p.Y - U(4) + _scroll) / RowHeight;
        return i >= 0 && i < _items.Count ? i : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_draggingThumb)
        {
            int travel = Height - U(8) - ThumbHeight();
            if (travel > 0)
            {
                int delta = e.Y - _dragStartY;
                _scroll = Math.Clamp(_dragStartScroll + (int)((long)delta * MaxScroll / travel), 0, MaxScroll);
                Invalidate();
            }
            return;
        }

        int i = IndexAt(e.Location);
        if (i != _hover)
        {
            _hover = i;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (e.X > Width - ScrollBarWidth && MaxScroll > 0)
        {
            var thumb = ThumbRect();
            if (thumb.Contains(e.Location))
            {
                _draggingThumb = true;
                _dragStartY = e.Y;
                _dragStartScroll = _scroll;
            }
            else
            {
                _scroll = Math.Clamp(_scroll + (e.Y < thumb.Y ? -Height : Height), 0, MaxScroll);
                Invalidate();
            }
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        bool wasDragging = _draggingThumb;
        _draggingThumb = false;
        if (!wasDragging && e.X <= Width - ScrollBarWidth)
        {
            int i = IndexAt(e.Location);
            if (i >= 0)
            {
                if (_selectionMode)
                {
                    _selected = i;
                    if ((ModifierKeys & Keys.Shift) == Keys.Shift && _anchor >= 0) SetCheckedRange(_anchor, i);
                    else ToggleChecked(i);
                    Invalidate();
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    SetSelectedIndex(i, true);
                    _anchor = i;
                }
            }
        }
        base.OnMouseUp(e);
    }

    private Rectangle CheckRect(int index)
    {
        int y = U(4) + index * RowHeight - _scroll;
        int size = U(18);
        return new Rectangle(U(2) + PaddingX + U(10), y + U(2) + (RowHeight - U(4) - size) / 2, size, size);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (e.X <= Width - ScrollBarWidth)
        {
            int i = IndexAt(e.Location);
            if (i >= 0)
            {
                SetSelectedIndex(i, true);
                ItemActivated?.Invoke(this, EventArgs.Empty);
            }
        }
        base.OnMouseDoubleClick(e);
    }

    private void HandleWheel(object sender, MouseEventArgs e)
    {
        if (MaxScroll <= 0) return;
        _scroll = Math.Clamp(_scroll - Math.Sign(e.Delta) * RowHeight * 2, 0, MaxScroll);
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_items.Count == 0) { base.OnKeyDown(e); return; }
        switch (e.KeyCode)
        {
            case Keys.Down:
                SetSelectedIndex(Math.Min(_items.Count - 1, _selected + 1), true);
                e.Handled = true;
                break;
            case Keys.Up:
                SetSelectedIndex(Math.Max(0, _selected <= 0 ? 0 : _selected - 1), true);
                e.Handled = true;
                break;
            case Keys.PageDown:
                SetSelectedIndex(Math.Min(_items.Count - 1, _selected + 8), true);
                e.Handled = true;
                break;
            case Keys.PageUp:
                SetSelectedIndex(Math.Max(0, _selected - 8), true);
                e.Handled = true;
                break;
            case Keys.Home:
                SetSelectedIndex(0, true);
                e.Handled = true;
                break;
            case Keys.End:
                SetSelectedIndex(_items.Count - 1, true);
                e.Handled = true;
                break;
            case Keys.Enter:
                ItemActivated?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case Keys.Space:
                if (_selected >= 0) ToggleChecked(_selected);
                e.Handled = true;
                break;
            case Keys.A:
                if ((e.Modifiers & Keys.Control) == Keys.Control)
                {
                    if (AllChecked) ClearChecked(); else CheckAll();
                    e.Handled = true;
                }
                break;
        }
        base.OnKeyDown(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
    }

    private int ThumbHeight()
    {
        if (ContentHeight <= 0) return Height;
        int view = Height - U(8);
        int h = (int)((long)view * view / ContentHeight);
        return Math.Max(U(32), Math.Min(view, h));
    }

    private Rectangle ThumbRect()
    {
        int view = Height - U(8);
        int thumb = ThumbHeight();
        int travel = view - thumb;
        int y = U(4) + (travel <= 0 || MaxScroll <= 0 ? 0 : (int)((long)_scroll * travel / MaxScroll));
        return new Rectangle(Width - ScrollBarWidth + U(3), y, U(4), thumb);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackgroundColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var card = new RectangleF(0, 0, Width - 1, Height - 1);
        Theme.FillRound(g, card, U(10), Theme.Surface);
        Theme.DrawRound(g, card, U(10), Theme.Stroke);

        if (_items.Count == 0)
        {
            Theme.GlyphDraw(g, Glyphs.Search, Theme.GlyphHuge, Theme.Text3, new Rectangle(0, Height / 2 - U(56), Width, U(56)));
            TextRenderer.DrawText(g, EmptyText, Theme.Body, new Rectangle(0, Height / 2, Width, U(28)),
                Theme.Text3, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.SingleLine);
            return;
        }

        var state = g.Save();
        g.SetClip(new RectangleF(U(1), U(1), Width - U(2), Height - U(2)));

        int first = Math.Max(0, _scroll / RowHeight);
        int last = Math.Min(_items.Count - 1, (_scroll + Height) / RowHeight);

        for (int i = first; i <= last; i++)
        {
            int y = U(4) + i * RowHeight - _scroll;
            var row = new Rectangle(U(2), y, Width - U(4), RowHeight);
            DrawRow(g, _items[i], row, i == _selected, i == _hover);
        }

        g.Restore(state);

        if (MaxScroll > 0)
        {
            var thumb = ThumbRect();
            Theme.FillRound(g, new RectangleF(thumb.X, thumb.Y, thumb.Width, thumb.Height), U(2),
                _draggingThumb ? Theme.Text3 : Theme.Blend(Theme.Text3, Theme.Surface, 0.45f));
        }
    }

    private void DrawRow(Graphics g, AppPackage pkg, Rectangle row, bool selected, bool hover)
    {
        var inner = new RectangleF(row.X + PaddingX, row.Y + U(2), row.Width - PaddingX * 2 - U(2), row.Height - U(4));
        if (selected)
            Theme.FillRound(g, inner, U(7), Theme.SelectedFill);
        else if (hover)
            Theme.FillRound(g, inner, U(7), Theme.Hover);

        if (selected)
            Theme.FillRound(g, new RectangleF(inner.X + U(2), inner.Y + (inner.Height - U(20)) / 2, U(3), U(20)), U(1.5f), Theme.Accent);

        if (_selectionMode)
        {
            bool isChecked = _checked.Contains(pkg.FullName);
            int boxSize = U(18);
            var box = new RectangleF(inner.X + U(10), inner.Y + (inner.Height - boxSize) / 2f, boxSize, boxSize);
            if (isChecked)
            {
                Theme.FillRound(g, box, U(4), Theme.Accent);
                Theme.GlyphDraw(g, Glyphs.Check, Theme.GlyphSm, Theme.AccentText, Rectangle.Round(box));
            }
            else
            {
                Theme.FillRound(g, box, U(4), Theme.Surface);
                Theme.DrawRound(g, box, U(4), Theme.Stroke);
            }
        }

        int iconSize = U(36);
        int iconLeft = (int)inner.X + (_selectionMode ? U(38) : U(12));
        var iconRect = new Rectangle(iconLeft, (int)(inner.Y + (inner.Height - iconSize) / 2), iconSize, iconSize);
        DrawIcon(g, pkg, iconRect);

        int textX = iconRect.Right + U(12);
        int textRight = (int)inner.Right - U(104);
        int nameWidth = Math.Max(U(40), textRight - textX);
        var nameRect = new Rectangle(textX, (int)inner.Y + U(9), nameWidth, U(22));
        TextRenderer.DrawText(g, pkg.Name, Theme.BodyStrong, nameRect, Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        var subRect = new Rectangle(textX, (int)inner.Y + U(31), nameRect.Width, U(18));
        TextRenderer.DrawText(g, pkg.Subtitle, Theme.Small, subRect, Theme.Text3,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        var sizeRect = new Rectangle((int)inner.Right - U(96), (int)inner.Y + U(10), U(86), U(20));
        TextRenderer.DrawText(g, pkg.SizeText, Theme.SmallStrong, sizeRect, Theme.Text2,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

        var badgeRect = new Rectangle((int)inner.Right - U(96), (int)inner.Y + U(32), U(86), U(16));
        TextRenderer.DrawText(g, pkg.KindText, Theme.Tiny, badgeRect, Theme.Text3,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    private void DrawIcon(Graphics g, AppPackage pkg, Rectangle rect)
    {
        var bmp = GetIcon(pkg);
        if (bmp != null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(bmp, rect);
            return;
        }

        Theme.FillRound(g, rect, U(8), Theme.AccentSoft);
        Theme.GlyphDraw(g, Glyphs.Apps, Theme.GlyphLg, Theme.Accent, rect);
    }

    private Bitmap GetIcon(AppPackage pkg)
    {
        if (_icons.TryGetValue(pkg.FullName, out var cached)) return cached;

        Bitmap result = null;
        try
        {
            if (!string.IsNullOrEmpty(pkg.LogoPath) && File.Exists(pkg.LogoPath))
            {
                using var src = Image.FromFile(pkg.LogoPath);
                int s = U(36);
                result = new Bitmap(s, s);
                using var g = Graphics.FromImage(result);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float scale = Math.Min((float)s / src.Width, (float)s / src.Height);
                int w = Math.Max(1, (int)(src.Width * scale));
                int h = Math.Max(1, (int)(src.Height * scale));
                g.DrawImage(src, new Rectangle((s - w) / 2, (s - h) / 2, w, h));
            }
        }
        catch
        {
            if (result != null) { result.Dispose(); }
            result = null;
        }

        _icons[pkg.FullName] = result;
        return result;
    }

    public void ClearIconCache()
    {
        foreach (var bmp in _icons.Values) bmp?.Dispose();
        _icons.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ClearIconCache();
        base.Dispose(disposing);
    }
}

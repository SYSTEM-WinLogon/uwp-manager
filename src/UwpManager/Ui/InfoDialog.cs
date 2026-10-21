using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UwpManager.Ui;

public enum DialogKind
{
    Info,
    Success,
    Warning,
    Error,
    Question,
}

/// <summary>Fluent 风格消息 / 确认 / 进度对话框。</summary>
public sealed class InfoDialog : FluentWindow
{
    private const int DialogWidthDip = 420;

    private readonly string _title;
    private readonly string _message;
    private readonly DialogKind _kind;
    private readonly bool _busy;
    private string _busyText;
    private readonly System.Windows.Forms.Timer _timer;
    private float _angle;
    private FluentButton[] _buttons = Array.Empty<FluentButton>();

    private InfoDialog(string title, string message, DialogKind kind, bool busy, string busyText)
    {
        _title = title ?? "";
        _message = message ?? "";
        _kind = kind;
        _busy = busy;
        _busyText = busyText ?? "";

        Resizable = false;
        CaptionHeight = 40;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Surface;
        Size = new Size(420, 240);

        if (_busy)
        {
            _timer = new System.Windows.Forms.Timer { Interval = 33 };
            _timer.Tick += (_, _) => { _angle = (_angle + 12f) % 360f; Invalidate(); };
            _timer.Start();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        FitToContent();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        FitToContent();
    }

    private void FitToContent()
    {
        int pad = U(24);
        int width = U(DialogWidthDip);
        int textWidth = width - pad * 2;
        int messageHeight = TextRenderer.MeasureText(_message, Theme.Body, new Size(textWidth, int.MaxValue),
            TextFormatFlags.WordBreak).Height;

        int height = pad + U(34) + U(8) + messageHeight + U(18) + U(34) + pad;
        height = Math.Max(height, U(170));

        ClientSize = new Size(width, height);
        MinimumSize = Size;
        MaximumSize = Size;
        LayoutButtons();
        Invalidate();
    }

    public void SetStatus(string text)
    {
        _busyText = text ?? "";
        Invalidate();
    }

    public void AddButtons(params (string Text, DialogResult Result, ButtonVariant Variant)[] buttons)
    {
        _buttons = new FluentButton[buttons.Length];
        for (int i = 0; i < buttons.Length; i++)
        {
            var spec = buttons[i];
            var b = new FluentButton
            {
                Text = spec.Text,
                Variant = spec.Variant,
                BackColor = Theme.Surface,
            };
            b.Click += (_, _) =>
            {
                DialogResult = spec.Result;
                Close();
            };
            Controls.Add(b);
            _buttons[i] = b;
        }
        if (IsHandleCreated) LayoutButtons();
    }

    /// <summary>危险样式：顶部红色警示条 + 红色标题；按钮从左侧排列（与常规确认的位置不同）。</summary>
    public bool DangerStyle { get; set; }

    private void LayoutButtons()
    {
        if (_buttons.Length == 0) return;
        int pad = U(24);
        int h = U(34);

        int[] widths = new int[_buttons.Length];
        for (int i = 0; i < _buttons.Length; i++)
        {
            int textWidth = TextRenderer.MeasureText(_buttons[i].Text ?? "", Theme.Body, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
            widths[i] = Math.Max(U(94), textWidth + U(30));
        }

        if (DangerStyle)
        {
            // 第二个确认框：主按钮放在最左侧，避免与第一次确认的点击位置相同
            int x = pad;
            for (int i = 0; i < _buttons.Length; i++)
            {
                _buttons[i].Size = new Size(widths[i], h);
                _buttons[i].Location = new Point(x, Height - pad - h);
                x += widths[i] + U(8);
            }
        }
        else
        {
            int x = Width - pad;
            for (int i = 0; i < _buttons.Length; i++)
            {
                x -= widths[i];
                _buttons[i].Size = new Size(widths[i], h);
                _buttons[i].Location = new Point(x, Height - pad - h);
                x -= U(8);
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int pad = U(24);
        (string glyph, Color color) = _kind switch
        {
            DialogKind.Success => (Glyphs.Check, Theme.Accent),
            DialogKind.Warning => (Glyphs.Warning, Theme.Warning),
            DialogKind.Error => (Glyphs.Error, Theme.Danger),
            _ => (Glyphs.Info, Theme.Accent),
        };

        if (DangerStyle)
        {
            // 醒目标识：顶部红色警示条 + 红色图标与标题
            Theme.FillRound(g, new RectangleF(0, 0, Width, U(8)), 0, Theme.DangerSolid);
            Theme.FillRound(g, new RectangleF(0, 0, Width, U(16)), 0, Color.FromArgb(40, Theme.DangerSolid));
            color = Theme.DangerSolid;
        }

        Theme.GlyphDraw(g, glyph, DangerStyle ? Theme.GlyphXl : Theme.GlyphLg, color,
            new Rectangle(pad, pad + U(2), U(28), U(28)));

        var titleRect = new Rectangle(pad + U(40), pad, Width - pad * 2 - U(40), U(30));
        TextRenderer.DrawText(g, _title, Theme.DialogTitle, titleRect, DangerStyle ? Theme.DangerSolid : Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        int messageTop = pad + U(44);
        int messageBottom = _busy ? Height - pad - U(34) : Height - pad - U(34) - U(16);
        var msgRect = new Rectangle(pad, messageTop, Width - pad * 2, Math.Max(U(20), messageBottom - messageTop));
        TextRenderer.DrawText(g, _message, Theme.Body, msgRect, Theme.Text2,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

        if (_busy)
        {
            int size = U(30);
            var spinner = new Rectangle(Width - pad - size, Height - pad - size - U(2), size, size);
            using var pen = new Pen(Theme.Accent, U(3)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(pen, spinner, _angle, 265f);
            using var track = new Pen(Theme.StrokeSoft, U(3));
            g.DrawArc(track, spinner, _angle + 265f, 60f);

            if (!string.IsNullOrEmpty(_busyText))
            {
                var t = new Rectangle(pad, spinner.Y + U(4), Width - pad * 2 - size - U(8), U(22));
                TextRenderer.DrawText(g, _busyText, Theme.Small, t, Theme.Text3,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape && !_busy)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
        base.OnKeyDown(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer?.Stop();
        _timer?.Dispose();
        base.OnFormClosed(e);
    }

    // ---------- 静态入口 ----------
    public static void Info(IWin32Window owner, string title, string message, DialogKind kind = DialogKind.Info)
    {
        using var dlg = new InfoDialog(title, message, kind, false, null);
        dlg.AddButtons(("确定", DialogResult.OK, ButtonVariant.Accent));
        dlg.ShowDialog(owner);
    }

    public static bool Confirm(IWin32Window owner, string title, string message, string okText,
        DialogKind kind = DialogKind.Question, string cancelText = "取消", ButtonVariant okVariant = ButtonVariant.Accent)
    {
        using var dlg = new InfoDialog(title, message, kind, false, null);
        dlg.AddButtons((okText, DialogResult.OK, okVariant), (cancelText, DialogResult.Cancel, ButtonVariant.Standard));
        return dlg.ShowDialog(owner) == DialogResult.OK;
    }

    /// <summary>二次确认：红色警示条 + 主按钮在左侧（与第一次确认的位置不同），防止连点误操作。</summary>
    public static bool ConfirmDanger(IWin32Window owner, string title, string message, string okText,
        string cancelText = "取消")
    {
        using var dlg = new InfoDialog(title, message, DialogKind.Warning, false, null) { DangerStyle = true };
        dlg.AddButtons((okText, DialogResult.OK, ButtonVariant.Danger), (cancelText, DialogResult.Cancel, ButtonVariant.Standard));
        return dlg.ShowDialog(owner) == DialogResult.OK;
    }

    /// <summary>安装完成：可「启动」或「完成」。</summary>
    public static bool Completed(IWin32Window owner, string title, string message, string launchText, string doneText)
    {
        using var dlg = new InfoDialog(title, message, DialogKind.Success, false, null);
        dlg.AddButtons((launchText, DialogResult.OK, ButtonVariant.Accent), (doneText, DialogResult.Cancel, ButtonVariant.Standard));
        return dlg.ShowDialog(owner) == DialogResult.OK;
    }

    public static async Task<Exception> RunTaskAsync(IWin32Window owner, string title, string message, string busyText, Func<IProgress<string>, Task> work)
    {
        using var dlg = new InfoDialog(title, message, DialogKind.Info, true, busyText);
        Exception failure = null;
        var progress = new Progress<string>(s => dlg.SetStatus(s));
        dlg.Shown += async (_, _) =>
        {
            try
            {
                await work(progress);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                dlg.DialogResult = failure == null ? DialogResult.OK : DialogResult.Abort;
                dlg.Close();
            }
        };
        dlg.ShowDialog(owner);
        return failure;
    }

    public static async Task<Exception> RunBusyAsync(IWin32Window owner, string title, string message, string busyText, Func<Task> work)
    {
        using var dlg = new InfoDialog(title, message, DialogKind.Info, true, busyText);
        Exception failure = null;
        dlg.Shown += async (_, _) =>
        {
            try
            {
                await work();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                dlg.DialogResult = failure == null ? DialogResult.OK : DialogResult.Abort;
                dlg.Close();
            }
        };
        dlg.ShowDialog(owner);
        return failure;
    }
}

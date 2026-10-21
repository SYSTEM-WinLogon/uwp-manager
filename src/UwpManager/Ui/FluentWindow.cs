using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UwpManager.Ui;

/// <summary>Fluent 风格无边框窗口基类：自绘标题栏、原生阴影/缩放/贴靠。</summary>
public class FluentWindow : Form
{
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_SYSMENU = 0x00080000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_MAXIMIZEBOX = 0x00020000;
    private const int WS_MINIMIZEBOX = 0x00010000;
    private const int CS_DROPSHADOW = 0x00020000;

    public int CaptionHeight { get; protected set; } = 44;
    public bool Resizable { get; set; } = true;

    public float Dpi => DeviceDpi / 96f;
    public int U(double dip) => (int)Math.Round(dip * Dpi);

    public FluentWindow()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.AppBg;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        KeyPreview = true;
        Theme.Changed += HandleThemeChanged;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_MAXIMIZEBOX | WS_MINIMIZEBOX;
            if (!Resizable)
                cp.Style &= ~(WS_THICKFRAME | WS_MAXIMIZEBOX);
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetRoundedCorners(Handle, true);
        Native.SetDarkTitleBar(Handle, Theme.Dark);
        ApplyMaximizedBounds();
    }

    /// <summary>最大化时正好贴合工作区（否则无边框窗口的不可见边框会把内容顶到屏幕外）。</summary>
    private void ApplyMaximizedBounds()
    {
        if (!IsHandleCreated) return;
        try { MaximizedBounds = Screen.FromHandle(Handle).WorkingArea; } catch { }
    }

    private void HandleThemeChanged()
    {
        if (IsDisposed) return;
        BackColor = Theme.AppBg;
        ForeColor = Theme.Text;
        if (IsHandleCreated) Native.SetDarkTitleBar(Handle, Theme.Dark);
        Invalidate(true);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case Native.WM_NCCALCSIZE:
                if (m.WParam != IntPtr.Zero)
                {
                    if (WindowState == FormWindowState.Maximized)
                    {
                        // 最大化时窗口比工作区多出不可见边框，这里按边框厚度收回来，
                        // 且只在窗口确实覆盖满工作区时才调整（避免“还原”瞬间误判导致客户区过大）
                        var monitor = Native.MonitorFromWindow(Handle, 2);
                        var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
                        var rc = Marshal.PtrToStructure<Native.RECT>(m.LParam);

                        if (Native.GetMonitorInfo(monitor, ref info))
                        {
                            int frame = Native.GetSystemMetrics(Native.SM_CXSIZEFRAME) +
                                        Native.GetSystemMetrics(Native.SM_CXPADDEDBORDER);

                            // 仅当窗口尺寸已经覆盖满工作区时，才把客户区对齐到工作区；
                            // “还原”那一瞬间窗口还没缩回，尺寸不满足条件，因此不会误判
                            if (rc.Width >= info.rcWork.Width - 2 && rc.Height >= info.rcWork.Height - frame * 2)
                            {
                                rc.Left = info.rcWork.Left;
                                rc.Top = info.rcWork.Top;
                                rc.Right = info.rcWork.Right;
                                rc.Bottom = info.rcWork.Bottom;
                                Marshal.StructureToPtr(rc, m.LParam, false);
                            }
                        }
                    }
                    m.Result = IntPtr.Zero;
                    return;
                }
                break;

            case Native.WM_NCHITTEST:
                if (HandleHitTest(ref m)) return;
                break;

            case Native.WM_SIZE:
                base.WndProc(ref m);
                // 最大化 / 还原后强制重排并整窗重绘，避免残留旧尺寸的布局
                LayoutNow();
                Invalidate(true);
                return;

            case Native.WM_ERASEBKGND:
                m.Result = (IntPtr)1;
                return;
        }
        base.WndProc(ref m);
    }


    private bool HandleHitTest(ref Message m)
    {
        if (WindowState == FormWindowState.Maximized) return false;

        int screenX = unchecked((short)(long)m.LParam);
        int screenY = unchecked((short)((long)m.LParam >> 16));
        var pt = PointToClient(new Point(screenX, screenY));

        int b = U(6);
        bool left = pt.X < b;
        bool right = pt.X > ClientSize.Width - b;
        bool top = pt.Y < b;
        bool bottom = pt.Y > ClientSize.Height - b;

        if (Resizable)
        {
            if (top && left) { m.Result = (IntPtr)Native.HTTOPLEFT; return true; }
            if (top && right) { m.Result = (IntPtr)Native.HTTOPRIGHT; return true; }
            if (bottom && left) { m.Result = (IntPtr)Native.HTBOTTOMLEFT; return true; }
            if (bottom && right) { m.Result = (IntPtr)Native.HTBOTTOMRIGHT; return true; }
            if (left) { m.Result = (IntPtr)Native.HTLEFT; return true; }
            if (right) { m.Result = (IntPtr)Native.HTRIGHT; return true; }
            if (top) { m.Result = (IntPtr)Native.HTTOP; return true; }
            if (bottom) { m.Result = (IntPtr)Native.HTBOTTOM; return true; }
        }

        if (pt.Y < U(CaptionHeight))
        {
            m.Result = (IntPtr)Native.HTCAPTION;
            return true;
        }
        return false;
    }



    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        LayoutNow();
        Invalidate(true);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutNow();
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.AppBg);
    }

    protected virtual void LayoutNow() { }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Theme.Changed -= HandleThemeChanged;
        base.Dispose(disposing);
    }
}

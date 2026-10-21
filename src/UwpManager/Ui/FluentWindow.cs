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
                        var monitor = Native.MonitorFromWindow(Handle, 2);
                        var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
                        if (Native.GetMonitorInfo(monitor, ref info))
                        {
                            var rc = Marshal.PtrToStructure<Native.RECT>(m.LParam);
                            rc.Left = info.rcWork.Left;
                            rc.Top = info.rcWork.Top;
                            rc.Right = info.rcWork.Right;
                            rc.Bottom = info.rcWork.Bottom;
                            Marshal.StructureToPtr(rc, m.LParam, false);
                        }
                    }
                    m.Result = IntPtr.Zero;
                    return;
                }
                break;

            case Native.WM_NCHITTEST:
                if (HandleHitTest(ref m)) return;
                break;

            case Native.WM_GETMINMAXINFO:
                if (WindowState == FormWindowState.Maximized) AdjustMaximizedBounds(ref m);
                break;

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

    private static void AdjustMaximizedBounds(ref Message m)
    {
        // 保持最大化时贴合工作区，避免无边框窗口溢出屏幕
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(m.LParam);
        var screen = Screen.FromHandle(m.HWnd);
        var wa = screen.WorkingArea;
        var bounds = screen.Bounds;
        mmi.ptMaxPosition.X = wa.X - bounds.X;
        mmi.ptMaxPosition.Y = wa.Y - bounds.Y;
        mmi.ptMaxSize.X = wa.Width;
        mmi.ptMaxSize.Y = wa.Height;
        Marshal.StructureToPtr(mmi, m.LParam, false);
        m.Result = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public Native.POINT ptReserved;
        public Native.POINT ptMaxSize;
        public Native.POINT ptMaxPosition;
        public Native.POINT ptMinTrackSize;
        public Native.POINT ptMaxTrackSize;
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

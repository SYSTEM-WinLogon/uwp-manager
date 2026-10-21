using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UwpManager.Ui;

public sealed class MainForm : FluentWindow
{
    private readonly NavList _nav = new();
    private readonly SearchBox _search = new();
    private readonly Segmented _sort = new();
    private readonly PackageList _list = new();
    private readonly DetailPane _detail = new();
    private readonly WindowButtons _windowButtons = new();
    private readonly FluentButton _install = new();
    private readonly FluentButton _refresh = new();
    private readonly FluentButton _selectAll = new();
    private readonly FluentButton _selectToggle = new();
    private readonly SelectionBar _selectionBar = new();
    private readonly ToggleSwitch _themeSwitch = new();

    private readonly System.Windows.Forms.Timer _toastTimer = new() { Interval = 2800 };
    private static readonly string VersionText = BuildVersionText();

    private static string BuildVersionText()
    {
        var v = typeof(MainForm).Assembly.GetName().Version ?? new Version(1, 0);
        return $"v{ v.Major}.{ v.Minor}.{ v.Build}";
    }

    private List<AppPackage> _all = new();
    private Bitmap _logo;
    private bool _loading;
    private bool _sized;
    private int _loadDone;
    private int _loadTotal;
    private int _filterIndex;
    private string _toast;
    private Rectangle _adminPillRect;

    public MainForm()
    {
        Text = "UWP 管家";
        MinimumSize = new Size(1024, 660);
        Size = new Size(1220, 800);
        CaptionHeight = 44;
        KeyPreview = true;

        try { _logo = Icon.ExtractAssociatedIcon(Application.ExecutablePath)?.ToBitmap(); } catch { }

        BuildControls();
        WireEvents();
    }

    private void BuildControls()
    {
        _nav.Items.Add(new NavItem { Glyph = Glyphs.AllApps, Title = "全部应用" });
        _nav.Items.Add(new NavItem { Glyph = Glyphs.Library, Title = "应用" });
        _nav.Items.Add(new NavItem { Glyph = Glyphs.Package, Title = "框架与资源" });
        _nav.Items.Add(new NavItem { Glyph = Glyphs.Settings, Title = "系统组件" });
        _nav.BackColor = Color.Transparent;

        _search.BackColor = Theme.AppBg;
        _sort.Options = new[] { "名称", "大小", "时间" };
        _sort.BackColor = Theme.AppBg;

        _list.BackColor = Theme.AppBg;
        _list.EmptyText = "正在读取已安装的应用…";

        _detail.BackColor = Theme.AppBg;

        _windowButtons.BackColor = Color.Transparent;
        _windowButtons.Host = this;

        _install.Text = "安装应用";
        _install.Glyph = Glyphs.Add;
        _install.Variant = ButtonVariant.Accent;
        _install.BackColor = Theme.AppBg;

        _refresh.Glyph = Glyphs.Refresh;
        _refresh.Variant = ButtonVariant.Subtle;
        _refresh.BackColor = Theme.AppBg;

        _selectAll.Text = "全选";
        _selectAll.Variant = ButtonVariant.Subtle;
        _selectAll.BackColor = Theme.AppBg;
        _selectAll.Visible = false;

        _selectToggle.Text = "选择";
        _selectToggle.Variant = ButtonVariant.Subtle;
        _selectToggle.BackColor = Theme.AppBg;

        _selectionBar.BackColor = Theme.AppBg;
        _selectionBar.Visible = false;

        _themeSwitch.BackColor = Theme.SurfaceAlt;

        Controls.Add(_nav);
        Controls.Add(_search);
        Controls.Add(_sort);
        Controls.Add(_list);
        Controls.Add(_detail);
        Controls.Add(_windowButtons);
        Controls.Add(_install);
        Controls.Add(_refresh);
        Controls.Add(_selectAll);
        Controls.Add(_selectToggle);
        Controls.Add(_selectionBar);
        Controls.Add(_themeSwitch);

        _themeSwitch.Checked = Theme.Dark;
        Theme.Changed += HandleThemeChanged;
    }

    private void HandleThemeChanged()
    {
        _themeSwitch.Checked = Theme.Dark;
        UpdateBackColors();
        Invalidate(true);
    }

    private void UpdateBackColors()
    {
        _search.BackColor = Theme.AppBg;
        _sort.BackColor = Theme.AppBg;
        _list.BackColor = Theme.AppBg;
        _detail.BackColor = Theme.AppBg;
        _install.BackColor = Theme.AppBg;
        _refresh.BackColor = Theme.AppBg;
        _selectAll.BackColor = Theme.AppBg;
        _selectionBar.BackColor = Theme.AppBg;
        _nav.BackColor = Color.Transparent;
        _themeSwitch.BackColor = Theme.SurfaceAlt;
    }

    private void WireEvents()
    {
        _nav.SelectionChanged += (_, _) =>
        {
            _filterIndex = _nav.SelectedIndex;
            ApplyFilter();
        };

        _search.QueryChanged += (_, _) => ApplyFilter();

        _sort.SelectionChanged += (_, _) => ApplyFilter();

        _list.SelectionChanged += (_, _) =>
        {
            _detail.Package = _list.SelectedItem;
        };

        _list.ItemActivated += async (_, _) => await LaunchSelectedAsync();

        _detail.OpenRequested += async (_, _) => await LaunchSelectedAsync();
        _detail.UninstallRequested += async (_, _) => await UninstallSelectedAsync();
        _detail.RevealRequested += (_, path) => AppxService.RevealInExplorer(path);
        _detail.CopyRequested += (_, text) =>
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                Clipboard.SetText(text);
                ShowToast("已复制到剪贴板");
            }
            catch
            {
                ShowToast("复制失败");
            }
        };

        _install.Click += async (_, _) => await InstallPackageAsync();
        _refresh.Click += async (_, _) => await ReloadAsync();

        _list.CheckedChanged += (_, _) => UpdateSelectionUi();
        _selectToggle.Click += (_, _) => ToggleSelectionMode();
        _selectAll.Click += (_, _) =>
        {
            if (_list.AllChecked) _list.ClearChecked();
            else _list.CheckAll();
        };
        _selectionBar.DeleteRequested += async (_, _) => await BatchUninstallAsync();
        _selectionBar.ClearRequested += (_, _) => _list.ClearChecked();

        _themeSwitch.Toggled += (_, _) => Theme.SetDark(_themeSwitch.Checked);

        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            _toast = null;
            Invalidate(new Rectangle(0, Height - U(140), Width, U(140)));
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        MinimumSize = new Size(U(1024), U(660));
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        MinimumSize = new Size(U(1024), U(660));
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            if (!_sized)
            {
                _sized = true;
                var wa = Screen.FromControl(this).WorkingArea;
                int w = Math.Min(U(1220), wa.Width - U(40));
                int h = Math.Min(U(800), wa.Height - U(40));
                Size = new Size(w, h);
                Location = new Point(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2);
            }
            _nav.SelectedIndex = 0;
            _filterIndex = 0;
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            CrashLog.Write("onshown", ex);
            throw;
        }
    }

    // ---------------- 布局 ----------------
    protected override void LayoutNow()
    {
        if (ClientSize.Width <= 0) return;

        int w = ClientSize.Width;
        int h = ClientSize.Height;

        _windowButtons.SetBounds(w - U(138), 0, U(138), U(44));

        int sidebar = U(236);
        _nav.SetBounds(U(10), U(74), sidebar - U(20), U(40) * _nav.Items.Count);
        _themeSwitch.SetBounds(U(24), h - U(104), U(40), U(20));

        int x0 = sidebar + U(28);
        int right = w - U(28);

        _install.SetBounds(right - U(124), U(58), U(124), U(36));
        _refresh.SetBounds(right - U(124) - U(44), U(58), U(36), U(36));

        _search.SetBounds(x0, U(126), Math.Clamp(right - U(14) - U(240) - U(170) - x0, U(180), U(420)), U(36));
        _sort.SetBounds(right - U(240), U(126), U(240), U(36));
        int toggleX = right - U(240) - U(8) - U(70);
        _selectToggle.SetBounds(toggleX, U(126), U(70), U(36));
        _selectAll.SetBounds(toggleX - U(8) - U(84), U(126), U(84), U(36));

        int top = U(180);
        int bottom = h - U(52);
        int detailW = U(348);
        int gap = U(16);

        bool wide = w - x0 > U(760);
        int listW = wide ? (right - detailW - gap) - x0 : right - x0;

        _list.SetBounds(x0, top, Math.Max(U(320), listW), Math.Max(U(220), bottom - top));

        if (wide)
        {
            _detail.Visible = true;
            _detail.SetBounds(right - detailW, top, detailW, Math.Max(U(220), bottom - top));
        }
        else
        {
            _detail.Visible = false;
        }

        // 多选操作条：浮在列表右下角
        int barW = U(430);
        int barH = U(54);
        _selectionBar.SetBounds(
            Math.Max(x0 + U(8), _list.Right - barW - U(10)),
            _list.Bottom - barH - U(10),
            barW,
            barH);
        _selectionBar.Visible = _list.CheckedCount > 0;
        if (_selectionBar.Visible) _selectionBar.BringToFront();
    }

    private void UpdateSelectionUi()
    {
        _selectionBar.Count = _list.CheckedCount;
        _selectionBar.Visible = _list.SelectionMode && _list.CheckedCount > 0;
        _selectToggle.Text = _list.SelectionMode ? "完成" : "选择";
        _selectAll.Visible = _list.SelectionMode;
        _selectAll.Text = _list.AllChecked ? "取消全选" : "全选";
        if (_selectionBar.Visible) _selectionBar.BringToFront();
        Invalidate(new Rectangle(_list.Left, _list.Bottom - U(80), _list.Width, U(80)));
    }

    private void ToggleSelectionMode()
    {
        _list.SelectionMode = !_list.SelectionMode;
        UpdateSelectionUi();
        if (_list.SelectionMode) ShowToast("多选模式：点击行即可勾选");
    }

    // ---------------- 绘制 ----------------
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int w = ClientSize.Width;
        int h = ClientSize.Height;
        int sidebar = U(236);

        using (var bg = new SolidBrush(Theme.AppBg))
            g.FillRectangle(bg, 0, 0, w, h);

        using (var side = new SolidBrush(Theme.SurfaceAlt))
            g.FillRectangle(side, 0, 0, sidebar, h);

        using (var line = new Pen(Theme.StrokeSoft, 1f))
        {
            g.DrawLine(line, sidebar, 0, sidebar, h);
            g.DrawLine(line, sidebar, h - U(46), w, h - U(46));
        }

        DrawBrand(g);
        DrawSidebarBottom(g, h);
        DrawHeader(g, sidebar);
        DrawStatus(g, sidebar, w, h);
        DrawToast(g, w, h);
    }

    private void DrawBrand(Graphics g)
    {
        int s = U(26);
        if (_logo != null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(_logo, new Rectangle(U(18), U(9), s, s));
        }
        else
        {
            Theme.FillRound(g, new RectangleF(U(18), U(9), s, s), U(6), Theme.Accent);
        }

        Theme.DrawText(g, "UWP 管家", Theme.BodyStrong, Theme.Text, new Rectangle(U(54), U(6), U(160), U(22)));
        Theme.DrawText(g, "应用管理器", Theme.Tiny, Theme.Text3, new Rectangle(U(54), U(25), U(160), U(16)));
    }

    private void DrawSidebarBottom(Graphics g, int h)
    {
        int y = h - U(104);
        Theme.DrawText(g, "深色模式", Theme.Small, Theme.Text2, new Rectangle(U(74), y - U(2), U(140), U(24)));

        int py = h - U(64);
        string label = "管理员模式";
        var size = TextRenderer.MeasureText(label, Theme.Tiny);
        int pillW = size.Width + U(40);
        _adminPillRect = new Rectangle(U(20), py, pillW, U(26));
        Theme.FillRound(g, _adminPillRect, U(13), Theme.AccentSoft);
        Theme.GlyphDraw(g, Glyphs.Shield, Theme.GlyphSm, Theme.Accent,
            new Rectangle(_adminPillRect.X + U(8), _adminPillRect.Y, U(18), _adminPillRect.Height));
        Theme.DrawText(g, label, Theme.Tiny, Theme.Accent,
            new Rectangle(_adminPillRect.X + U(28), _adminPillRect.Y, size.Width + U(6), _adminPillRect.Height));

        Theme.DrawText(g, VersionText + "  ·  Windows 10 / 11", Theme.Tiny, Theme.Text3, new Rectangle(U(20), h - U(34), U(220), U(18)));
    }

    private void DrawHeader(Graphics g, int sidebar)
    {
        int x0 = sidebar + U(28);
        string[] titles = { "全部应用", "应用", "框架与资源", "系统组件" };
        string title = titles[Math.Clamp(_filterIndex, 0, titles.Length - 1)];
        Theme.DrawText(g, title, Theme.Title, Theme.Text, new Rectangle(x0, U(48), U(400), U(40)));

        long total = _list.Items.Sum(p => p.Size ?? 0);
        string subtitle;
        if (_loading)
            subtitle = _loadTotal > 0 ? $"正在统计占用空间 {_loadDone}/{_loadTotal}…" : "正在读取已安装的应用…";
        else
            subtitle = $"共 {_list.Items.Count} 个包 · 合计 {AppxService.FormatSize(total)}";

        Theme.DrawText(g, subtitle, Theme.Small, Theme.Text3, new Rectangle(x0, U(92), U(520), U(22)));
    }

    private void DrawStatus(Graphics g, int sidebar, int w, int h)
    {
        int x0 = sidebar + U(28);
        string left = _loading
            ? "正在读取…"
            : Program.Relocated
                ? "本地副本模式 · 原目录受限，已自动切换"
                : AppxService.CompatMode
                    ? "兼容模式 · 经系统 PowerShell 读取应用包"
                    : "就绪 · 数据来自 PackageManager（当前用户）";
        Theme.DrawText(g, left, Theme.Tiny, Theme.Text3, new Rectangle(x0, h - U(40), U(560), U(26)));

        string right = "以管理员身份运行";
        var size = TextRenderer.MeasureText(right, Theme.Tiny);
        int rx = w - U(28) - size.Width - U(50);
        Theme.GlyphDraw(g, Glyphs.Shield, Theme.GlyphSm, Theme.Text3, new Rectangle(rx - U(6), h - U(40), U(18), U(26)));
        Theme.DrawText(g, right, Theme.Tiny, Theme.Text3, new Rectangle(rx + U(16), h - U(40), size.Width + U(4), U(26)));
    }

    private void DrawToast(Graphics g, int w, int h)
    {
        if (string.IsNullOrEmpty(_toast)) return;
        var size = TextRenderer.MeasureText(_toast, Theme.Small);
        int tw = size.Width + U(48);
        int th = U(36);
        var rect = new RectangleF(w - U(28) - tw, h - U(74) - th, tw, th);

        Theme.FillRound(g, rect, U(8), Theme.Dark ? Theme.Surface : Color.FromArgb(0x20, 0x20, 0x20, 0x20));
        Theme.GlyphDraw(g, Glyphs.Check, Theme.GlyphSm, Color.White, new Rectangle((int)rect.X + U(12), (int)rect.Y, U(18), th));
        Theme.DrawText(g, _toast, Theme.Small, Color.White, new Rectangle((int)rect.X + U(34), (int)rect.Y, size.Width + U(4), th));
    }

    // ---------------- 数据 ----------------
    private async Task ReloadAsync(string selectFullName = null)
    {
        if (_loading) return;
        _loading = true;
        _loadDone = 0;
        _loadTotal = 0;
        _list.EmptyText = "正在读取已安装的应用…";
        ApplyFilter();
        Invalidate();

        var progress = new Progress<LoadProgress>(p =>
        {
            _loadDone = p.Done;
            _loadTotal = p.Total;
            Invalidate(new Rectangle(0, U(40), ClientSize.Width, U(80)));
        });

        try
        {
            var items = await AppxService.LoadAsync(progress, CancellationToken.None);
            _all = items;
            _list.ClearIconCache();
            UpdateNavCounts();
            ApplyFilter(selectFullName);
            ShowToast(Program.Relocated
                ? "已从受限目录切换为本地副本运行"
                : AppxService.CompatMode
                    ? $"已读取 {items.Count} 个应用包（兼容模式）"
                    : $"已读取 {items.Count} 个应用包");
        }
        catch (Exception ex)
        {
            CrashLog.Write("reload", ex);
            _all.Clear();
            _list.EmptyText = "读取失败";
            ApplyFilter();

            InfoDialog.Info(this, "读取失败",
                IsAccessDenied(ex)
                    ? "当前文件夹被系统安全策略锁定，Windows 拒绝向本程序提供应用包信息（0x80070005）。\n\n" +
                      "请把 UwpManager.exe 复制到桌面或其他普通文件夹后再运行。"
                    : ex.Message,
                DialogKind.Error);
        }
        finally
        {
            _loading = false;
            _list.EmptyText = "没有找到匹配的应用";
            if (!IsDisposed) Invalidate();
        }
    }

private static bool IsAccessDenied(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
            if (e.HResult == unchecked((int)0x80070005)) return true;
        return false;
    }

    private void UpdateNavCounts()
    {
        _nav.Items[0].Count = _all.Count;
        _nav.Items[1].Count = _all.Count(p => p.Kind == PackageKind.App);
        _nav.Items[2].Count = _all.Count(p => p.Kind == PackageKind.Framework || p.Kind == PackageKind.Resource);
        _nav.Items[3].Count = _all.Count(p => p.Kind == PackageKind.System);
        _nav.Invalidate();
    }

    private void ApplyFilter(string selectFullName = null)
    {
        IEnumerable<AppPackage> query = _all;
        query = _filterIndex switch
        {
            1 => query.Where(p => p.Kind == PackageKind.App),
            2 => query.Where(p => p.Kind == PackageKind.Framework || p.Kind == PackageKind.Resource),
            3 => query.Where(p => p.Kind == PackageKind.System),
            _ => query,
        };

        var text = _search.Query;
        if (!string.IsNullOrWhiteSpace(text))
            query = query.Where(p => p.Matches(text));

        var list = query.ToList();
        list = _sort.SelectedIndex switch
        {
            1 => list.OrderByDescending(p => p.Size ?? -1).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            2 => list.OrderByDescending(p => p.InstalledDate).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            _ => list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
        };

        string keep = selectFullName ?? _list.SelectedItem?.FullName;
        _list.SetItems(list, keep);
        _detail.Package = _list.SelectedItem;
        Invalidate();
    }

    // ---------------- 操作 ----------------
    private async Task LaunchSelectedAsync()
    {
        var pkg = _list.SelectedItem;
        if (pkg == null) return;
        bool ok = await AppxService.LaunchAsync(pkg);
        if (!ok) ShowToast("无法启动该应用");
    }

    private async Task UninstallSelectedAsync()
    {
        var pkg = _list.SelectedItem;
        if (pkg == null) return;

        bool risky = pkg.Kind != PackageKind.App;
        string kindNote = risky ? $"类型：{pkg.KindText}（系统内置组件，卸载可能影响系统功能）\n" : "";

        // 第一次确认：常规对话框，按钮在右下
        string message =
            $"确定要卸载“{pkg.Name}”吗？\n\n" +
            $"版本号：{pkg.VersionText}\n" +
            $"发布者：{pkg.Publisher}\n" +
            kindNote +
            $"包全名：{pkg.FullName}\n\n" +
            "卸载后应用数据将一并删除，且无法恢复。";

        if (!InfoDialog.Confirm(this, "卸载应用", message, "卸载", DialogKind.Warning, "取消", ButtonVariant.Danger)) return;

        // 第二次确认：红色警示条 + 主按钮移到左侧，避免误连点
        string secondMessage =
            $"即将永久卸载“{pkg.Name}”。\n\n" +
            $"版本号：{pkg.VersionText}\n" +
            $"包全名：{pkg.FullName}\n\n" +
            (risky
                ? "这是系统内置组件，卸载后相关功能可能直接失效，且通常无法从应用商店恢复。\n\n"
                : "") +
            "此操作不可撤销。确认请点击左下角的红色按钮。";

        if (!InfoDialog.ConfirmDanger(this, "再次确认 · 永久卸载", secondMessage, "确认卸载（不可恢复）")) return;

        var failure = await InfoDialog.RunBusyAsync(this, "正在卸载", $"正在移除 {pkg.Name}…", "请稍候，卸载中…",
            () => AppxService.UninstallAsync(pkg.FullName));

        if (failure != null)
        {
            ShowOperationError("卸载失败", failure);
            return;
        }

        ShowToast("已卸载 " + pkg.Name);
        await ReloadAsync();
    }

    private async Task InstallPackageAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择 UWP 安装包（可多选，一次装多个）",
            Filter = "UWP 应用包 (*.appx;*.msix;*.appxbundle;*.msixbundle)|*.appx;*.msix;*.appxbundle;*.msixbundle|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        // 先装普通包，再装 bundle（bundle 往往自带依赖）
        var files = dialog.FileNames
            .OrderBy(f => Path.GetExtension(f).ToLowerInvariant() is ".appxbundle" or ".msixbundle" ? 1 : 0)
            .ToArray();

        string preview = string.Join("\n", files.Take(8).Select(f => "· " + Path.GetFileName(f)));
        if (files.Length > 8) preview += $"\n… 等共 {files.Length} 个";

        string message =
            $"即将安装 {files.Length} 个包：\n\n{preview}\n\n" +
            "安装期间请不要关闭本窗口。\n" +
            "如果提示缺少依赖包，请先安装同目录 Dependencies 文件夹中的框架包。";

        if (!InfoDialog.Confirm(this, "安装应用", message, "开始安装", DialogKind.Question)) return;

        var before = new HashSet<string>(_all.Select(p => p.FullName), StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();

        var failure = await InfoDialog.RunTaskAsync(this, "正在安装", $"共 {files.Length} 个包", "准备中…", async progress =>
        {
            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                progress.Report($"({i + 1}/{files.Length}) {Path.GetFileName(file)}");
                try { await AppxService.InstallAsync(file); }
                catch (Exception ex) { failures.Add(Path.GetFileName(file) + "：" + ShortError(ex)); }
            }
        });

        await ReloadAsync();

        var added = _all.Where(p => !before.Contains(p.FullName)).ToList();
        if (added.Count > 0) _list.Select(added[0]);

        if (failures.Count > 0)
        {
            var lines = new List<string> { $"成功 {files.Length - failures.Count} 个，失败 {failures.Count} 个。", "" };
            lines.AddRange(failures.Take(6));
            if (failures.Count > 6) lines.Add($"… 其余 {failures.Count - 6} 个失败略");
            if (failure != null) { lines.Add(""); lines.Add(failure.Message); }
            InfoDialog.Info(this, "安装结果", string.Join("\n", lines), DialogKind.Warning);
            return;
        }

        string doneMessage = added.Count == 1
            ? $"「{added[0].Name}」已安装完成。\n\n版本号：{added[0].VersionText}\n发布者：{added[0].Publisher}\n" +
              $"占用空间：{added[0].SizeText}\n\n现在启动，还是稍后从开始菜单打开？"
            : $"{files.Length} 个包已安装完成。\n\n" + string.Join("\n", added.Take(6).Select(p => "· " + p.Name)) +
              "\n\n现在启动第一个，还是稍后从开始菜单打开？";

        bool launch = InfoDialog.Completed(this, "安装完成", doneMessage, "启动", "完成");
        if (launch && added.Count > 0)
        {
            bool ok = await AppxService.LaunchAsync(added[0]);
            if (!ok) ShowToast("无法启动该应用，请稍后从开始菜单打开");
        }
    }

    private async Task BatchUninstallAsync()
    {
        var targets = _list.CheckedItems;
        if (targets.Count == 0) return;

        int risky = targets.Count(p => p.Kind != PackageKind.App);
        string preview = string.Join("\n", targets.Take(8).Select(p => "· " + p.Name));
        if (targets.Count > 8) preview += $"\n… 等共 {targets.Count} 个";

        string first =
            $"将卸载以下 {targets.Count} 个应用：\n\n{preview}\n\n" +
            (risky > 0 ? $"其中 {risky} 个是系统内置组件，卸载可能影响系统功能。\n\n" : "") +
            "卸载后应用数据将一并删除，且无法恢复。";

        if (!InfoDialog.Confirm(this, "批量卸载", first, "卸载", DialogKind.Warning, "取消", ButtonVariant.Danger)) return;

        string second =
            $"即将永久卸载 {targets.Count} 个应用，此操作不可撤销。\n\n" +
            (risky > 0 ? $"其中包含 {risky} 个系统内置组件。\n\n" : "") +
            "确认请点击左下角的红色按钮。";

        if (!InfoDialog.ConfirmDanger(this, "再次确认 · 批量卸载", second, "确认卸载全部")) return;

        var failures = new List<string>();
        var failure = await InfoDialog.RunTaskAsync(this, "正在批量卸载", $"共 {targets.Count} 个应用", "准备中…", async progress =>
        {
            for (int i = 0; i < targets.Count; i++)
            {
                var pkg = targets[i];
                progress.Report($"({i + 1}/{targets.Count}) {pkg.Name}");
                try { await AppxService.UninstallAsync(pkg.FullName); }
                catch (Exception ex) { failures.Add(pkg.Name + "：" + ShortError(ex)); }
            }
        });

        await ReloadAsync();
        _list.ClearChecked();
        _list.SelectionMode = false;
        UpdateSelectionUi();

        var lines = new List<string> { $"成功 {targets.Count - failures.Count} 个，失败 {failures.Count} 个。" };
        if (failures.Count > 0)
        {
            lines.Add("");
            lines.AddRange(failures.Take(6));
            if (failures.Count > 6) lines.Add($"… 其余 {failures.Count - 6} 个失败略");
        }
        if (failure != null) { lines.Add(""); lines.Add(failure.Message); }

        if (failures.Count == 0 && failure == null)
        {
            ShowToast($"已卸载 {targets.Count} 个应用");
            InfoDialog.Info(this, "批量卸载完成", string.Join("\n", lines), DialogKind.Success);
        }
        else
        {
            InfoDialog.Info(this, "批量卸载有失败项", string.Join("\n", lines), DialogKind.Warning);
        }
    }

    private static string ShortError(Exception ex)
    {
        if (ex is AppxOperationException appx)
            return string.IsNullOrWhiteSpace(appx.Hint) ? appx.Message : appx.Message + "（" + appx.Hint + "）";
        return ex.Message;
    }
    private void ShowOperationError(string title, Exception failure)
    {
        if (failure is AppxOperationException appx)
        {
            string text = appx.Message + "\n\n错误代码：" + appx.CodeText;
            if (!string.IsNullOrWhiteSpace(appx.Hint)) text += "\n\n" + appx.Hint;
            InfoDialog.Info(this, title, text, DialogKind.Error);
        }
        else
        {
            InfoDialog.Info(this, title, failure.Message, DialogKind.Error);
        }
    }

    private void ShowToast(string message)
    {
        _toast = message;
        _toastTimer.Stop();
        _toastTimer.Start();
        Invalidate(new Rectangle(0, Height - U(160), Width, U(160)));
    }

    // ---------------- 快捷键 ----------------
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F5:
                _ = ReloadAsync();
                return true;
            case Keys.Control | Keys.F:
                _search.FocusInput();
                return true;
            case Keys.Control | Keys.O:
                _ = InstallPackageAsync();
                return true;
            case Keys.Delete:
                if (_list.Focused)
                {
                    if (_list.CheckedCount > 0) _ = BatchUninstallAsync();
                    else _ = UninstallSelectedAsync();
                }
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _windowButtons?.Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Theme.Changed -= HandleThemeChanged;
            _toastTimer.Dispose();
            _logo?.Dispose();
        }
        base.Dispose(disposing);
    }
}

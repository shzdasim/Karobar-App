#if WINDOWS_DESKTOP
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Karobar.Background;
using Karobar.Host.Mascot;

namespace Karobar.Host.GuideBot;

/// <summary>
/// Persistent Native Windows Docked Right-Hand GuideBot Panel (`DockStyle.Right`).
/// Survives all Waseela tab switches, shrinks `_tabContentPanel` cleanly with zero WebView2 airspace overlap,
/// supports EN &amp; Urdu RTL, AI Chat with Slash Commands &amp; Pre-DB Diff Approval Cards, Screen Guide (80+ screens),
/// Live Active-Tab Spotlight Tour triggers, and Native Singleton Background Task telemetry.
/// </summary>
public sealed class NativeGuideBotDrawer : Panel
{
    private const int CompactWidth = 412;
    private const int WideWidth = 530;

    private static readonly Color BgRoot = Color.FromArgb(15, 23, 42);        // #0f172a
    private static readonly Color BgSurface = Color.FromArgb(30, 41, 59);     // #1e293b
    private static readonly Color BgCard = Color.FromArgb(24, 34, 52);
    private static readonly Color BorderColor = Color.FromArgb(51, 65, 85);   // #334155
    private static readonly Color TextPrimary = Color.FromArgb(248, 250, 252);
    private static readonly Color TextMuted = Color.FromArgb(148, 163, 184);
    private static readonly Color AccentBlue = Color.FromArgb(37, 99, 235);
    private static readonly Color AccentCyan = Color.FromArgb(14, 165, 233);
    private static readonly Color AccentEmerald = Color.FromArgb(16, 185, 129);
    private static readonly Color AccentAmber = Color.FromArgb(245, 158, 11);
    private static readonly Color AccentRed = Color.FromArgb(220, 38, 38);

    private readonly GuideBotSessionController _session;
    private readonly MascotPreferencesStore _prefsStore;
    private readonly Panel _headerAvatarCanvas;
    private readonly Label _screenSubtitleLabel;
    private readonly Button _langBtn;
    private readonly Button _widthBtn;
    private readonly Button _tabChatBtn;
    private readonly Button _tabGuideBtn;
    private readonly Button _tabTasksBtn;

    private readonly Panel _chatTabPanel;
    private readonly FlowLayoutPanel _chatFeedPanel;
    private readonly TextBox _composerBox;
    private readonly Button _sendBtn;
    private readonly ContextMenuStrip _slashMenu;

    private readonly Panel _guideTabPanel;
    private readonly TextBox _screenSearchBox;
    private readonly ComboBox _categoryFilterBox;
    private readonly Panel _guideDetailScroll;
    private readonly ListBox _directoryList;
    private string _activeBlueprintSubTab = "overview";
    private CubeScreenGuideInfo? _previewedScreen;

    private readonly Panel _tasksTabPanel;
    private readonly TextBox _taskFilterBox;
    private readonly ProgressBar _taskProgressBar;
    private readonly Label _taskStatusLabel;
    private readonly Label _metricScannedLbl;
    private readonly Label _metricFilledLbl;
    private readonly Label _metricSkippedLbl;
    private readonly Label _metricRemainingLbl;
    private readonly Button _taskPreviewBtn;
    private readonly Button _taskStartBtn;
    private readonly Button _taskStopBtn;
    private readonly Button _taskExportCsvBtn;
    private readonly ListView _taskReportList;

    private readonly System.Windows.Forms.Timer _avatarAnimTimer;
    private double _avatarAnimSec;
    private int _avatarTrickIndex;
    private int _headerClickCount;
    private string _activeMainTab = "chat"; // chat | guide | tasks
    private bool _isSending;

    public event Action<string, string>? NavigateRequested;
    public event Action<string>? StartSpotlightTourRequested;
    public event Action? DrawerVisibilityChanged;

    public NativeGuideBotDrawer(GuideBotSessionController session, MascotPreferencesStore prefsStore)
    {
        _session = session;
        _prefsStore = prefsStore;

        Dock = DockStyle.Right;
        Width = _prefsStore.Current.DrawerWide ? WideWidth : CompactWidth;
        BackColor = BgRoot;
        ForeColor = TextPrimary;
        Visible = false;
        Padding = new Padding(1, 0, 0, 0);

        _session.Language = _prefsStore.Current.Language;

        // ── Header Tier 1 + Tier 2 ───────────────────────────────────────────
        var headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 98,
            BackColor = BgSurface
        };
        headerPanel.Paint += (_, e) =>
        {
            using var pen = new Pen(BorderColor, 1f);
            e.Graphics.DrawLine(pen, 0, headerPanel.Height - 1, headerPanel.Width, headerPanel.Height - 1);
        };

        _headerAvatarCanvas = new Panel
        {
            Left = 10,
            Top = 8,
            Width = 44,
            Height = 44,
            BackColor = BgCard,
            Cursor = Cursors.Hand
        };
        _headerAvatarCanvas.Paint += OnHeaderAvatarPaint;
        _headerAvatarCanvas.Click += (_, _) =>
        {
            _avatarTrickIndex = (_avatarTrickIndex + 1) % MascotRoamingController.StationaryTricks.Length;
            _headerClickCount++;
            if (_headerClickCount >= 7)
            {
                _headerClickCount = 0;
                _prefsStore.Update(p => p.Skin = MascotPreferences.NextSkin(p.Skin));
            }
            _headerAvatarCanvas.Invalidate();
        };
        headerPanel.Controls.Add(_headerAvatarCanvas);

        var titleLbl = new Label
        {
            Text = "Cube AI Copilot",
            Left = 62,
            Top = 9,
            AutoSize = true,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = TextPrimary
        };
        headerPanel.Controls.Add(titleLbl);

        _screenSubtitleLabel = new Label
        {
            Text = "/dashboard",
            Left = 62,
            Top = 31,
            Width = 170,
            Height = 18,
            AutoEllipsis = true,
            Font = new Font("Segoe UI", 8.2f, FontStyle.Regular),
            ForeColor = AccentCyan
        };
        headerPanel.Controls.Add(_screenSubtitleLabel);

        // Top-right action buttons
        var closeBtn = CreateHeaderBtn("×", 30, (_, _) => HideDrawer());
        closeBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        closeBtn.Location = new Point(Width - 38, 12);

        _widthBtn = CreateHeaderBtn(_prefsStore.Current.DrawerWide ? "⇥" : "⇤", 30, (_, _) => ToggleWidth());
        _widthBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _widthBtn.Location = new Point(Width - 72, 12);

        var settingsBtn = CreateHeaderBtn("⚙", 30, (_, _) => NavigateRequested?.Invoke("/settings#cube", "Cube AI Settings"));
        settingsBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        settingsBtn.Location = new Point(Width - 106, 12);

        _langBtn = CreateHeaderBtn(_session.Language == "ur" ? "اردو" : "EN", 42, (_, _) => ToggleLanguage());
        _langBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _langBtn.Location = new Point(Width - 152, 12);

        headerPanel.Controls.AddRange([_langBtn, settingsBtn, _widthBtn, closeBtn]);

        // Tier 2: 3-Tab Switcher
        var tabBar = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 36,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = BgRoot,
            Padding = new Padding(8, 4, 8, 4)
        };
        tabBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        tabBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        tabBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));

        _tabChatBtn = CreateTabSwitchButton("AI Chat", () => SwitchMainTab("chat"));
        _tabGuideBtn = CreateTabSwitchButton("Screen Guide", () => SwitchMainTab("guide"));
        _tabTasksBtn = CreateTabSwitchButton("Tasks", () => SwitchMainTab("tasks"));
        tabBar.Controls.Add(_tabChatBtn, 0, 0);
        tabBar.Controls.Add(_tabGuideBtn, 1, 0);
        tabBar.Controls.Add(_tabTasksBtn, 2, 0);
        headerPanel.Controls.Add(tabBar);

        // ── Tab 1: AI Chat ───────────────────────────────────────────────────
        _chatTabPanel = new Panel { Dock = DockStyle.Fill, BackColor = BgRoot };

        _slashMenu = BuildSlashCommandMenu();

        var composerBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 82,
            BackColor = BgSurface,
            Padding = new Padding(8)
        };
        var slashBtn = new Button
        {
            Text = "/",
            Width = 32,
            Height = 34,
            Left = 8,
            Top = 10,
            FlatStyle = FlatStyle.Flat,
            BackColor = BgCard,
            ForeColor = AccentCyan,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        slashBtn.FlatAppearance.BorderColor = BorderColor;
        slashBtn.Click += (_, _) => _slashMenu.Show(slashBtn, new Point(0, -_slashMenu.Height));

        _sendBtn = new Button
        {
            Text = "Send",
            Width = 66,
            Height = 34,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Left = Width - 78,
            Top = 10,
            FlatStyle = FlatStyle.Flat,
            BackColor = AccentBlue,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 8.8f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        _sendBtn.FlatAppearance.BorderSize = 0;
        _sendBtn.Click += async (_, _) => await HandleComposerSubmitAsync();

        _composerBox = new TextBox
        {
            Multiline = true,
            Left = 46,
            Top = 8,
            Width = Math.Max(160, Width - 130),
            Height = 46,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = BgRoot,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9.2f),
            PlaceholderText = "Ask Cube anything or type / for commands…"
        };
        _composerBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                await HandleComposerSubmitAsync();
            }
        };

        var hintLbl = new Label
        {
            Text = "Enter Send · Shift+Enter New line · / Commands · Ctrl+G Toggle",
            Left = 10,
            Top = 58,
            AutoSize = true,
            Font = new Font("Segoe UI", 7.3f),
            ForeColor = TextMuted
        };

        composerBar.Controls.AddRange([slashBtn, _composerBox, _sendBtn, hintLbl]);

        _chatFeedPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = BgRoot,
            Padding = new Padding(10, 10, 10, 12)
        };
        _chatFeedPanel.SizeChanged += (_, _) => ResizeChatCards();

        _chatTabPanel.Controls.Add(_chatFeedPanel);
        _chatTabPanel.Controls.Add(composerBar);

        // ── Tab 2: Screen Guide & 80+ Screen Directory ───────────────────────
        _guideTabPanel = new Panel { Dock = DockStyle.Fill, BackColor = BgRoot, Visible = false, Padding = new Padding(10) };

        var guideTopBar = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = BgRoot };
        _screenSearchBox = new TextBox
        {
            Left = 0, Top = 0, Width = 230, Height = 26,
            BackColor = BgSurface, ForeColor = TextPrimary, BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "Search 80+ screens (e.g. POS, Ledger, Batch)…"
        };
        _screenSearchBox.TextChanged += (_, _) => PopulateDirectoryList();

        _categoryFilterBox = new ComboBox
        {
            Left = 236, Top = 0, Width = 148, DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = BgSurface, ForeColor = TextPrimary, FlatStyle = FlatStyle.Flat
        };
        _categoryFilterBox.SelectedIndexChanged += (_, _) => PopulateDirectoryList();

        var startTourBtn = new Button
        {
            Text = "▶ Live On-Screen Spotlight Tour",
            Left = 0, Top = 32, Width = 225, Height = 28,
            FlatStyle = FlatStyle.Flat, BackColor = AccentCyan, ForeColor = Color.White,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), Cursor = Cursors.Hand
        };
        startTourBtn.FlatAppearance.BorderSize = 0;
        startTourBtn.Click += (_, _) =>
        {
            var guide = _previewedScreen ?? _session.GetCurrentScreenGuide();
            StartSpotlightTourRequested?.Invoke(guide?.WorkflowId ?? "dynamic_screen_tour");
        };

        var openGuideRouteBtn = new Button
        {
            Text = "Open Screen →",
            Left = 232, Top = 32, Width = 152, Height = 28,
            FlatStyle = FlatStyle.Flat, BackColor = BgSurface, ForeColor = TextPrimary,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), Cursor = Cursors.Hand
        };
        openGuideRouteBtn.FlatAppearance.BorderColor = BorderColor;
        openGuideRouteBtn.Click += (_, _) =>
        {
            var g = _previewedScreen ?? _session.GetCurrentScreenGuide();
            if (g != null) NavigateRequested?.Invoke(g.Route, g.TitleEn);
        };
        guideTopBar.Controls.AddRange([_screenSearchBox, _categoryFilterBox, startTourBtn, openGuideRouteBtn]);

        _directoryList = new ListBox
        {
            Dock = DockStyle.Top,
            Height = 140,
            BackColor = BgSurface,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 8.5f)
        };
        _directoryList.SelectedIndexChanged += (_, _) =>
        {
            if (_directoryList.SelectedItem is ScreenListItem item)
            {
                _previewedScreen = item.Screen;
                RenderScreenBlueprint();
            }
        };

        _guideDetailScroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = BgRoot,
            Padding = new Padding(0, 8, 0, 8)
        };

        _guideTabPanel.Controls.Add(_guideDetailScroll);
        _guideTabPanel.Controls.Add(_directoryList);
        _guideTabPanel.Controls.Add(guideTopBar);

        // ── Tab 3: Native Background Tasks (Bulk Formulation Auto-Fill) ──────
        _tasksTabPanel = new Panel { Dock = DockStyle.Fill, BackColor = BgRoot, Visible = false, Padding = new Padding(10) };

        var taskHeader = new Label
        {
            Text = "DRAP Bulk Formulation Auto-Fill (Runs Natively Across All Tabs)",
            Dock = DockStyle.Top,
            Height = 24,
            Font = new Font("Segoe UI", 9.2f, FontStyle.Bold),
            ForeColor = TextPrimary
        };

        var taskControls = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = BgRoot };
        _taskFilterBox = new TextBox
        {
            Left = 0, Top = 4, Width = 380, Height = 26,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = BgSurface, ForeColor = TextPrimary, BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "Optional product filter (leave blank for all missing formulations)…"
        };
        _taskPreviewBtn = CreateActionBtn("Preview", AccentAmber, 0, 36, 92, async (_, _) => await RunTaskPreviewAsync());
        _taskStartBtn = CreateActionBtn("Start Auto-Fill", AccentEmerald, 98, 36, 114, (_, _) => StartBackgroundAutofill());
        _taskStopBtn = CreateActionBtn("Stop", AccentRed, 218, 36, 68, (_, _) => _session.BackgroundTaskManager?.Stop());
        _taskExportCsvBtn = CreateActionBtn("CSV", BgSurface, 292, 36, 64, (_, _) => ExportTaskReportCsv());
        taskControls.Controls.AddRange([_taskFilterBox, _taskPreviewBtn, _taskStartBtn, _taskStopBtn, _taskExportCsvBtn]);

        _taskProgressBar = new ProgressBar { Dock = DockStyle.Top, Height = 14, Minimum = 0, Maximum = 100 };
        _taskStatusLabel = new Label
        {
            Text = "Idle — click Preview to inspect DRAP matches before posting.",
            Dock = DockStyle.Top,
            Height = 26,
            Font = new Font("Segoe UI", 8.2f),
            ForeColor = TextMuted
        };

        var metricsGrid = new TableLayoutPanel { Dock = DockStyle.Top, Height = 54, ColumnCount = 4, RowCount = 1, BackColor = BgSurface };
        for (var i = 0; i < 4; i++) metricsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        _metricScannedLbl = CreateMetricCard(metricsGrid, 0, "Scanned", TextPrimary);
        _metricFilledLbl = CreateMetricCard(metricsGrid, 1, "Filled", AccentEmerald);
        _metricSkippedLbl = CreateMetricCard(metricsGrid, 2, "Skipped", AccentAmber);
        _metricRemainingLbl = CreateMetricCard(metricsGrid, 3, "Remaining", AccentCyan);

        _taskReportList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            BackColor = BgSurface,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 8.2f)
        };
        _taskReportList.Columns.Add("Status", 66);
        _taskReportList.Columns.Add("Product", 150);
        _taskReportList.Columns.Add("Formulation / Reason", 170);

        _tasksTabPanel.Controls.Add(_taskReportList);
        _tasksTabPanel.Controls.Add(metricsGrid);
        _tasksTabPanel.Controls.Add(_taskStatusLabel);
        _tasksTabPanel.Controls.Add(_taskProgressBar);
        _tasksTabPanel.Controls.Add(taskControls);
        _tasksTabPanel.Controls.Add(taskHeader);

        // Assemble main drawer
        Controls.Add(_tasksTabPanel);
        Controls.Add(_guideTabPanel);
        Controls.Add(_chatTabPanel);
        Controls.Add(headerPanel);

        // Subscribe to session & background task updates
        _session.ContextChanged += () => SafeUi(RefreshActiveScreenContext);
        _session.MessagesChanged += () => SafeUi(RenderChatFeed);
        if (_session.BackgroundTaskManager != null)
        {
            _session.BackgroundTaskManager.StateChanged += snap => SafeUi(() => ApplyTaskSnapshot(snap));
        }

        _avatarAnimTimer = new System.Windows.Forms.Timer { Interval = 80 };
        _avatarAnimTimer.Tick += (_, _) =>
        {
            if (!Visible) return;
            _avatarAnimSec += 0.08;
            if ((int)(_avatarAnimSec / 4.2) != (int)((_avatarAnimSec - 0.08) / 4.2))
            {
                _avatarTrickIndex = (_avatarTrickIndex + 1) % MascotRoamingController.StationaryTricks.Length;
            }
            _headerAvatarCanvas.Invalidate();
        };

        InitCategories();
        RefreshActiveScreenContext();
        SwitchMainTab("chat");
        RenderChatFeed();
        if (_session.BackgroundTaskManager != null) ApplyTaskSnapshot(_session.BackgroundTaskManager.GetSnapshot());
    }

    public void ShowDrawer(string? tab = null)
    {
        if (!string.IsNullOrWhiteSpace(tab)) SwitchMainTab(tab);
        Visible = true;
        BringToFront();
        _avatarAnimTimer.Start();
        RefreshActiveScreenContext();
        _composerBox.Focus();
        DrawerVisibilityChanged?.Invoke();
    }

    public void HideDrawer()
    {
        Visible = false;
        _avatarAnimTimer.Stop();
        DrawerVisibilityChanged?.Invoke();
    }

    public void ToggleDrawer()
    {
        if (Visible) HideDrawer();
        else ShowDrawer();
    }

    public void AskFromSpotlightStep(string promptText)
    {
        ShowDrawer("chat");
        _ = _session.SendQuestionAsync(promptText);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(BorderColor, 1f);
        e.Graphics.DrawLine(pen, 0, 0, 0, Height);
    }

    private void OnHeaderAvatarPaint(object? sender, PaintEventArgs e)
    {
        var trick = _isSending ? MascotTrick.Scan : MascotRoamingController.StationaryTricks[_avatarTrickIndex];
        using var bmp = new Bitmap(128, 128, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            MascotVectorRenderer.DrawMascot(g, 128, 128, _prefsStore.Current.Skin, trick, _avatarAnimSec, _avatarAnimSec % 4.2, 0, 0, false, _isSending ? 1 : 0, false, true, null);
        }
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.Clear(BgCard);
        e.Graphics.DrawImage(bmp, new Rectangle(-4, -8, 52, 54));
    }

    private void ToggleWidth()
    {
        var wide = Width <= CompactWidth;
        Width = wide ? WideWidth : CompactWidth;
        _widthBtn.Text = wide ? "⇥" : "⇤";
        _prefsStore.Update(p => p.DrawerWide = wide);
        ResizeChatCards();
        DrawerVisibilityChanged?.Invoke();
    }

    private void ToggleLanguage()
    {
        _session.Language = _session.Language == "ur" ? "en" : "ur";
        _langBtn.Text = _session.Language == "ur" ? "اردو" : "EN";
        _prefsStore.Update(p => p.Language = _session.Language);
        RenderChatFeed();
        RenderScreenBlueprint();
    }

    private void SwitchMainTab(string tab)
    {
        _activeMainTab = tab;
        _chatTabPanel.Visible = tab == "chat";
        _guideTabPanel.Visible = tab == "guide";
        _tasksTabPanel.Visible = tab == "tasks";

        StyleTabButton(_tabChatBtn, tab == "chat");
        StyleTabButton(_tabGuideBtn, tab == "guide");
        StyleTabButton(_tabTasksBtn, tab == "tasks");

        if (tab == "guide")
        {
            PopulateDirectoryList();
            RenderScreenBlueprint();
        }
    }

    private void RefreshActiveScreenContext()
    {
        var g = _session.GetCurrentScreenGuide();
        var title = g != null ? (_session.Language == "ur" && !string.IsNullOrWhiteSpace(g.TitleUr) ? g.TitleUr : g.TitleEn) : _session.CurrentRoute;
        _screenSubtitleLabel.Text = string.IsNullOrEmpty(_session.ActiveModal) ? title : $"{title} • {_session.ActiveModal}";
        _previewedScreen = g;
        if (_activeMainTab == "guide") RenderScreenBlueprint();
    }

    private async Task HandleComposerSubmitAsync()
    {
        if (_isSending) return;
        var text = _composerBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        _composerBox.Clear();

        if (text.Equals("/clear", StringComparison.OrdinalIgnoreCase))
        {
            _session.ClearHistory();
            return;
        }
        if (text.Equals("/tasks", StringComparison.OrdinalIgnoreCase))
        {
            SwitchMainTab("tasks");
            return;
        }
        if (text.Equals("/walkthrough", StringComparison.OrdinalIgnoreCase))
        {
            var g = _session.GetCurrentScreenGuide();
            StartSpotlightTourRequested?.Invoke(g?.WorkflowId ?? "dynamic_screen_tour");
            return;
        }

        _isSending = true;
        _sendBtn.Enabled = false;
        _sendBtn.Text = "…";
        try
        {
            await _session.SendQuestionAsync(text);
        }
        finally
        {
            _isSending = false;
            _sendBtn.Enabled = true;
            _sendBtn.Text = "Send";
            _composerBox.Focus();
        }
    }

    private void RenderChatFeed()
    {
        _chatFeedPanel.SuspendLayout();
        _chatFeedPanel.Controls.Clear();

        var cardW = Math.Max(260, _chatFeedPanel.ClientSize.Width - 28);
        var isUr = _session.Language == "ur";

        if (_session.Messages.Count == 0)
        {
            var hero = new Panel { Width = cardW, AutoSize = true, BackColor = BgSurface, Padding = new Padding(12), Margin = new Padding(0, 0, 0, 10) };
            var heroFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            heroFlow.Controls.Add(new Label
            {
                Text = isUr ? "خوش آمدید! میں کیوب اے آئی ہوں" : "Welcome to Cube AI Copilot (Native Windows)",
                AutoSize = true, MaximumSize = new Size(cardW - 24, 0),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = TextPrimary
            });
            heroFlow.Controls.Add(new Label
            {
                Text = isUr
                    ? "کسی بھی اسکرین کی وضاحت پوچھیں، پروڈکٹس/برانڈز شامل کریں، یا خودکار فارمولیشن چلائیں۔"
                    : "Ask how any ERP screen works, run non-destructive data tasks with Pre-DB preview guards, or launch a live Spotlight Tour.",
                AutoSize = true, MaximumSize = new Size(cardW - 24, 0), Margin = new Padding(0, 4, 0, 10),
                Font = new Font("Segoe UI", 8.5f), ForeColor = TextMuted
            });

            var actions = new (string Label, Action Handler)[]
            {
                (isUr ? "اس اسکرین کی وضاحت کریں" : "Explain Current Screen", () => _ = _session.SendQuestionAsync("Explain this screen step by step")),
                (isUr ? "نئی پروڈکٹ شامل کریں" : "+ Add New Product", () => _ = _session.SendQuestionAsync("/add-product")),
                (isUr ? "خالی فارمولیشن خودکار بھریں" : "Auto-Fill Formulations", () => SwitchMainTab("tasks")),
                (isUr ? "لائیو اسپاٹ لائٹ ٹور" : "▶ Live Spotlight Tour", () => StartSpotlightTourRequested?.Invoke(_session.GetCurrentScreenGuide()?.WorkflowId ?? "dynamic_screen_tour"))
            };

            foreach (var (lbl, handler) in actions)
            {
                var b = new Button
                {
                    Text = lbl,
                    Width = cardW - 28,
                    Height = 32,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = BgCard,
                    ForeColor = TextPrimary,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = new Font("Segoe UI", 8.8f, FontStyle.Bold),
                    Margin = new Padding(0, 3, 0, 3),
                    Cursor = Cursors.Hand
                };
                b.FlatAppearance.BorderColor = BorderColor;
                b.Click += (_, _) => handler();
                heroFlow.Controls.Add(b);
            }

            hero.Controls.Add(heroFlow);
            _chatFeedPanel.Controls.Add(hero);
        }
        else
        {
            foreach (var msg in _session.Messages)
            {
                _chatFeedPanel.Controls.Add(BuildMessageCard(msg, cardW, isUr));
            }
        }

        _chatFeedPanel.ResumeLayout();
        if (_chatFeedPanel.Controls.Count > 0)
        {
            _chatFeedPanel.ScrollControlIntoView(_chatFeedPanel.Controls[^1]);
        }
    }

    private Control BuildMessageCard(GuideBotChatMessage msg, int cardW, bool isUr)
    {
        var isUser = msg.Role == "user";
        var card = new Panel
        {
            Width = cardW,
            AutoSize = true,
            BackColor = isUser ? AccentBlue : BgSurface,
            Padding = new Padding(10),
            Margin = new Padding(isUser ? 26 : 0, 0, isUser ? 0 : 12, 8),
            RightToLeft = isUr ? RightToLeft.Yes : RightToLeft.No
        };

        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };

        if (!isUser)
        {
            var title = isUr && !string.IsNullOrWhiteSpace(msg.TitleUr) ? msg.TitleUr : msg.TitleEn;
            stack.Controls.Add(new Label
            {
                Text = title,
                AutoSize = true,
                MaximumSize = new Size(cardW - 24, 0),
                Font = new Font("Segoe UI", 9.2f, FontStyle.Bold),
                ForeColor = AccentCyan
            });
        }

        var body = isUr && !string.IsNullOrWhiteSpace(msg.AnswerUr) ? msg.AnswerUr : msg.AnswerEn;
        body = Regex.Replace(body, @"\*\*(.*?)\*\*", "$1");
        stack.Controls.Add(new Label
        {
            Text = body,
            AutoSize = true,
            MaximumSize = new Size(cardW - 24, 0),
            Margin = new Padding(0, 4, 0, 4),
            Font = new Font("Segoe UI", 8.8f),
            ForeColor = TextPrimary
        });

        // Staged Pre-Database Permission Card
        if (!isUser && msg.TaskMeta != null &&
            msg.TaskMeta.TryGetValue("awaiting_permission", out var ap) &&
            (ap is true || string.Equals(ap?.ToString(), "true", StringComparison.OrdinalIgnoreCase)))
        {
            var guardBox = new FlowLayoutPanel
            {
                Width = cardW - 24,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.FromArgb(46, 36, 18),
                Padding = new Padding(8),
                Margin = new Padding(0, 6, 0, 4)
            };
            guardBox.Controls.Add(new Label
            {
                Text = "⚠ NOT POSTED TO DATABASE YET — Approval Required",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.2f, FontStyle.Bold),
                ForeColor = AccentAmber
            });

            if (msg.TaskMeta.TryGetValue("preview_result", out var prObj) && prObj is Dictionary<string, object?> pr)
            {
                if (pr.TryGetValue("database_table", out var tbl) && tbl != null)
                {
                    guardBox.Controls.Add(new Label
                    {
                        Text = $"Table: {tbl}  |  Operation: {pr.GetValueOrDefault("operation") ?? "WRITE"}",
                        AutoSize = true,
                        Font = new Font("Segoe UI", 7.8f),
                        ForeColor = TextMuted
                    });
                }
                if (pr.TryGetValue("records", out var recsObj) && recsObj is System.Collections.IEnumerable rawRecs)
                {
                    foreach (var rd in rawRecs.Cast<object?>().OfType<Dictionary<string, object?>>().Take(5))
                    {
                        var line = $"• {rd.GetValueOrDefault("title")}";
                        if (rd.TryGetValue("fields", out var fObj) && fObj is System.Collections.IEnumerable rawFields)
                        {
                            var fParts = rawFields.Cast<object?>().OfType<Dictionary<string, object?>>()
                                .Take(4)
                                .Select(f => $"{f.GetValueOrDefault("field")}: {f.GetValueOrDefault("before")} → {f.GetValueOrDefault("after")}");
                            line += "\n   " + string.Join(" | ", fParts);
                        }
                        guardBox.Controls.Add(new Label
                        {
                            Text = line,
                            AutoSize = true,
                            MaximumSize = new Size(cardW - 44, 0),
                            Font = new Font("Segoe UI", 8f),
                            ForeColor = TextPrimary
                        });
                    }
                }
            }

            var btnRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 6, 0, 0) };
            var approveBtn = CreateSmallPillBtn("✓ Approve & Post", AccentEmerald, async () =>
            {
                var pendingAction = msg.TaskMeta.GetValueOrDefault("pending_action")?.ToString()
                    ?? msg.TaskMeta.GetValueOrDefault("action")?.ToString();
                if (pendingAction == "bulk_autofill_missing_formulations")
                {
                    SwitchMainTab("tasks");
                    StartBackgroundAutofill();
                }
                else
                {
                    await _session.SendQuestionAsync("yes", userConfirmed: true);
                }
            });
            var denyBtn = CreateSmallPillBtn("✕ Deny", AccentRed, async () => await _session.SendQuestionAsync("cancel"));
            btnRow.Controls.AddRange([approveBtn, denyBtn]);
            guardBox.Controls.Add(btnRow);
            stack.Controls.Add(guardBox);
        }

        // DB Match Disambiguation Buttons
        if (!isUser && msg.TaskMeta != null && msg.TaskMeta.TryGetValue("db_matches", out var dmObj) && dmObj is System.Collections.IEnumerable dmEnum)
        {
            var dmItems = dmEnum.Cast<object?>().OfType<Dictionary<string, object?>>().Take(5).ToList();
            if (dmItems.Count > 0)
            {
                var matchRow = new FlowLayoutPanel { Width = cardW - 24, AutoSize = true, Margin = new Padding(0, 4, 0, 2) };
                foreach (var item in dmItems)
                {
                    var id = item.GetValueOrDefault("id")?.ToString() ?? "";
                    var name = item.GetValueOrDefault("name")?.ToString() ?? "";
                    matchRow.Controls.Add(CreateSmallPillBtn($"✓ {name}", BgCard, async () => await _session.SendQuestionAsync(id)));
                }
                stack.Controls.Add(matchRow);
            }
        }

        // Route Navigation Links
        if (!isUser && msg.Links.Count > 0)
        {
            var linksRow = new FlowLayoutPanel { Width = cardW - 24, AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
            foreach (var lnk in msg.Links.Take(3))
            {
                var path = lnk.GetValueOrDefault("path", "/dashboard");
                var lbl = isUr && !string.IsNullOrWhiteSpace(lnk.GetValueOrDefault("ur")) ? lnk["ur"] : lnk.GetValueOrDefault("en", path);
                linksRow.Controls.Add(CreateSmallPillBtn($"→ {lbl}", BgCard, () => NavigateRequested?.Invoke(path, lbl)));
            }
            stack.Controls.Add(linksRow);
        }

        card.Controls.Add(stack);
        return card;
    }

    private void ResizeChatCards()
    {
        var cardW = Math.Max(260, _chatFeedPanel.ClientSize.Width - 28);
        foreach (Control c in _chatFeedPanel.Controls) c.Width = cardW;
    }

    private void InitCategories()
    {
        _categoryFilterBox.Items.Clear();
        _categoryFilterBox.Items.Add("All Modules");
        foreach (var cat in _session.KnowledgeRetriever.GetCategories())
        {
            var key = cat.GetValueOrDefault("key", "");
            if (!string.IsNullOrWhiteSpace(key) && key != "all")
            {
                _categoryFilterBox.Items.Add(cat.GetValueOrDefault("en", key));
            }
        }
        _categoryFilterBox.SelectedIndex = 0;
        PopulateDirectoryList();
    }

    private void PopulateDirectoryList()
    {
        var q = _screenSearchBox.Text.Trim().ToLowerInvariant();
        var catIdx = _categoryFilterBox.SelectedIndex;
        var cats = _session.KnowledgeRetriever.GetCategories().Where(c => c.GetValueOrDefault("key") != "all").ToList();
        var selectedCatKey = catIdx > 0 && catIdx - 1 < cats.Count ? cats[catIdx - 1].GetValueOrDefault("key", "") : "";

        _directoryList.BeginUpdate();
        _directoryList.Items.Clear();
        foreach (var sc in _session.KnowledgeRetriever.GetScreens())
        {
            if (!string.IsNullOrEmpty(selectedCatKey) && !sc.Category.Equals(selectedCatKey, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.IsNullOrEmpty(q) &&
                !sc.TitleEn.ToLowerInvariant().Contains(q) &&
                !sc.Route.ToLowerInvariant().Contains(q) &&
                !sc.SummaryEn.ToLowerInvariant().Contains(q))
                continue;
            _directoryList.Items.Add(new ScreenListItem(sc));
        }
        _directoryList.EndUpdate();
    }

    private void RenderScreenBlueprint()
    {
        _guideDetailScroll.SuspendLayout();
        _guideDetailScroll.Controls.Clear();

        var g = _previewedScreen ?? _session.GetCurrentScreenGuide();
        if (g == null)
        {
            _guideDetailScroll.ResumeLayout();
            return;
        }

        var isUr = _session.Language == "ur";
        var w = Math.Max(260, _guideDetailScroll.ClientSize.Width - 22);
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            RightToLeft = isUr ? RightToLeft.Yes : RightToLeft.No
        };

        flow.Controls.Add(new Label
        {
            Text = isUr && !string.IsNullOrWhiteSpace(g.TitleUr) ? g.TitleUr : g.TitleEn,
            AutoSize = true, MaximumSize = new Size(w, 0),
            Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = AccentCyan
        });
        flow.Controls.Add(new Label
        {
            Text = g.Route,
            AutoSize = true, Font = new Font("Consolas", 8.2f), ForeColor = TextMuted
        });

        // 4 Blueprint Sub-Tabs
        var subRow = new FlowLayoutPanel { Width = w, AutoSize = true, Margin = new Padding(0, 6, 0, 6) };
        foreach (var (key, label) in new[] { ("overview", "Overview"), ("fields", "Fields"), ("steps", "Steps"), ("impact", "4-Pillar Impact") })
        {
            var active = _activeBlueprintSubTab == key;
            subRow.Controls.Add(CreateSmallPillBtn(label, active ? AccentBlue : BgSurface, () =>
            {
                _activeBlueprintSubTab = key;
                RenderScreenBlueprint();
            }));
        }
        flow.Controls.Add(subRow);

        var sb = new StringBuilder();
        if (_activeBlueprintSubTab == "overview")
        {
            sb.AppendLine(isUr && !string.IsNullOrWhiteSpace(g.SummaryUr) ? g.SummaryUr : g.SummaryEn);
            var feats = isUr && g.FeaturesUr.Count > 0 ? g.FeaturesUr : g.FeaturesEn;
            if (feats.Count > 0)
            {
                sb.AppendLine();
                foreach (var f in feats.Take(6)) sb.AppendLine($"• {f}");
            }
            var ex = isUr && !string.IsNullOrWhiteSpace(g.ExampleUr) ? g.ExampleUr : g.ExampleEn;
            if (!string.IsNullOrWhiteSpace(ex))
            {
                sb.AppendLine($"\nExample:\n{ex}");
            }
            var cm = isUr && !string.IsNullOrWhiteSpace(g.CommonMistakeUr) ? g.CommonMistakeUr : g.CommonMistakeEn;
            if (!string.IsNullOrWhiteSpace(cm))
            {
                sb.AppendLine($"\nCommon Mistake to Avoid:\n{cm}");
            }
        }
        else if (_activeBlueprintSubTab == "fields")
        {
            if (g.Fields.Count == 0) sb.AppendLine("No field glossary documented for this screen.");
            foreach (var f in g.Fields)
            {
                var desc = isUr && !string.IsNullOrWhiteSpace(f.GetValueOrDefault("ur")) ? f["ur"] : f.GetValueOrDefault("en", "");
                sb.AppendLine($"• {f.GetValueOrDefault("name")}: {desc}");
            }
        }
        else if (_activeBlueprintSubTab == "steps")
        {
            var steps = isUr && g.StepsUr.Count > 0 ? g.StepsUr : g.StepsEn;
            for (var i = 0; i < steps.Count; i++) sb.AppendLine($"{i + 1}. {steps[i]}");
            if (g.Shortcuts.Count > 0)
            {
                sb.AppendLine("\nKeyboard Shortcuts:");
                foreach (var sc in g.Shortcuts)
                {
                    var desc = isUr && !string.IsNullOrWhiteSpace(sc.GetValueOrDefault("ur")) ? sc["ur"] : sc.GetValueOrDefault("en", "");
                    sb.AppendLine($"• [{sc.GetValueOrDefault("key")}] {desc}");
                }
            }
        }
        else if (_activeBlueprintSubTab == "impact")
        {
            if (g.ImpactMatrix.Count > 0)
            {
                foreach (var im in g.ImpactMatrix)
                {
                    var desc = isUr && !string.IsNullOrWhiteSpace(im.GetValueOrDefault("ur")) ? im["ur"] : im.GetValueOrDefault("en", "");
                    sb.AppendLine($"• {im.GetValueOrDefault("pillar")}: {desc}");
                }
            }
            else
            {
                var imps = isUr && g.ImpactsUr.Count > 0 ? g.ImpactsUr : g.ImpactsEn;
                foreach (var imp in imps) sb.AppendLine($"• {imp}");
            }
        }

        flow.Controls.Add(new Label
        {
            Text = sb.ToString().Trim(),
            AutoSize = true, MaximumSize = new Size(w, 0),
            Font = new Font("Segoe UI", 8.6f), ForeColor = TextPrimary
        });

        _guideDetailScroll.Controls.Add(flow);
        _guideDetailScroll.ResumeLayout();
    }

    private async Task RunTaskPreviewAsync()
    {
        _taskPreviewBtn.Enabled = false;
        _taskStatusLabel.Text = "Running dry-run DRAP preview (0 DB writes)…";
        try
        {
            var res = await _session.PreviewFormulationAutofillAsync(_taskFilterBox.Text.Trim());
            var tm = res?.GetValueOrDefault("task_meta") as Dictionary<string, object?>;
            var pr = tm?.GetValueOrDefault("preview_result") as Dictionary<string, object?>;
            var totalMissing = Convert.ToInt32(pr?.GetValueOrDefault("total_missing") ?? 0);
            var resolvable = Convert.ToInt32(pr?.GetValueOrDefault("resolvable_in_preview") ?? 0);
            _taskStatusLabel.Text = $"Preview: {totalMissing} missing total, {resolvable} verified in sample batch (0 DB writes).";

            _taskReportList.BeginUpdate();
            _taskReportList.Items.Clear();
            if (tm?.GetValueOrDefault("updated_products") is System.Collections.IEnumerable rawUpds)
            {
                foreach (var u in rawUpds.Cast<object?>().OfType<Dictionary<string, object?>>())
                {
                    var li = new ListViewItem("PREVIEW") { ForeColor = AccentEmerald };
                    li.SubItems.Add(u.GetValueOrDefault("name")?.ToString() ?? "");
                    li.SubItems.Add(u.GetValueOrDefault("formulation")?.ToString() ?? "");
                    _taskReportList.Items.Add(li);
                }
            }
            _taskReportList.EndUpdate();
        }
        finally
        {
            _taskPreviewBtn.Enabled = true;
        }
    }

    private void StartBackgroundAutofill()
    {
        var mgr = _session.BackgroundTaskManager;
        if (mgr == null) return;
        mgr.StartBulkAutofill(new CubeUserContext { UserId = 1, IsAdmin = true }, _taskFilterBox.Text.Trim(), 20);
    }

    private void ApplyTaskSnapshot(CubeBackgroundTaskSnapshot snap)
    {
        _taskProgressBar.Value = Math.Clamp(snap.Percent, 0, 100);
        _taskStatusLabel.Text = string.IsNullOrWhiteSpace(snap.Message) ? $"Status: {snap.Status}" : snap.Message;
        _metricScannedLbl.Text = $"{snap.ScannedCount}\nScanned";
        _metricFilledLbl.Text = $"{snap.UpdatedCount}\nFilled";
        _metricSkippedLbl.Text = $"{snap.SkippedCount}\nSkipped";
        _metricRemainingLbl.Text = $"{snap.RemainingCount}\nRemaining";

        _taskStartBtn.Enabled = snap.Status != "running";
        _taskStopBtn.Enabled = snap.Status == "running";
        _tabTasksBtn.Text = snap.Status == "running" ? $"Tasks ({snap.UpdatedCount})" : "Tasks";

        _taskReportList.BeginUpdate();
        _taskReportList.Items.Clear();
        foreach (var u in snap.UpdatedProducts.TakeLast(120))
        {
            var li = new ListViewItem("FILLED") { ForeColor = AccentEmerald };
            li.SubItems.Add(u.GetValueOrDefault("name")?.ToString() ?? "");
            li.SubItems.Add(u.GetValueOrDefault("formulation")?.ToString() ?? "");
            _taskReportList.Items.Add(li);
        }
        foreach (var s in snap.SkippedProducts.TakeLast(60))
        {
            var li = new ListViewItem("SKIPPED") { ForeColor = AccentAmber };
            li.SubItems.Add(s.GetValueOrDefault("name")?.ToString() ?? "");
            li.SubItems.Add(s.GetValueOrDefault("reason_en")?.ToString() ?? "");
            _taskReportList.Items.Add(li);
        }
        _taskReportList.EndUpdate();
    }

    private void ExportTaskReportCsv()
    {
        var snap = _session.BackgroundTaskManager?.GetSnapshot();
        if (snap == null || (snap.UpdatedProducts.Count == 0 && snap.SkippedProducts.Count == 0)) return;
        using var sfd = new SaveFileDialog
        {
            Filter = "CSV Files (*.csv)|*.csv",
            FileName = $"cube-formulations-report-{DateTime.Now:yyyyMMdd-HHmm}.csv"
        };
        if (sfd.ShowDialog() != DialogResult.OK) return;
        var sb = new StringBuilder();
        sb.AppendLine("Status,Product ID,Code,Product Name,Formulation / Reason");
        foreach (var u in snap.UpdatedProducts)
        {
            sb.AppendLine($"FILLED,{u.GetValueOrDefault("id")},\"{u.GetValueOrDefault("code")}\",\"{EscCsv(u.GetValueOrDefault("name"))}\",\"{EscCsv(u.GetValueOrDefault("formulation"))}\"");
        }
        foreach (var s in snap.SkippedProducts)
        {
            sb.AppendLine($"SKIPPED,{s.GetValueOrDefault("id")},\"{s.GetValueOrDefault("code")}\",\"{EscCsv(s.GetValueOrDefault("name"))}\",\"{EscCsv(s.GetValueOrDefault("reason_en"))}\"");
        }
        File.WriteAllText(sfd.FileName, sb.ToString(), new UTF8Encoding(true));
    }

    private static string EscCsv(object? v) => (v?.ToString() ?? "").Replace("\"", "\"\"");

    private ContextMenuStrip BuildSlashCommandMenu()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = BgSurface,
            ForeColor = TextPrimary,
            Renderer = new DarkMenuStripRenderer()
        };
        foreach (var cmd in GuideBotSessionController.SlashCommands)
        {
            var item = new ToolStripMenuItem($"{cmd.Command}  —  {cmd.LabelEn}");
            item.Click += (_, _) =>
            {
                _composerBox.Text = cmd.Template;
                _composerBox.SelectionStart = _composerBox.Text.Length;
                _composerBox.Focus();
            };
            menu.Items.Add(item);
        }
        return menu;
    }

    private Button CreateHeaderBtn(string text, int width, EventHandler onClick)
    {
        var b = new Button
        {
            Text = text,
            Width = width,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = BgCard,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderColor = BorderColor;
        b.Click += onClick;
        return b;
    }

    private Button CreateTabSwitchButton(string text, Action onClick)
    {
        var b = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8.4f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(2, 0, 2, 0)
        };
        b.FlatAppearance.BorderSize = 0;
        b.Click += (_, _) => onClick();
        return b;
    }

    private static void StyleTabButton(Button btn, bool active)
    {
        btn.BackColor = active ? AccentBlue : BgSurface;
        btn.ForeColor = active ? Color.White : TextMuted;
    }

    private static Button CreateActionBtn(string text, Color bg, int left, int top, int width, EventHandler onClick)
    {
        var b = new Button
        {
            Text = text, Left = left, Top = top, Width = width, Height = 28,
            FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = Color.White,
            Font = new Font("Segoe UI", 8.2f, FontStyle.Bold), Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderColor = BorderColor;
        b.Click += onClick;
        return b;
    }

    private static Button CreateSmallPillBtn(string text, Color bg, Action onClick)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, Height = 26,
            FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = TextPrimary,
            Font = new Font("Segoe UI", 7.8f, FontStyle.Bold), Padding = new Padding(4, 0, 4, 0),
            Margin = new Padding(0, 0, 5, 4), Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderColor = BorderColor;
        b.Click += (_, _) => onClick();
        return b;
    }

    private static Label CreateMetricCard(TableLayoutPanel grid, int col, string title, Color color)
    {
        var lbl = new Label
        {
            Text = $"0\n{title}",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 8.2f, FontStyle.Bold),
            ForeColor = color
        };
        grid.Controls.Add(lbl, col, 0);
        return lbl;
    }

    private void SafeUi(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(action);
        else action();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _avatarAnimTimer.Stop(); _avatarAnimTimer.Dispose(); } catch { }
            try { _slashMenu.Dispose(); } catch { }
        }
        base.Dispose(disposing);
    }

    private sealed class ScreenListItem
    {
        public CubeScreenGuideInfo Screen { get; }
        public ScreenListItem(CubeScreenGuideInfo screen) => Screen = screen;
        public override string ToString() => $"{Screen.TitleEn}  ({Screen.Route})";
    }
}
#endif

#if WINDOWS_DESKTOP
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Karobar.Host;

/// <summary>
/// Custom double-buffered Waseela-style top tab strip for single-window multi-tab ERP multitasking.
/// Features color-coded module dots, active accent indicator, close buttons, middle-click close,
/// right-click tab context menu, + New Tab button, and Waseela quick-launch pills (+POS, +Sale, +Purchase, +Products).
/// </summary>
public sealed class NativeTabStripControl : Control
{
    private readonly NativeTabWorkspace _workspace;
    private readonly List<TabHitRect> _tabRects = new();
    private readonly List<QuickButtonRect> _quickRects = new();
    private Rectangle _newTabButtonRect = Rectangle.Empty;
    private Rectangle _scrollLeftRect = Rectangle.Empty;
    private Rectangle _scrollRightRect = Rectangle.Empty;
    private Rectangle _overflowButtonRect = Rectangle.Empty;

    private int _scrollOffset;
    private int _maxScrollOffset;
    private bool _hasOverflow;
    private string? _lastTrackedActiveTabId;
    private int _lastTrackedTabCount;

    private string? _hoveredTabId;
    private string? _hoveredCloseTabId;
    private bool _hoveredNewTab;
    private bool _hoveredScrollLeft;
    private bool _hoveredScrollRight;
    private bool _hoveredOverflow;
    private string? _hoveredQuickRoute;
    private readonly ContextMenuStrip _tabContextMenu;
    private readonly ContextMenuStrip _overflowMenu;
    private string? _contextMenuTabId;

    private static readonly Color StripBg = Color.FromArgb(15, 23, 42);         // #0f172a
    private static readonly Color TabActiveBg = Color.FromArgb(30, 41, 59);     // #1e293b
    private static readonly Color TabHoverBg = Color.FromArgb(24, 34, 52);
    private static readonly Color TabBorder = Color.FromArgb(51, 65, 85);       // #334155
    private static readonly Color TextActive = Color.FromArgb(248, 250, 252);   // #f8fafc
    private static readonly Color TextMuted = Color.FromArgb(148, 163, 184);    // #94a3b8
    private static readonly Color ActiveAccent = Color.FromArgb(56, 189, 248);  // #38bdf8
    private static readonly Color CloseHoverBg = Color.FromArgb(220, 38, 38);   // #dc2626

    public event Action<string>? TabSelected;
    public event Action<string>? TabCloseRequested;
    public event Action? NewTabRequested;
    public event Action<string, string, bool>? QuickOpenRequested;
    public event Action<string>? TabReloadRequested;
    public event Action<string>? TabDuplicateRequested;
    public event Action<string>? TabPinToggled;
    public event Action<string>? CloseOthersRequested;
    public event Action<string>? CloseToRightRequested;
    public event Action? ReopenClosedRequested;
    public event Action<string>? DetachTabRequested;
    public event Action? GuideBotToggleRequested;

    public NativeTabStripControl(NativeTabWorkspace workspace)
    {
        _workspace = workspace;
        Height = 36;
        Dock = DockStyle.Top;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Font = new Font("Segoe UI", 8.75F, FontStyle.Regular);
        Cursor = Cursors.Hand;

        _tabContextMenu = BuildContextMenu();
        _overflowMenu = new ContextMenuStrip
        {
            BackColor = Color.FromArgb(30, 41, 59),
            ForeColor = Color.FromArgb(248, 250, 252),
            Renderer = new DarkMenuStripRenderer(),
            ShowCheckMargin = true,
            ShowImageMargin = false
        };
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Color.FromArgb(30, 41, 59),
            ForeColor = Color.FromArgb(248, 250, 252),
            Renderer = new DarkMenuStripRenderer()
        };
        menu.Items.Add("Reload Tab", null, (_, _) => { if (_contextMenuTabId != null) TabReloadRequested?.Invoke(_contextMenuTabId); });
        menu.Items.Add("Duplicate Tab (Ctrl+Shift+D)", null, (_, _) => { if (_contextMenuTabId != null) TabDuplicateRequested?.Invoke(_contextMenuTabId); });
        menu.Items.Add("Pin / Unpin Tab", null, (_, _) => { if (_contextMenuTabId != null) TabPinToggled?.Invoke(_contextMenuTabId); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Close Tab (Ctrl+F4)", null, (_, _) => { if (_contextMenuTabId != null) TabCloseRequested?.Invoke(_contextMenuTabId); });
        menu.Items.Add("Close Other Tabs (Ctrl+Shift+X)", null, (_, _) => { if (_contextMenuTabId != null) CloseOthersRequested?.Invoke(_contextMenuTabId); });
        menu.Items.Add("Close Tabs to Right", null, (_, _) => { if (_contextMenuTabId != null) CloseToRightRequested?.Invoke(_contextMenuTabId); });
        menu.Items.Add("Reopen Closed Tab (Ctrl+Shift+T)", null, (_, _) => ReopenClosedRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Detach Tab to Separate Window", null, (_, _) => { if (_contextMenuTabId != null) DetachTabRequested?.Invoke(_contextMenuTabId); });
        return menu;
    }

    public void RefreshTabs()
    {
        Invalidate();
    }

    public bool IsPointOnGuideBotPill(Point screenPoint)
    {
        if (IsDisposed || !IsHandleCreated) return false;
        var pt = PointToClient(screenPoint);
        foreach (var q in _quickRects)
        {
            if (q.Route == "__guide_bot__" && q.Bounds.Contains(pt))
            {
                return true;
            }
        }
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _tabContextMenu.Dispose(); } catch { }
            try { _overflowMenu.Dispose(); } catch { }
            try { Font?.Dispose(); } catch { }
        }
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(StripBg);

        using var borderPen = new Pen(TabBorder, 1f);
        g.DrawLine(borderPen, 0, Height - 1, Width, Height - 1);

        _tabRects.Clear();
        _quickRects.Clear();

        // 1. Reserve right side for Waseela Quick-Launch Pills (+POS, +Sale, +Purchase, +Products, Cube AI)
        var quickButtons = new (string Label, string Route, Color Accent)[]
        {
            ("+ POS", "/sale-invoices/pos", Color.FromArgb(16, 185, 129)),
            ("+ Sale", "/sale-invoices/create/retail", Color.FromArgb(14, 165, 233)),
            ("+ Purchase", "/purchase-invoices/create", Color.FromArgb(99, 102, 241)),
            ("+ Products", "/products", Color.FromArgb(245, 158, 11)),
            ("Cube AI", "__guide_bot__", Color.FromArgb(37, 99, 235))
        };

        var rightX = Width - 8;
        if (Width >= 780)
        {
            using var quickFont = new Font("Segoe UI Semibold", 8.2F, FontStyle.Bold);
            for (var i = quickButtons.Length - 1; i >= 0; i--)
            {
                var qb = quickButtons[i];
                var textSize = TextRenderer.MeasureText(qb.Label, quickFont);
                var pillW = textSize.Width + 14;
                var pillH = 24;
                var pillX = rightX - pillW;
                var pillY = (Height - pillH) / 2;
                var rect = new Rectangle(pillX, pillY, pillW, pillH);
                _quickRects.Add(new QuickButtonRect(rect, qb.Route, qb.Label.TrimStart('+', ' ')));

                var isHovered = _hoveredQuickRoute == qb.Route;
                using var bgBrush = new SolidBrush(isHovered ? qb.Accent : Color.FromArgb(30, 41, 59));
                using var pillPen = new Pen(isHovered ? qb.Accent : Color.FromArgb(71, 85, 105), 1f);
                FillRoundedRect(g, bgBrush, rect, 5);
                DrawRoundedRect(g, pillPen, rect, 5);

                TextRenderer.DrawText(
                    g,
                    qb.Label,
                    quickFont,
                    rect,
                    isHovered ? Color.White : TextActive,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                rightX = pillX - 6;
            }
        }

        // 2. Detect tab overflow & compute viewport + non-overlapping control rects
        var tabs = _workspace.Tabs;
        const int tabViewportLeft = 6;
        const int noOverflowControlsW = 60; // '+' (24) + gap (4) + '▾' (24) + padding (8)
        var noOverflowViewportW = Math.Max(140, rightX - tabViewportLeft - noOverflowControlsW);
        var tabCount = Math.Max(1, tabs.Count);
        var probeTabW = Math.Clamp(noOverflowViewportW / tabCount, 96, 195);

        var totalNaturalW = 0;
        foreach (var tab in tabs)
        {
            totalNaturalW += tab.IsPinned ? Math.Min(probeTabW, 132) : probeTabW;
        }

        _hasOverflow = totalNaturalW > noOverflowViewportW;

        int tabViewportRight;
        int totalLogicalW = 0;
        var activeStart = 0;
        var activeEnd = 0;

        if (_hasOverflow)
        {
            _overflowButtonRect = new Rectangle(rightX - 26, 7, 24, 22);
            _scrollRightRect = new Rectangle(_overflowButtonRect.Left - 23, 7, 21, 22);
            _scrollLeftRect = new Rectangle(_scrollRightRect.Left - 23, 7, 21, 22);
            _newTabButtonRect = new Rectangle(_scrollLeftRect.Left - 28, 7, 24, 22);

            tabViewportRight = Math.Max(tabViewportLeft + 40, _newTabButtonRect.Left - 6);
            var viewportW = tabViewportRight - tabViewportLeft;

            const int overflowTabW = 112;
            foreach (var tab in tabs)
            {
                var tabW = tab.IsPinned ? Math.Min(overflowTabW, 100) : overflowTabW;
                if (tab.TabId == _workspace.ActiveTabId)
                {
                    activeStart = totalLogicalW;
                    activeEnd = totalLogicalW + tabW;
                }
                totalLogicalW += tabW;
            }

            _maxScrollOffset = Math.Max(0, totalLogicalW - viewportW);
            if (_workspace.ActiveTabId != _lastTrackedActiveTabId || tabs.Count != _lastTrackedTabCount)
            {
                if (activeEnd > activeStart)
                {
                    if (activeStart < _scrollOffset)
                    {
                        _scrollOffset = activeStart;
                    }
                    else if (activeEnd > _scrollOffset + viewportW)
                    {
                        _scrollOffset = activeEnd - viewportW;
                    }
                }
                _lastTrackedActiveTabId = _workspace.ActiveTabId;
                _lastTrackedTabCount = tabs.Count;
            }

            _scrollOffset = Math.Clamp(_scrollOffset, 0, _maxScrollOffset);
        }
        else
        {
            _scrollOffset = 0;
            _maxScrollOffset = 0;
            _scrollLeftRect = Rectangle.Empty;
            _scrollRightRect = Rectangle.Empty;
            tabViewportRight = Math.Max(tabViewportLeft + 40, rightX - noOverflowControlsW);
            totalLogicalW = totalNaturalW;
            _lastTrackedActiveTabId = _workspace.ActiveTabId;
            _lastTrackedTabCount = tabs.Count;
        }

        // 3. Draw clipped tabs inside viewportRect
        var viewportRect = new Rectangle(tabViewportLeft, 0, Math.Max(1, tabViewportRight - tabViewportLeft), Height);
        var gState = g.Save();
        g.SetClip(viewportRect);

        using var activeFont = new Font("Segoe UI Semibold", 8.75F, FontStyle.Bold);
        using var closeFont = new Font("Segoe UI", 8.5F, FontStyle.Bold);

        var logicalX = 0;
        foreach (var tab in tabs)
        {
            var isPinned = tab.IsPinned;
            var baseTabW = _hasOverflow ? 112 : probeTabW;
            var tabW = isPinned ? Math.Min(baseTabW, _hasOverflow ? 100 : 132) : baseTabW;
            var screenX = tabViewportLeft + logicalX - _scrollOffset;
            logicalX += tabW;

            if (screenX + tabW <= tabViewportLeft || screenX >= tabViewportRight)
            {
                continue;
            }

            var rect = new Rectangle(screenX, 4, tabW - 3, Height - 4);
            var canClose = !isPinned && tabs.Count > 1;
            var closeRect = canClose
                ? new Rectangle(rect.Right - 22, rect.Top + (rect.Height - 16) / 2, 16, 16)
                : Rectangle.Empty;

            var hitBounds = Rectangle.Intersect(rect, viewportRect);
            var hitCloseRect = !closeRect.IsEmpty && viewportRect.Contains(closeRect)
                ? closeRect
                : Rectangle.Empty;

            if (!hitBounds.IsEmpty)
            {
                _tabRects.Add(new TabHitRect(tab.TabId, hitBounds, hitCloseRect));
            }

            var isActive = tab.TabId == _workspace.ActiveTabId;
            var isHovered = tab.TabId == _hoveredTabId;

            if (isActive || isHovered)
            {
                using var tabBrush = new SolidBrush(isActive ? TabActiveBg : TabHoverBg);
                FillTopRoundedRect(g, tabBrush, rect, 6);
            }

            if (isActive)
            {
                using var accentBrush = new SolidBrush(ActiveAccent);
                g.FillRectangle(accentBrush, rect.Left + 4, rect.Bottom - 2, rect.Width - 8, 2);
            }
            else
            {
                // Subtle vertical divider between inactive tabs
                using var divPen = new Pen(Color.FromArgb(40, 52, 70), 1f);
                g.DrawLine(divPen, rect.Right + 1, rect.Top + 7, rect.Right + 1, rect.Bottom - 7);
            }

            // Category color dot
            var dotColor = ParseHexColor(tab.CategoryColorHex, ActiveAccent);
            using (var dotBrush = new SolidBrush(dotColor))
            {
                g.FillEllipse(dotBrush, rect.Left + 9, rect.Top + (rect.Height - 8) / 2, 8, 8);
            }

            // Tab title
            var titleLeft = rect.Left + 22;
            var titleRight = canClose ? closeRect.Left - 3 : rect.Right - 6;
            var titleRect = new Rectangle(titleLeft, rect.Top, Math.Max(20, titleRight - titleLeft), rect.Height);
            var displayTitle = isPinned ? $"• {tab.Title}" : tab.Title;
            TextRenderer.DrawText(
                g,
                displayTitle,
                isActive ? activeFont : Font,
                titleRect,
                isActive ? TextActive : TextMuted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            // Close '×' button
            if (canClose)
            {
                var closeHover = _hoveredCloseTabId == tab.TabId;
                if (closeHover)
                {
                    using var closeBg = new SolidBrush(CloseHoverBg);
                    g.FillEllipse(closeBg, closeRect);
                }

                TextRenderer.DrawText(
                    g,
                    "×",
                    closeFont,
                    closeRect,
                    closeHover ? Color.White : (isActive ? TextActive : TextMuted),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        g.Restore(gState);

        if (!_hasOverflow)
        {
            var afterTabsX = Math.Min(tabViewportRight, tabViewportLeft + totalLogicalW);
            _newTabButtonRect = new Rectangle(afterTabsX + 2, 7, 24, 22);
            _overflowButtonRect = new Rectangle(_newTabButtonRect.Right + 4, 7, 24, 22);
        }

        // 4. '+' New Tab button
        using (var plusBg = new SolidBrush(_hoveredNewTab ? Color.FromArgb(51, 65, 85) : Color.Transparent))
        {
            FillRoundedRect(g, plusBg, _newTabButtonRect, 5);
        }
        using var plusFont = new Font("Segoe UI", 11F, FontStyle.Bold);
        TextRenderer.DrawText(
            g,
            "+",
            plusFont,
            _newTabButtonRect,
            _hoveredNewTab ? Color.White : TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        // 5. '<' and '>' scroll buttons when overflowed
        if (_hasOverflow)
        {
            using var scrollFont = new Font("Segoe UI", 9F, FontStyle.Bold);
            var canLeft = _scrollOffset > 0;
            var canRight = _scrollOffset < _maxScrollOffset;

            using (var leftBg = new SolidBrush(_hoveredScrollLeft && canLeft ? Color.FromArgb(51, 65, 85) : Color.FromArgb(24, 34, 52)))
            using (var leftPen = new Pen(TabBorder, 1f))
            {
                FillRoundedRect(g, leftBg, _scrollLeftRect, 4);
                DrawRoundedRect(g, leftPen, _scrollLeftRect, 4);
            }
            TextRenderer.DrawText(
                g,
                "‹",
                scrollFont,
                _scrollLeftRect,
                canLeft ? (_hoveredScrollLeft ? Color.White : TextActive) : Color.FromArgb(71, 85, 105),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            using (var rightBg = new SolidBrush(_hoveredScrollRight && canRight ? Color.FromArgb(51, 65, 85) : Color.FromArgb(24, 34, 52)))
            using (var rightPen = new Pen(TabBorder, 1f))
            {
                FillRoundedRect(g, rightBg, _scrollRightRect, 4);
                DrawRoundedRect(g, rightPen, _scrollRightRect, 4);
            }
            TextRenderer.DrawText(
                g,
                "›",
                scrollFont,
                _scrollRightRect,
                canRight ? (_hoveredScrollRight ? Color.White : TextActive) : Color.FromArgb(71, 85, 105),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        // 6. '▾' All Open Tabs overflow dropdown button
        using (var ovBg = new SolidBrush(_hoveredOverflow ? Color.FromArgb(51, 65, 85) : (_hasOverflow ? TabActiveBg : Color.Transparent)))
        {
            FillRoundedRect(g, ovBg, _overflowButtonRect, 5);
        }
        if (_hasOverflow)
        {
            using var ovPen = new Pen(TabBorder, 1f);
            DrawRoundedRect(g, ovPen, _overflowButtonRect, 5);
        }
        using var ovFont = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        TextRenderer.DrawText(
            g,
            "▾",
            ovFont,
            _overflowButtonRect,
            _hoveredOverflow ? Color.White : (_hasOverflow ? ActiveAccent : TextMuted),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_hasOverflow && e.Delta != 0)
        {
            _scrollOffset = Math.Clamp(_scrollOffset - Math.Sign(e.Delta) * 112, 0, _maxScrollOffset);
            Invalidate();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        string? newHoverTab = null;
        string? newHoverClose = null;
        string? newHoverQuick = null;
        var newHoverPlus = _newTabButtonRect.Contains(e.Location);
        var newHoverLeft = _hasOverflow && _scrollOffset > 0 && _scrollLeftRect.Contains(e.Location);
        var newHoverRight = _hasOverflow && _scrollOffset < _maxScrollOffset && _scrollRightRect.Contains(e.Location);
        var newHoverOverflow = _overflowButtonRect.Contains(e.Location);

        foreach (var t in _tabRects)
        {
            if (t.Bounds.Contains(e.Location))
            {
                newHoverTab = t.TabId;
                if (!t.CloseBounds.IsEmpty && t.CloseBounds.Contains(e.Location))
                {
                    newHoverClose = t.TabId;
                }
                break;
            }
        }

        foreach (var q in _quickRects)
        {
            if (q.Bounds.Contains(e.Location))
            {
                newHoverQuick = q.Route;
                break;
            }
        }

        if (newHoverTab != _hoveredTabId ||
            newHoverClose != _hoveredCloseTabId ||
            newHoverPlus != _hoveredNewTab ||
            newHoverLeft != _hoveredScrollLeft ||
            newHoverRight != _hoveredScrollRight ||
            newHoverOverflow != _hoveredOverflow ||
            newHoverQuick != _hoveredQuickRoute)
        {
            _hoveredTabId = newHoverTab;
            _hoveredCloseTabId = newHoverClose;
            _hoveredNewTab = newHoverPlus;
            _hoveredScrollLeft = newHoverLeft;
            _hoveredScrollRight = newHoverRight;
            _hoveredOverflow = newHoverOverflow;
            _hoveredQuickRoute = newHoverQuick;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoveredTabId = null;
        _hoveredCloseTabId = null;
        _hoveredNewTab = false;
        _hoveredScrollLeft = false;
        _hoveredScrollRight = false;
        _hoveredOverflow = false;
        _hoveredQuickRoute = null;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Left && _hasOverflow && _scrollLeftRect.Contains(e.Location))
        {
            _scrollOffset = Math.Clamp(_scrollOffset - 168, 0, _maxScrollOffset);
            Invalidate();
            return;
        }

        if (e.Button == MouseButtons.Left && _hasOverflow && _scrollRightRect.Contains(e.Location))
        {
            _scrollOffset = Math.Clamp(_scrollOffset + 168, 0, _maxScrollOffset);
            Invalidate();
            return;
        }

        if (e.Button == MouseButtons.Left && _overflowButtonRect.Contains(e.Location))
        {
            ShowOverflowTabsMenu();
            return;
        }

        if (e.Button == MouseButtons.Left && _newTabButtonRect.Contains(e.Location))
        {
            NewTabRequested?.Invoke();
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            foreach (var q in _quickRects)
            {
                if (q.Bounds.Contains(e.Location))
                {
                    if (q.Route == "__guide_bot__")
                    {
                        GuideBotToggleRequested?.Invoke();
                    }
                    else
                    {
                        QuickOpenRequested?.Invoke(q.Route, q.Title, false);
                    }
                    return;
                }
            }
        }

        foreach (var t in _tabRects)
        {
            if (!t.Bounds.Contains(e.Location)) continue;

            if (e.Button == MouseButtons.Middle)
            {
                TabCloseRequested?.Invoke(t.TabId);
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                _contextMenuTabId = t.TabId;
                _tabContextMenu.Show(this, e.Location);
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                if (!t.CloseBounds.IsEmpty && t.CloseBounds.Contains(e.Location))
                {
                    TabCloseRequested?.Invoke(t.TabId);
                }
                else
                {
                    TabSelected?.Invoke(t.TabId);
                }
                return;
            }
        }
    }

    private void ShowOverflowTabsMenu()
    {
        foreach (var oldItem in _overflowMenu.Items.Cast<ToolStripItem>().ToArray())
        {
            oldItem.Dispose();
        }
        _overflowMenu.Items.Clear();

        var header = new ToolStripMenuItem($"All Open Tabs ({_workspace.Tabs.Count})")
        {
            Enabled = false,
            ForeColor = TextMuted
        };
        _overflowMenu.Items.Add(header);
        _overflowMenu.Items.Add(new ToolStripSeparator());

        var tabs = _workspace.Tabs;
        for (var i = 0; i < tabs.Count; i++)
        {
            var tab = tabs[i];
            var targetId = tab.TabId;
            var label = $"{i + 1}. {(tab.IsPinned ? "• " : "")}{tab.Title}";
            var item = new ToolStripMenuItem(label)
            {
                Checked = tab.TabId == _workspace.ActiveTabId,
                ForeColor = tab.TabId == _workspace.ActiveTabId ? ActiveAccent : TextActive,
                BackColor = TabActiveBg
            };
            item.Click += (_, _) => TabSelected?.Invoke(targetId);
            _overflowMenu.Items.Add(item);
        }

        _overflowMenu.Show(this, new Point(_overflowButtonRect.Left, _overflowButtonRect.Bottom + 2));
    }

    private static Color ParseHexColor(string hex, Color fallback)
    {
        try
        {
            return ColorTranslator.FromHtml(hex);
        }
        catch
        {
            return fallback;
        }
    }

    private static void FillRoundedRect(Graphics g, Brush brush, Rectangle r, int radius)
    {
        using var path = CreateRoundedPath(r, radius);
        g.FillPath(brush, path);
    }

    private static void DrawRoundedRect(Graphics g, Pen pen, Rectangle r, int radius)
    {
        using var path = CreateRoundedPath(r, radius);
        g.DrawPath(pen, path);
    }

    private static void FillTopRoundedRect(Graphics g, Brush brush, Rectangle r, int radius)
    {
        using var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddLine(r.Right, r.Bottom, r.Left, r.Bottom);
        path.CloseFigure();
        g.FillPath(brush, path);
    }

    private static GraphicsPath CreateRoundedPath(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed record TabHitRect(string TabId, Rectangle Bounds, Rectangle CloseBounds);
    private sealed record QuickButtonRect(Rectangle Bounds, string Route, string Title);
}
#endif

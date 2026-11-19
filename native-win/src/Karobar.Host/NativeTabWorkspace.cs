using System.Text.RegularExpressions;

namespace Karobar.Host;

public sealed class WorkspaceTabRecord
{
    public string TabId { get; init; } = Guid.NewGuid().ToString("N")[..10];
    public string Title { get; set; } = "Dashboard";
    public string RoutePath { get; set; } = "/dashboard";
    public string CategoryColorHex { get; set; } = "#3b82f6";
    public bool IsPinned { get; set; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class ClosedTabRecord
{
    public string Title { get; init; } = "Dashboard";
    public string RoutePath { get; init; } = "/dashboard";
    public DateTime ClosedAtUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Platform-independent Waseela-style Multi-Tab MDI workspace state model.
/// Manages singleton vs. parallel transactional tabs (e.g. multiple simultaneous Retail/Wholesale/Purchase bills),
/// pinned tabs, tab cycling, and closed-tab recovery stack (Ctrl+Shift+T).
/// </summary>
public sealed class NativeTabWorkspace
{
    private const int MaxClosedHistory = 15;
    private readonly List<WorkspaceTabRecord> _tabs = new();
    private readonly Stack<ClosedTabRecord> _closedTabs = new();

    private static readonly string[] ParallelRoutePrefixes =
    [
        "/sale-invoices/create",
        "/sale-invoices/pos",
        "/sale-invoices/barcode-pos",
        "/wholesale-invoices/create",
        "/purchase-invoices/create",
        "/purchase-orders/create",
        "/sale-returns/create",
        "/purchase-returns/create",
        "/stock-adjustments/create",
        "/stocktake-sessions/create",
        "/stock-disposals/create"
    ];

    public IReadOnlyList<WorkspaceTabRecord> Tabs => _tabs;
    public IReadOnlyCollection<ClosedTabRecord> ClosedTabs => _closedTabs;
    public string? ActiveTabId { get; private set; }
    public WorkspaceTabRecord? ActiveTab => _tabs.FirstOrDefault(t => t.TabId == ActiveTabId);

    public (WorkspaceTabRecord Tab, bool CreatedNew) OpenTab(
        string routePath,
        string? title = null,
        bool allowDuplicate = false,
        bool focus = true,
        bool pinned = false)
    {
        var normalized = NormalizeRoutePath(routePath);
        var isParallelRoute = IsParallelTransactionalRoute(normalized);

        if (!allowDuplicate && !isParallelRoute)
        {
            var existing = _tabs.FirstOrDefault(t =>
                string.Equals(StripQuery(t.RoutePath), StripQuery(normalized), StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.RoutePath = normalized;
                if (focus) ActiveTabId = existing.TabId;
                return (existing, false);
            }
        }

        var baseTitle = string.IsNullOrWhiteSpace(title) ? ResolveTitleForRoute(normalized) : title.Trim();
        var finalTitle = baseTitle;
        if (isParallelRoute || allowDuplicate)
        {
            var samePrefixCount = _tabs.Count(t =>
                string.Equals(StripQuery(t.RoutePath), StripQuery(normalized), StringComparison.OrdinalIgnoreCase));
            if (samePrefixCount > 0)
            {
                finalTitle = $"{baseTitle} #{samePrefixCount + 1}";
            }
        }

        var tab = new WorkspaceTabRecord
        {
            Title = finalTitle,
            RoutePath = normalized,
            CategoryColorHex = ResolveCategoryColorHex(normalized),
            IsPinned = pinned
        };

        if (pinned)
        {
            var insertIdx = _tabs.FindLastIndex(t => t.IsPinned) + 1;
            _tabs.Insert(insertIdx, tab);
        }
        else
        {
            _tabs.Add(tab);
        }

        if (focus || string.IsNullOrEmpty(ActiveTabId))
        {
            ActiveTabId = tab.TabId;
        }

        return (tab, true);
    }

    public bool FocusTab(string tabId)
    {
        if (!_tabs.Any(t => t.TabId == tabId)) return false;
        ActiveTabId = tabId;
        return true;
    }

    public WorkspaceTabRecord? FocusByIndex(int zeroBasedIndex)
    {
        if (_tabs.Count == 0) return null;
        var clamped = Math.Clamp(zeroBasedIndex, 0, _tabs.Count - 1);
        ActiveTabId = _tabs[clamped].TabId;
        return _tabs[clamped];
    }

    public WorkspaceTabRecord? FocusLastTab()
    {
        if (_tabs.Count == 0) return null;
        ActiveTabId = _tabs[^1].TabId;
        return _tabs[^1];
    }

    public WorkspaceTabRecord? CycleTab(int delta)
    {
        if (_tabs.Count <= 1) return ActiveTab;
        var idx = _tabs.FindIndex(t => t.TabId == ActiveTabId);
        if (idx < 0) idx = 0;
        var nextIdx = (idx + delta) % _tabs.Count;
        if (nextIdx < 0) nextIdx += _tabs.Count;
        ActiveTabId = _tabs[nextIdx].TabId;
        return _tabs[nextIdx];
    }

    public bool CloseTab(string tabId, bool forcePinned = false)
    {
        var idx = _tabs.FindIndex(t => t.TabId == tabId);
        if (idx < 0) return false;

        var target = _tabs[idx];
        if (target.IsPinned && !forcePinned) return false;
        if (_tabs.Count <= 1) return false;

        _tabs.RemoveAt(idx);
        PushClosedTab(target);

        if (ActiveTabId == tabId)
        {
            var nextIdx = Math.Min(idx, _tabs.Count - 1);
            ActiveTabId = _tabs[nextIdx].TabId;
        }

        return true;
    }

    public List<string> CloseOtherTabs(string keepTabId)
    {
        var removedIds = new List<string>();
        for (var i = _tabs.Count - 1; i >= 0; i--)
        {
            var t = _tabs[i];
            if (t.TabId == keepTabId || t.IsPinned) continue;
            _tabs.RemoveAt(i);
            PushClosedTab(t);
            removedIds.Add(t.TabId);
        }

        if (_tabs.Any(t => t.TabId == keepTabId))
        {
            ActiveTabId = keepTabId;
        }
        else if (_tabs.Count > 0)
        {
            ActiveTabId = _tabs[0].TabId;
        }

        return removedIds;
    }

    public List<string> CloseTabsToRight(string tabId)
    {
        var removedIds = new List<string>();
        var idx = _tabs.FindIndex(t => t.TabId == tabId);
        if (idx < 0) return removedIds;

        for (var i = _tabs.Count - 1; i > idx; i--)
        {
            var t = _tabs[i];
            if (t.IsPinned) continue;
            _tabs.RemoveAt(i);
            PushClosedTab(t);
            removedIds.Add(t.TabId);
        }

        if (!_tabs.Any(t => t.TabId == ActiveTabId) && _tabs.Count > 0)
        {
            ActiveTabId = _tabs[Math.Min(idx, _tabs.Count - 1)].TabId;
        }

        return removedIds;
    }

    public WorkspaceTabRecord? ReopenLastClosedTab()
    {
        if (_closedTabs.Count == 0) return null;
        var last = _closedTabs.Pop();
        var (tab, _) = OpenTab(last.RoutePath, last.Title, allowDuplicate: true, focus: true);
        return tab;
    }

    public bool TogglePin(string tabId)
    {
        var idx = _tabs.FindIndex(t => t.TabId == tabId);
        if (idx < 0) return false;

        var tab = _tabs[idx];
        tab.IsPinned = !tab.IsPinned;
        _tabs.RemoveAt(idx);

        if (tab.IsPinned)
        {
            var insertIdx = _tabs.FindLastIndex(t => t.IsPinned) + 1;
            _tabs.Insert(insertIdx, tab);
        }
        else
        {
            var firstUnpinned = _tabs.FindIndex(t => !t.IsPinned);
            if (firstUnpinned < 0) _tabs.Add(tab);
            else _tabs.Insert(firstUnpinned, tab);
        }

        return true;
    }

    public void UpdateTabMetadata(string tabId, string? title, string? routePath)
    {
        var tab = _tabs.FirstOrDefault(t => t.TabId == tabId);
        if (tab == null) return;

        if (!string.IsNullOrWhiteSpace(routePath))
        {
            var norm = NormalizeRoutePath(routePath);
            tab.RoutePath = norm;
            tab.CategoryColorHex = ResolveCategoryColorHex(norm);
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            var cleanTitle = title.Trim();
            var dashIdx = cleanTitle.LastIndexOf(" - ", StringComparison.Ordinal);
            if (dashIdx > 0 && cleanTitle[(dashIdx + 3)..].Contains("Karobar", StringComparison.OrdinalIgnoreCase))
            {
                cleanTitle = cleanTitle[..dashIdx].Trim();
            }
            if (!string.IsNullOrWhiteSpace(cleanTitle))
            {
                tab.Title = cleanTitle;
            }
        }
    }

    private void PushClosedTab(WorkspaceTabRecord tab)
    {
        _closedTabs.Push(new ClosedTabRecord
        {
            Title = tab.Title,
            RoutePath = tab.RoutePath
        });

        if (_closedTabs.Count > MaxClosedHistory)
        {
            var keep = _closedTabs.Take(MaxClosedHistory).Reverse().ToList();
            _closedTabs.Clear();
            foreach (var item in keep) _closedTabs.Push(item);
        }
    }

    public static bool IsParallelTransactionalRoute(string routePath)
    {
        var clean = StripQuery(NormalizeRoutePath(routePath));
        return ParallelRoutePrefixes.Any(prefix => clean.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public static string NormalizeRoutePath(string? routePath)
    {
        if (string.IsNullOrWhiteSpace(routePath)) return "/dashboard";
        var trimmed = routePath.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            trimmed = uri.PathAndQuery;
        }
        if (!trimmed.StartsWith('/')) trimmed = "/" + trimmed;
        return trimmed == "/" ? "/dashboard" : trimmed;
    }

    public static string StripQuery(string routePath)
    {
        var q = routePath.IndexOf('?');
        var p = q >= 0 ? routePath[..q] : routePath;
        p = p.TrimEnd('/');
        return string.IsNullOrEmpty(p) ? "/dashboard" : p;
    }

    public static string ResolveTitleForRoute(string routePath)
    {
        var clean = StripQuery(NormalizeRoutePath(routePath)).ToLowerInvariant();
        return clean switch
        {
            "/dashboard" => "Dashboard",
            "/products" => "Products",
            "/products/create" => "Add Product",
            "/product-batches" => "Batches",
            "/categories" => "Categories",
            "/brands" => "Brands",
            "/suppliers" => "Suppliers",
            "/customers" => "Customers",
            "/barcode-labels" => "Barcode Studio",
            "/sale-invoices/pos" or "/sale-invoices/barcode-pos" => "Barcode POS",
            "/sale-invoices" => "Sale Invoices",
            "/sale-invoices/create/retail" or "/sale-invoices/create-retail" => "Retail Sale",
            "/wholesale-invoices" => "Wholesale Invoices",
            "/sale-invoices/create/wholesale" or "/sale-invoices/create-wholesale" or "/wholesale-invoices/create" => "Wholesale Sale",
            "/purchase-invoices" => "Purchase Invoices",
            "/purchase-invoices/create" => "Purchase Invoice",
            "/purchase-orders" => "Purchase Orders",
            "/purchase-orders/create" => "Purchase Order",
            "/smart-reorder" => "Smart Reorder",
            "/sale-returns" => "Sale Returns",
            "/sale-returns/create" => "Sale Return",
            "/purchase-returns" => "Purchase Returns",
            "/purchase-returns/create" => "Purchase Return",
            "/batch-expiry" => "Batch & Expiry",
            "/stock-adjustments" => "Stock Adjustments",
            "/stocktake-sessions" => "Stocktake",
            "/stock-disposals" => "Disposals",
            "/customer-ledger" => "Customer Ledger",
            "/supplier-ledger" => "Supplier Ledger",
            "/payments" => "Payments",
            "/cash-drawer" => "Cash Drawer",
            "/accounts" => "Accounts",
            "/expenses" => "Expenses",
            "/accounting" => "Accounting GL",
            "/hr-payroll" => "HR & Payroll",
            "/settings" => "Settings",
            "/settings/sqlite-import" or "/sqlite-import" => "SQLite Import",
            "/users" => "Users",
            "/roles" => "Roles",
            "/backups" => "Backups",
            _ when clean.StartsWith("/reports/") => "Report: " + FormatSlug(clean["/reports/".Length..]),
            _ when Regex.IsMatch(clean, @"^/sale-invoices/\d+") => "Sale Invoice",
            _ when Regex.IsMatch(clean, @"^/purchase-invoices/\d+") => "Purchase Invoice",
            _ when Regex.IsMatch(clean, @"^/products/\d+") => "Edit Product",
            _ => "Workspace"
        };
    }

    public static string ResolveCategoryColorHex(string routePath)
    {
        var clean = StripQuery(NormalizeRoutePath(routePath)).ToLowerInvariant();
        if (clean.StartsWith("/sale-") || clean.StartsWith("/wholesale-")) return "#10b981"; // Emerald
        if (clean.StartsWith("/purchase-") || clean.StartsWith("/smart-reorder")) return "#6366f1"; // Indigo
        if (clean.StartsWith("/product") || clean.StartsWith("/batch") || clean.StartsWith("/stock") ||
            clean.StartsWith("/brand") || clean.StartsWith("/categor") || clean.StartsWith("/barcode")) return "#f59e0b"; // Amber
        if (clean.StartsWith("/customer") || clean.StartsWith("/supplier") || clean.StartsWith("/payment") ||
            clean.StartsWith("/cash-drawer") || clean.StartsWith("/account") || clean.StartsWith("/expense")) return "#06b6d4"; // Cyan
        if (clean.StartsWith("/reports")) return "#a855f7"; // Purple
        return "#3b82f6"; // Blue (Dashboard / System)
    }

    private static string FormatSlug(string slug)
    {
        var parts = slug.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }
}

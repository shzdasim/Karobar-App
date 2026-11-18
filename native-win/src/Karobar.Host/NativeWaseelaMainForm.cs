#if WINDOWS_DESKTOP
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Karobar.Data;
using Karobar.Printing;
using Karobar.Services;
using Karobar.Services.Infrastructure;

namespace Karobar.Host;

/// <summary>
/// Pure Native Windows Desktop ERP Shell (Option 2 — Waseela-Style Zero-Browser WinForms/DataGridView UI).
/// Directly queries SQLite via KarobarDb and renders native Windows controls with sub-millisecond
/// keyboard navigation (F1–F12 hotkeys), instant DataGridViews, and direct Win32 thermal printing.
/// Optional F12 toggle opens a native WebView2 window for any auxiliary screen.
/// </summary>
public sealed class NativeWaseelaMainForm : Form
{
    private readonly NetworkConfigManager _networkConfig;
    private readonly WebView2WindowManager _windowManager;
    private readonly KestrelHostServer _hostServer;
    private readonly KarobarDb _db;
    private readonly InvoiceCalculationService _calcService;
    private readonly PrintTemplateRenderer _printRenderer = new();
    private readonly Win32RawPrinterService _rawPrinter = new();

    private readonly Panel _contentHost;
    private readonly Label _headerTitleLabel;
    private readonly Label _statusClockLabel;
    private readonly Label _statusDbLabel;
    private readonly Dictionary<string, Button> _navButtons = new(StringComparer.OrdinalIgnoreCase);

    // POS Counter State
    private DataGridView? _posGrid;
    private TextBox? _posBarcodeBox;
    private ComboBox? _posCustomerCombo;
    private ComboBox? _posSaleTypeCombo;
    private ComboBox? _posPaymentTypeCombo;
    private TextBox? _posDiscountBox;
    private TextBox? _posCashReceivedBox;
    private Label? _posGrossLabel;
    private Label? _posDiscountTotalLabel;
    private Label? _posNetTotalLabel;
    private Label? _posChangeLabel;
    private DataGridView? _posProductLookupGrid;

    public NativeWaseelaMainForm(
        NetworkConfigManager networkConfig,
        WebView2WindowManager windowManager,
        KestrelHostServer hostServer)
    {
        _networkConfig = networkConfig;
        _windowManager = windowManager;
        _hostServer = hostServer;

        var bootstrapper = new DatabaseBootstrapper(new DbConnectionFactory());
        bootstrapper.EnsureSchemaReady();
        _db = new KarobarDb(explicitSqlitePath: bootstrapper.SqlitePath);
        _calcService = new InvoiceCalculationService(_db);

        Text = "Karobar-App Native ERP (Pure Windows Desktop Edition — Waseela Speed)";
        Width = 1480;
        Height = 920;
        WindowState = FormWindowState.Maximized;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        BackColor = Color.FromArgb(241, 245, 249);
        Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);

        var iconPath = windowManager.ResolveIconPath();
        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath) && iconPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
        {
            try { Icon = new Icon(iconPath); } catch { }
        }

        // 1. Top Hotkey Bar (Waseela-style F1-F12 bar)
        var topBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 52,
            BackColor = Color.FromArgb(15, 23, 42),
            Padding = new Padding(16, 0, 16, 0)
        };

        _headerTitleLabel = new Label
        {
            Text = "⚡ POS COUNTER BILLING (F2)",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 13F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(16, 13)
        };

        var hotkeyHint = new Label
        {
            Text = "[F1] Dashboard   [F2] POS Counter   [F3] Products & Stock   [F4] Purchase Invoice   [F5] Customer Khata   [F6] Supplier Khata   [F7] Cash Drawer   [F8] Reports   [F9] Post & Print   [F12] Web UI View",
            ForeColor = Color.FromArgb(148, 163, 184),
            Font = new Font("Segoe UI", 8.75F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(340, 17)
        };

        topBar.Controls.Add(_headerTitleLabel);
        topBar.Controls.Add(hotkeyHint);

        // 2. Left Sidebar Navigation
        var sideBar = new Panel
        {
            Dock = DockStyle.Left,
            Width = 235,
            BackColor = Color.FromArgb(30, 41, 59),
            Padding = new Padding(10, 12, 10, 12)
        };

        var brandLabel = new Label
        {
            Text = "KAROBAR NATIVE",
            ForeColor = Color.FromArgb(56, 189, 248),
            Font = new Font("Segoe UI", 13F, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 36,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var subBrandLabel = new Label
        {
            Text = "Pure Win32 / .NET 9 Engine",
            ForeColor = Color.FromArgb(148, 163, 184),
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
            Dock = DockStyle.Top,
            Height = 24
        };

        var navStack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0, 8, 0, 0)
        };

        AddNavButton(navStack, "pos", "⚡ POS Counter (F2)", () => ShowPosCounterScreen());
        AddNavButton(navStack, "dashboard", "📊 Executive Dashboard (F1)", () => ShowDashboardScreen());
        AddNavButton(navStack, "products", "💊 Products & Batches (F3)", () => ShowProductsScreen());
        AddNavButton(navStack, "purchases", "📦 Purchase Entry (F4)", () => ShowPurchasesScreen());
        AddNavButton(navStack, "sales_list", "🧾 Sale Invoices History", () => ShowSaleInvoicesListScreen());
        AddNavButton(navStack, "customer_ledger", "📒 Customer Khata (F5)", () => ShowLedgerScreen("customer"));
        AddNavButton(navStack, "supplier_ledger", "🏢 Supplier Khata (F6)", () => ShowLedgerScreen("supplier"));
        AddNavButton(navStack, "drawer", "💵 Cash Drawer & Accounts (F7)", () => ShowCashDrawerScreen());
        AddNavButton(navStack, "reports", "📈 Analytical Reports (F8)", () => ShowReportsScreen());
        AddNavButton(navStack, "waseela", "🔄 Waseela / SQLite Importer", () => ShowWaseelaImporterScreen());
        AddNavButton(navStack, "webui", "🌐 Open Full React Window (F12)", () => OpenHybridWebViewWindow("/dashboard"));

        sideBar.Controls.Add(navStack);
        sideBar.Controls.Add(subBrandLabel);
        sideBar.Controls.Add(brandLabel);

        // 3. Bottom Status Bar
        var statusBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 30,
            BackColor = Color.FromArgb(15, 23, 42),
            Padding = new Padding(14, 0, 14, 0)
        };

        _statusDbLabel = new Label
        {
            Text = $"SQLite WAL Native Engine: {_db.SqlitePath}  |  Zero PHP  |  Zero Electron  |  Sub-ms Native DataGrid",
            ForeColor = Color.FromArgb(74, 222, 128),
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
            Dock = DockStyle.Left,
            AutoSize = true,
            Padding = new Padding(0, 6, 0, 0)
        };

        _statusClockLabel = new Label
        {
            Text = DateTime.Now.ToString("dd MMM yyyy  hh:mm:ss tt"),
            ForeColor = Color.FromArgb(203, 213, 225),
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            Dock = DockStyle.Right,
            AutoSize = true,
            Padding = new Padding(0, 6, 0, 0)
        };

        statusBar.Controls.Add(_statusClockLabel);
        statusBar.Controls.Add(_statusDbLabel);

        var clockTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        clockTimer.Tick += (_, _) => _statusClockLabel.Text = DateTime.Now.ToString("dd MMM yyyy  hh:mm:ss tt");
        clockTimer.Start();

        // 4. Main Content Area
        _contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(241, 245, 249),
            Padding = new Padding(14)
        };

        Controls.Add(_contentHost);
        Controls.Add(sideBar);
        Controls.Add(topBar);
        Controls.Add(statusBar);

        KeyDown += OnGlobalHotkeyDown;
        Load += (_, _) => ShowPosCounterScreen();
    }

    private void AddNavButton(FlowLayoutPanel parent, string key, string text, Action onClick)
    {
        var btn = new Button
        {
            Text = text,
            Width = 210,
            Height = 40,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(226, 232, 240),
            BackColor = Color.FromArgb(30, 41, 59),
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 0, 5)
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(51, 65, 85);
        btn.Click += (_, _) =>
        {
            HighlightNav(key);
            onClick();
        };
        _navButtons[key] = btn;
        parent.Controls.Add(btn);
    }

    private void HighlightNav(string activeKey)
    {
        foreach (var kv in _navButtons)
        {
            if (kv.Key.Equals(activeKey, StringComparison.OrdinalIgnoreCase))
            {
                kv.Value.BackColor = Color.FromArgb(2, 132, 199);
                kv.Value.ForeColor = Color.White;
            }
            else
            {
                kv.Value.BackColor = Color.FromArgb(30, 41, 59);
                kv.Value.ForeColor = Color.FromArgb(226, 232, 240);
            }
        }
    }

    private void OnGlobalHotkeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.F1:
                e.Handled = true;
                ShowDashboardScreen();
                break;
            case Keys.F2:
                e.Handled = true;
                ShowPosCounterScreen();
                break;
            case Keys.F3:
                e.Handled = true;
                ShowProductsScreen();
                break;
            case Keys.F4:
                e.Handled = true;
                ShowPurchasesScreen();
                break;
            case Keys.F5:
                e.Handled = true;
                ShowLedgerScreen("customer");
                break;
            case Keys.F6:
                e.Handled = true;
                ShowLedgerScreen("supplier");
                break;
            case Keys.F7:
                e.Handled = true;
                ShowCashDrawerScreen();
                break;
            case Keys.F8:
                e.Handled = true;
                ShowReportsScreen();
                break;
            case Keys.F9:
                if (_posGrid != null && _posGrid.Visible)
                {
                    e.Handled = true;
                    _ = PostAndPrintPosInvoiceAsync(printThermal: true);
                }
                break;
            case Keys.F12:
                e.Handled = true;
                OpenHybridWebViewWindow("/dashboard");
                break;
        }
    }

    // =========================================================================
    // 1. LIGHTNING-FAST WASEELA-STYLE POS COUNTER SCREEN (F2)
    // =========================================================================
    private void ShowPosCounterScreen()
    {
        HighlightNav("pos");
        _headerTitleLabel.Text = "⚡ POS COUNTER BILLING — KEYBOARD-FIRST NATIVE GRID (F2)";
        _contentHost.Controls.Clear();

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 880,
            BackColor = Color.FromArgb(226, 232, 240)
        };

        // Left Pane: Fast Barcode / Search Bar + Native DataGridView Invoice Lines
        var leftPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12) };

        var scanBar = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = Color.FromArgb(248, 250, 252), Padding = new Padding(8) };
        var scanLbl = new Label
        {
            Text = "Barcode / Medicine Lookup (Enter):",
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(8, 15)
        };
        _posBarcodeBox = new TextBox
        {
            Location = new Point(255, 11),
            Width = 380,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            PlaceholderText = "Scan barcode or type medicine / generic name + press Enter..."
        };
        _posBarcodeBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await LookupAndAddPosProductAsync(_posBarcodeBox.Text.Trim());
            }
            else if (e.KeyCode == Keys.Down && _posProductLookupGrid is { Rows.Count: > 0 })
            {
                e.SuppressKeyPress = true;
                _posProductLookupGrid.Focus();
            }
        };
        _posBarcodeBox.TextChanged += async (_, _) =>
        {
            await RefreshPosProductCatalogLookupAsync(_posBarcodeBox.Text.Trim());
        };

        var clearCartBtn = new Button
        {
            Text = "Clear Bill (Esc)",
            Location = new Point(650, 10),
            Width = 130,
            Height = 32,
            BackColor = Color.FromArgb(239, 68, 68),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };
        clearCartBtn.FlatAppearance.BorderSize = 0;
        clearCartBtn.Click += (_, _) =>
        {
            _posGrid?.Rows.Clear();
            RecalculatePosTotals();
            _posBarcodeBox?.Focus();
        };

        scanBar.Controls.Add(scanLbl);
        scanBar.Controls.Add(_posBarcodeBox);
        scanBar.Controls.Add(clearCartBtn);

        _posGrid = CreateStyledDataGrid();
        _posGrid.ReadOnly = false;
        _posGrid.AllowUserToDeleteRows = true;
        _posGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ProductId", HeaderText = "ID", Width = 50, ReadOnly = true });
        _posGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ProductName", HeaderText = "Medicine / Item Name", Width = 250, ReadOnly = true });
        _posGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "BatchNo", HeaderText = "Batch", Width = 90 });
        _posGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Expiry", HeaderText = "Expiry", Width = 85, ReadOnly = true });
        _posGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Stock", HeaderText = "Avail", Width = 65, ReadOnly = true });
        _posGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Qty", HeaderText = "Qty", Width = 65 });
        _posGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Price", HeaderText = "Unit Price", Width = 90 });
        _posGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "DiscPct", HeaderText = "Disc %", Width = 70 });
        _posGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SubTotal", HeaderText = "Line Total", Width = 105, ReadOnly = true });

        _posGrid.CellEndEdit += (_, _) => RecalculatePosTotals();
        _posGrid.RowsRemoved += (_, _) => RecalculatePosTotals();

        leftPanel.Controls.Add(_posGrid);
        leftPanel.Controls.Add(scanBar);

        // Right Pane: Live Medicine Search Table + Totals & Checkout Box
        var rightPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12) };

        var lookupHeader = new Label
        {
            Text = "Instant Medicine & Salt Lookup (Double-Click or Enter to Add):",
            Dock = DockStyle.Top,
            Height = 26,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 41, 59)
        };

        _posProductLookupGrid = CreateStyledDataGrid();
        _posProductLookupGrid.Dock = DockStyle.Top;
        _posProductLookupGrid.Height = 280;
        _posProductLookupGrid.Columns.Add("Id", "ID");
        _posProductLookupGrid.Columns["Id"]!.Width = 45;
        _posProductLookupGrid.Columns.Add("Name", "Medicine Name");
        _posProductLookupGrid.Columns["Name"]!.Width = 175;
        _posProductLookupGrid.Columns.Add("Formulation", "Generic / Salt");
        _posProductLookupGrid.Columns["Formulation"]!.Width = 115;
        _posProductLookupGrid.Columns.Add("Stock", "Stock");
        _posProductLookupGrid.Columns["Stock"]!.Width = 55;
        _posProductLookupGrid.Columns.Add("Price", "Retail Rs.");
        _posProductLookupGrid.Columns["Price"]!.Width = 75;
        _posProductLookupGrid.Columns.Add("Rack", "Rack");
        _posProductLookupGrid.Columns["Rack"]!.Width = 55;

        _posProductLookupGrid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                var pid = Convert.ToInt64(_posProductLookupGrid.Rows[e.RowIndex].Cells["Id"].Value);
                await AddProductByIdToPosGridAsync(pid);
            }
        };
        _posProductLookupGrid.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && _posProductLookupGrid.CurrentRow != null)
            {
                e.SuppressKeyPress = true;
                var pid = Convert.ToInt64(_posProductLookupGrid.CurrentRow.Cells["Id"].Value);
                await AddProductByIdToPosGridAsync(pid);
                _posBarcodeBox?.Focus();
            }
        };

        // Totals & Payment Panel
        var checkoutBox = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(248, 250, 252),
            Padding = new Padding(14)
        };

        int y = 10;
        checkoutBox.Controls.Add(new Label { Text = "Customer:", Location = new Point(12, y + 4), AutoSize = true, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold) });
        _posCustomerCombo = new ComboBox { Location = new Point(135, y), Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
        checkoutBox.Controls.Add(_posCustomerCombo);

        y += 36;
        checkoutBox.Controls.Add(new Label { Text = "Sale Mode:", Location = new Point(12, y + 4), AutoSize = true, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold) });
        _posSaleTypeCombo = new ComboBox { Location = new Point(135, y), Width = 125, DropDownStyle = ComboBoxStyle.DropDownList };
        _posSaleTypeCombo.Items.AddRange(["retail", "wholesale"]);
        _posSaleTypeCombo.SelectedIndex = 0;

        _posPaymentTypeCombo = new ComboBox { Location = new Point(270, y), Width = 125, DropDownStyle = ComboBoxStyle.DropDownList };
        _posPaymentTypeCombo.Items.AddRange(["cash", "credit", "card", "bank_transfer"]);
        _posPaymentTypeCombo.SelectedIndex = 0;
        checkoutBox.Controls.Add(_posSaleTypeCombo);
        checkoutBox.Controls.Add(_posPaymentTypeCombo);

        y += 38;
        checkoutBox.Controls.Add(new Label { Text = "Bill Discount (Rs):", Location = new Point(12, y + 4), AutoSize = true, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold) });
        _posDiscountBox = new TextBox { Text = "0", Location = new Point(155, y), Width = 110, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
        _posDiscountBox.TextChanged += (_, _) => RecalculatePosTotals();
        checkoutBox.Controls.Add(_posDiscountBox);

        y += 36;
        checkoutBox.Controls.Add(new Label { Text = "Cash Received:", Location = new Point(12, y + 4), AutoSize = true, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold) });
        _posCashReceivedBox = new TextBox { Text = "0", Location = new Point(155, y), Width = 110, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
        _posCashReceivedBox.TextChanged += (_, _) => RecalculatePosTotals();
        checkoutBox.Controls.Add(_posCashReceivedBox);

        y += 42;
        _posGrossLabel = new Label { Text = "Gross Total: Rs. 0.00", Location = new Point(12, y), AutoSize = true, Font = new Font("Segoe UI", 10.5F, FontStyle.Bold), ForeColor = Color.FromArgb(51, 65, 85) };
        checkoutBox.Controls.Add(_posGrossLabel);

        y += 26;
        _posDiscountTotalLabel = new Label { Text = "Total Discount: Rs. 0.00", Location = new Point(12, y), AutoSize = true, Font = new Font("Segoe UI", 10F, FontStyle.Regular), ForeColor = Color.FromArgb(220, 38, 38) };
        checkoutBox.Controls.Add(_posDiscountTotalLabel);

        y += 28;
        _posNetTotalLabel = new Label { Text = "NET PAYABLE: Rs. 0.00", Location = new Point(12, y), AutoSize = true, Font = new Font("Segoe UI", 16F, FontStyle.Bold), ForeColor = Color.FromArgb(2, 132, 199) };
        checkoutBox.Controls.Add(_posNetTotalLabel);

        y += 34;
        _posChangeLabel = new Label { Text = "Change Back: Rs. 0.00", Location = new Point(12, y), AutoSize = true, Font = new Font("Segoe UI", 11F, FontStyle.Bold), ForeColor = Color.FromArgb(22, 163, 74) };
        checkoutBox.Controls.Add(_posChangeLabel);

        y += 40;
        var postPrintBtn = new Button
        {
            Text = "🖨️ POST & THERMAL PRINT (F9)",
            Location = new Point(12, y),
            Width = 240,
            Height = 46,
            BackColor = Color.FromArgb(22, 163, 74),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        postPrintBtn.FlatAppearance.BorderSize = 0;
        postPrintBtn.Click += async (_, _) => await PostAndPrintPosInvoiceAsync(printThermal: true);

        var postOnlyBtn = new Button
        {
            Text = "💾 SAVE ONLY",
            Location = new Point(262, y),
            Width = 135,
            Height = 46,
            BackColor = Color.FromArgb(2, 132, 199),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        postOnlyBtn.FlatAppearance.BorderSize = 0;
        postOnlyBtn.Click += async (_, _) => await PostAndPrintPosInvoiceAsync(printThermal: false);

        checkoutBox.Controls.Add(postPrintBtn);
        checkoutBox.Controls.Add(postOnlyBtn);

        rightPanel.Controls.Add(checkoutBox);
        rightPanel.Controls.Add(_posProductLookupGrid);
        rightPanel.Controls.Add(lookupHeader);

        split.Panel1.Controls.Add(leftPanel);
        split.Panel2.Controls.Add(rightPanel);
        _contentHost.Controls.Add(split);

        _ = LoadPosInitialDataAsync();
        _posBarcodeBox.Focus();
    }

    private async Task LoadPosInitialDataAsync()
    {
        if (_posCustomerCombo != null)
        {
            _posCustomerCombo.Items.Clear();
            _posCustomerCombo.Items.Add(new ComboItem(0, "Walk-In Customer (Cash)"));
            var customers = await _db.QueryListAsync("SELECT id, name, phone FROM customers ORDER BY name ASC LIMIT 200");
            foreach (var c in customers)
            {
                var id = Convert.ToInt64(c["id"]);
                var name = c.GetValueOrDefault("name")?.ToString() ?? "Customer";
                var phone = c.GetValueOrDefault("phone")?.ToString();
                _posCustomerCombo.Items.Add(new ComboItem(id, string.IsNullOrWhiteSpace(phone) ? name : $"{name} ({phone})"));
            }
            _posCustomerCombo.SelectedIndex = 0;
        }

        await RefreshPosProductCatalogLookupAsync("");
    }

    private async Task RefreshPosProductCatalogLookupAsync(string query)
    {
        if (_posProductLookupGrid == null) return;
        List<Dictionary<string, object?>> rows;
        if (string.IsNullOrWhiteSpace(query))
        {
            rows = await _db.QueryListAsync(
                "SELECT id, name, formulation, quantity, unit_sale_price, pack_sale_price, rack FROM products ORDER BY name ASC LIMIT 60");
        }
        else
        {
            rows = await _db.QueryListAsync(
                @"SELECT id, name, formulation, quantity, unit_sale_price, pack_sale_price, rack
                  FROM products
                  WHERE name LIKE @q OR product_code LIKE @q OR barcode LIKE @q OR formulation LIKE @q
                  ORDER BY name ASC LIMIT 60",
                new { q = $"%{query}%" });
        }

        _posProductLookupGrid.Rows.Clear();
        foreach (var r in rows)
        {
            var unitPrice = AccountService.ToDecimal(r.GetValueOrDefault("unit_sale_price"));
            if (unitPrice <= 0m) unitPrice = AccountService.ToDecimal(r.GetValueOrDefault("pack_sale_price"));
            _posProductLookupGrid.Rows.Add(new object[]
            {
                r["id"] ?? 0,
                r.GetValueOrDefault("name")?.ToString() ?? "-",
                r.GetValueOrDefault("formulation")?.ToString() ?? "-",
                r.GetValueOrDefault("quantity") ?? 0,
                unitPrice.ToString("0.00"),
                r.GetValueOrDefault("rack")?.ToString() ?? "-"
            });
        }
    }

    private async Task LookupAndAddPosProductAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;

        var exact = await _db.QuerySingleOrDefaultAsync(
            "SELECT * FROM products WHERE barcode = @q OR product_code = @q LIMIT 1",
            new { q = query });

        if (exact == null)
        {
            exact = await _db.QuerySingleOrDefaultAsync(
                "SELECT * FROM products WHERE name LIKE @q OR formulation LIKE @q ORDER BY name ASC LIMIT 1",
                new { q = $"%{query}%" });
        }

        if (exact == null)
        {
            MessageBox.Show($"No product found matching '{query}'.", "POS Lookup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await AddProductRecordToPosGridAsync(exact);
        if (_posBarcodeBox != null)
        {
            _posBarcodeBox.Clear();
            _posBarcodeBox.Focus();
        }
    }

    private async Task AddProductByIdToPosGridAsync(long productId)
    {
        var prod = await _db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @id", new { id = productId });
        if (prod != null)
        {
            await AddProductRecordToPosGridAsync(prod);
        }
    }

    private async Task AddProductRecordToPosGridAsync(Dictionary<string, object?> product)
    {
        if (_posGrid == null) return;
        var pid = AccountService.ToLong(product["id"]);

        // If already in grid, increment Qty
        foreach (DataGridViewRow existingRow in _posGrid.Rows)
        {
            if (existingRow.IsNewRow) continue;
            if (Convert.ToInt64(existingRow.Cells["ProductId"].Value) == pid)
            {
                var curQty = Convert.ToInt32(existingRow.Cells["Qty"].Value ?? 1);
                existingRow.Cells["Qty"].Value = curQty + 1;
                RecalculatePosTotals();
                return;
            }
        }

        var batch = await _db.QuerySingleOrDefaultAsync(
            "SELECT * FROM batches WHERE product_id = @pid AND quantity > 0 ORDER BY expiry_date ASC, id ASC LIMIT 1",
            new { pid });

        var unitPrice = AccountService.ToDecimal(product.GetValueOrDefault("unit_sale_price"));
        if (unitPrice <= 0m) unitPrice = AccountService.ToDecimal(product.GetValueOrDefault("pack_sale_price"));

        var batchNo = batch?.GetValueOrDefault("batch_number")?.ToString() ?? "DEFAULT";
        var expiry = batch?.GetValueOrDefault("expiry_date")?.ToString() ?? "-";
        var avail = ProductAvailableQty(product, batch);

        _posGrid.Rows.Add(new object[]
        {
            pid,
            product.GetValueOrDefault("name")?.ToString() ?? "Item",
            batchNo,
            expiry,
            avail,
            1,
            unitPrice.ToString("0.00"),
            "0",
            unitPrice.ToString("0.00")
        });

        RecalculatePosTotals();
    }

    private static int ProductAvailableQty(Dictionary<string, object?> product, Dictionary<string, object?>? batch)
    {
        if (batch != null && int.TryParse(batch.GetValueOrDefault("quantity")?.ToString(), out var bq) && bq > 0)
        {
            return bq;
        }
        return int.TryParse(product.GetValueOrDefault("quantity")?.ToString(), out var pq) ? pq : 0;
    }

    private (decimal Gross, decimal ItemDiscount, decimal BillDiscount, decimal NetTotal) RecalculatePosTotals()
    {
        if (_posGrid == null) return (0m, 0m, 0m, 0m);

        decimal gross = 0m;
        decimal itemDiscTotal = 0m;

        foreach (DataGridViewRow row in _posGrid.Rows)
        {
            if (row.IsNewRow) continue;
            var qty = AccountService.ToDecimal(row.Cells["Qty"].Value);
            var price = AccountService.ToDecimal(row.Cells["Price"].Value);
            var discPct = AccountService.ToDecimal(row.Cells["DiscPct"].Value);

            var lineGross = Math.Round(qty * price, 2);
            var lineDisc = Math.Round(lineGross * (discPct / 100m), 2);
            var lineNet = Math.Max(0m, lineGross - lineDisc);

            row.Cells["SubTotal"].Value = lineNet.ToString("0.00");
            gross += lineGross;
            itemDiscTotal += lineDisc;
        }

        var billDisc = AccountService.ToDecimal(_posDiscountBox?.Text);
        var net = Math.Max(0m, gross - itemDiscTotal - billDisc);
        var cashRec = AccountService.ToDecimal(_posCashReceivedBox?.Text);
        var change = cashRec > net ? cashRec - net : 0m;

        if (_posGrossLabel != null) _posGrossLabel.Text = $"Gross Total: Rs. {gross:N2}";
        if (_posDiscountTotalLabel != null) _posDiscountTotalLabel.Text = $"Total Discount: Rs. {(itemDiscTotal + billDisc):N2}";
        if (_posNetTotalLabel != null) _posNetTotalLabel.Text = $"NET PAYABLE: Rs. {net:N2}";
        if (_posChangeLabel != null) _posChangeLabel.Text = $"Change Back: Rs. {change:N2}";

        return (gross, itemDiscTotal, billDisc, net);
    }

    private async Task PostAndPrintPosInvoiceAsync(bool printThermal)
    {
        if (_posGrid == null || _posGrid.Rows.Count == 0)
        {
            MessageBox.Show("Please add at least one medicine/item to the bill before posting.", "POS Billing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var itemsPayload = new List<Dictionary<string, object?>>();
        var printItems = new List<PrintInvoiceItemViewModel>();

        foreach (DataGridViewRow row in _posGrid.Rows)
        {
            if (row.IsNewRow || row.Cells["ProductId"].Value == null) continue;
            var pid = AccountService.ToLong(row.Cells["ProductId"].Value);
            var qty = Math.Max(1, (int)AccountService.ToDecimal(row.Cells["Qty"].Value));
            var price = AccountService.ToDecimal(row.Cells["Price"].Value);
            var discPct = AccountService.ToDecimal(row.Cells["DiscPct"].Value);
            var subTotal = AccountService.ToDecimal(row.Cells["SubTotal"].Value);
            var batchNo = row.Cells["BatchNo"].Value?.ToString() ?? "";
            var expiry = row.Cells["Expiry"].Value?.ToString() ?? "";

            itemsPayload.Add(new Dictionary<string, object?>
            {
                ["product_id"] = pid,
                ["quantity"] = qty,
                ["price"] = price,
                ["item_discount_percentage"] = discPct,
                ["batch_number"] = batchNo,
                ["expiry"] = expiry,
                ["sub_total"] = subTotal
            });

            printItems.Add(new PrintInvoiceItemViewModel
            {
                ProductName = row.Cells["ProductName"].Value?.ToString() ?? "Item",
                Quantity = qty,
                Price = price,
                ItemDiscountPercentage = discPct,
                SubTotal = subTotal,
                BatchNumber = batchNo,
                Expiry = expiry
            });
        }

        if (itemsPayload.Count == 0) return;

        var totals = RecalculatePosTotals();
        var seqService = new DocumentSequenceService(_db);
        var postedNumber = await seqService.NextAsync("sale_invoice", "SI-");
        var selectedCustomer = _posCustomerCombo?.SelectedItem as ComboItem;
        long? customerId = selectedCustomer is { Id: > 0 } ? selectedCustomer.Id : null;
        var payType = _posPaymentTypeCombo?.SelectedItem?.ToString() ?? "cash";
        var saleType = _posSaleTypeCombo?.SelectedItem?.ToString() ?? "retail";
        var cashReceived = AccountService.ToDecimal(_posCashReceivedBox?.Text);
        if (cashReceived <= 0m && payType == "cash") cashReceived = totals.NetTotal;

        var invoiceId = await _db.InsertAsync("sale_invoices", new Dictionary<string, object?>
        {
            ["posted_number"] = postedNumber,
            ["date"] = DateTime.Now.ToString("yyyy-MM-dd"),
            ["customer_id"] = customerId,
            ["sale_type"] = saleType,
            ["invoice_type"] = payType == "credit" ? "credit" : "cash",
            ["payment_method"] = payType == "credit" ? "cash" : payType,
            ["gross_amount"] = totals.Gross,
            ["item_discount"] = totals.ItemDiscount,
            ["discount_amount"] = totals.BillDiscount,
            ["tax_amount"] = 0m,
            ["total"] = totals.NetTotal,
            ["total_receive"] = payType == "credit" ? 0m : totals.NetTotal
        });

        foreach (var item in itemsPayload)
        {
            var pid = AccountService.ToLong(item["product_id"]);
            var qty = Convert.ToInt32(item["quantity"]);

            await _db.InsertAsync("sale_invoice_items", new Dictionary<string, object?>
            {
                ["sale_invoice_id"] = invoiceId,
                ["product_id"] = pid,
                ["pack_size"] = 1,
                ["quantity"] = qty,
                ["price"] = item["price"],
                ["item_discount_percentage"] = item["item_discount_percentage"],
                ["sub_total"] = item["sub_total"],
                ["batch_number"] = item["batch_number"],
                ["expiry"] = item["expiry"]
            });

            await _db.ExecuteAsync(
                "UPDATE products SET quantity = MAX(0, COALESCE(quantity, 0) - @qty) WHERE id = @pid",
                new { qty, pid });
        }

        if (printThermal)
        {
            var receiptText =
                $"KAROBAR PHARMACY & POS\n" +
                $"Invoice: {postedNumber}   Date: {DateTime.Now:dd-MMM-yyyy HH:mm}\n" +
                $"Customer: {selectedCustomer?.Label ?? "Walk-In"}\n" +
                $"----------------------------------------\n" +
                string.Join("\n", printItems.Select(i => $"{i.ProductName} x{i.Quantity} @ {i.Price:0.00} = {i.SubTotal:0.00}")) +
                $"\n----------------------------------------\n" +
                $"Gross: Rs. {totals.Gross:N2}\n" +
                $"Discount: Rs. {(totals.ItemDiscount + totals.BillDiscount):N2}\n" +
                $"NET TOTAL: Rs. {totals.NetTotal:N2}\n";

            var escPosBytes = _rawPrinter.BuildEscPosReceiptPayload(receiptText, kickDrawer: true, cutPaper: true);
            var preferredPrinter = KarobarDesktopBridge.ReadPreferredPrinterConfig();
            if (!string.IsNullOrWhiteSpace(preferredPrinter))
            {
                _rawPrinter.SendRawBytesToPrinter(preferredPrinter, escPosBytes, $"Karobar Receipt {postedNumber}");
            }
        }

        _posGrid.Rows.Clear();
        if (_posDiscountBox != null) _posDiscountBox.Text = "0";
        if (_posCashReceivedBox != null) _posCashReceivedBox.Text = "0";
        RecalculatePosTotals();
        await RefreshPosProductCatalogLookupAsync(_posBarcodeBox?.Text ?? "");

        MessageBox.Show(
            $"Invoice {postedNumber} posted (Rs. {totals.NetTotal:N2}) {(printThermal ? "& sent to thermal spooler!" : "successfully!")}",
            "Invoice Posted",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        _posBarcodeBox?.Focus();
    }

    // =========================================================================
    // 2. EXECUTIVE DASHBOARD SCREEN (F1)
    // =========================================================================
    private async void ShowDashboardScreen()
    {
        HighlightNav("dashboard");
        _headerTitleLabel.Text = "📊 EXECUTIVE STORE DASHBOARD (F1)";
        _contentHost.Controls.Clear();

        var totalProducts = await _db.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM products");
        var totalCustomers = await _db.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM customers");
        var totalSuppliers = await _db.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM suppliers");
        var totalSales = await _db.ExecuteScalarAsync<decimal>("SELECT COALESCE(SUM(total), 0) FROM sale_invoices WHERE status IS NULL OR status != 'reversed'");
        var totalPurchases = await _db.ExecuteScalarAsync<decimal>("SELECT COALESCE(SUM(total_amount), 0) FROM purchase_invoices WHERE status IS NULL OR status != 'reversed'");
        var lowStockCount = await _db.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM products WHERE COALESCE(quantity, 0) <= 10");

        var cardsFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 135,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        cardsFlow.Controls.Add(CreateKpiCard("TOTAL REVENUE", $"Rs. {totalSales:N2}", Color.FromArgb(2, 132, 199)));
        cardsFlow.Controls.Add(CreateKpiCard("TOTAL PURCHASES", $"Rs. {totalPurchases:N2}", Color.FromArgb(124, 58, 237)));
        cardsFlow.Controls.Add(CreateKpiCard("ACTIVE MEDICINES", $"{totalProducts:N0} Items", Color.FromArgb(22, 163, 74)));
        cardsFlow.Controls.Add(CreateKpiCard("LOW STOCK ALERTS", $"{lowStockCount:N0} Items", Color.FromArgb(220, 38, 38)));
        cardsFlow.Controls.Add(CreateKpiCard("CUSTOMERS & SUPPLIERS", $"{totalCustomers} / {totalSuppliers}", Color.FromArgb(217, 119, 6)));

        var recentGrid = CreateStyledDataGrid();
        var recentSales = await _db.QueryListAsync(
            @"SELECT s.id, s.posted_number, s.date, COALESCE(c.name, 'Walk-In Customer') AS customer_name,
                     s.sale_type, s.invoice_type, s.total, s.total_receive
              FROM sale_invoices s
              LEFT JOIN customers c ON c.id = s.customer_id
              ORDER BY s.id DESC LIMIT 50");
        PopulateGridFromDictionaries(recentGrid, recentSales);

        var gridHeader = new Label
        {
            Text = "Recent Counter Sale Invoices (Live SQLite WAL Query):",
            Dock = DockStyle.Top,
            Height = 30,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold)
        };

        _contentHost.Controls.Add(recentGrid);
        _contentHost.Controls.Add(gridHeader);
        _contentHost.Controls.Add(cardsFlow);
    }

    // =========================================================================
    // 3. PRODUCTS, BATCHES & INVENTORY MASTER SCREEN (F3)
    // =========================================================================
    private async void ShowProductsScreen()
    {
        HighlightNav("products");
        _headerTitleLabel.Text = "💊 PRODUCTS, FORMULATIONS & BATCH INVENTORY (F3)";
        _contentHost.Controls.Clear();

        var topTools = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.White, Padding = new Padding(10) };
        var searchBox = new TextBox
        {
            Location = new Point(12, 11),
            Width = 360,
            Font = new Font("Segoe UI", 10F),
            PlaceholderText = "Search by medicine name, code, barcode, generic salt, or rack..."
        };

        var addBtn = new Button
        {
            Text = "+ Quick Add Medicine",
            Location = new Point(390, 9),
            Width = 170,
            Height = 32,
            BackColor = Color.FromArgb(2, 132, 199),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
        };
        addBtn.FlatAppearance.BorderSize = 0;

        var grid = CreateStyledDataGrid();

        async Task LoadProductsAsync(string q)
        {
            var rows = string.IsNullOrWhiteSpace(q)
                ? await _db.QueryListAsync(
                    @"SELECT p.id, p.product_code, p.name, p.formulation, b.name AS brand, c.name AS category,
                             p.pack_size, p.quantity, p.unit_purchase_price, p.unit_sale_price, p.whole_sale_unit_price, p.rack, p.barcode
                      FROM products p
                      LEFT JOIN brands b ON b.id = p.brand_id
                      LEFT JOIN categories c ON c.id = p.category_id
                      ORDER BY p.id DESC LIMIT 250")
                : await _db.QueryListAsync(
                    @"SELECT p.id, p.product_code, p.name, p.formulation, b.name AS brand, c.name AS category,
                             p.pack_size, p.quantity, p.unit_purchase_price, p.unit_sale_price, p.whole_sale_unit_price, p.rack, p.barcode
                      FROM products p
                      LEFT JOIN brands b ON b.id = p.brand_id
                      LEFT JOIN categories c ON c.id = p.category_id
                      WHERE p.name LIKE @q OR p.product_code LIKE @q OR p.barcode LIKE @q OR p.formulation LIKE @q
                      ORDER BY p.name ASC LIMIT 250",
                    new { q = $"%{q}%" });
            PopulateGridFromDictionaries(grid, rows);
        }

        searchBox.TextChanged += async (_, _) => await LoadProductsAsync(searchBox.Text.Trim());
        addBtn.Click += (_, _) => OpenHybridWebViewWindow("/products/create");

        topTools.Controls.Add(searchBox);
        topTools.Controls.Add(addBtn);

        _contentHost.Controls.Add(grid);
        _contentHost.Controls.Add(topTools);

        await LoadProductsAsync("");
        searchBox.Focus();
    }

    // =========================================================================
    // 4. PURCHASE INVOICES & STOCK RECEIVING SCREEN (F4)
    // =========================================================================
    private async void ShowPurchasesScreen()
    {
        HighlightNav("purchases");
        _headerTitleLabel.Text = "📦 PURCHASE INVOICES & SUPPLIER RECEIVING (F4)";
        _contentHost.Controls.Clear();

        var topBar = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.White, Padding = new Padding(10) };
        var newPurchaseBtn = new Button
        {
            Text = "+ New Purchase Invoice",
            Location = new Point(12, 9),
            Width = 190,
            Height = 32,
            BackColor = Color.FromArgb(22, 163, 74),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
        };
        newPurchaseBtn.FlatAppearance.BorderSize = 0;
        newPurchaseBtn.Click += (_, _) => OpenHybridWebViewWindow("/purchase-invoices/create");
        topBar.Controls.Add(newPurchaseBtn);

        var grid = CreateStyledDataGrid();
        var rows = await _db.QueryListAsync(
            @"SELECT pi.id, pi.posted_number, pi.posted_date, s.name AS supplier, pi.invoice_number AS supplier_inv_no,
                     pi.invoice_amount, pi.discount_amount, pi.tax_amount, pi.total_amount, pi.total_paid
              FROM purchase_invoices pi
              LEFT JOIN suppliers s ON s.id = pi.supplier_id
              ORDER BY pi.id DESC LIMIT 200");
        PopulateGridFromDictionaries(grid, rows);

        _contentHost.Controls.Add(grid);
        _contentHost.Controls.Add(topBar);
    }

    // =========================================================================
    // 5. SALE INVOICES HISTORY SCREEN
    // =========================================================================
    private async void ShowSaleInvoicesListScreen()
    {
        HighlightNav("sales_list");
        _headerTitleLabel.Text = "🧾 SALE INVOICES REGISTER";
        _contentHost.Controls.Clear();

        var grid = CreateStyledDataGrid();
        var rows = await _db.QueryListAsync(
            @"SELECT s.id, s.posted_number, s.date, COALESCE(c.name, 'Walk-In') AS customer,
                     s.sale_type, s.invoice_type, s.gross_amount, s.discount_amount, s.total, s.total_receive, s.fbr_invoice_number
              FROM sale_invoices s
              LEFT JOIN customers c ON c.id = s.customer_id
              ORDER BY s.id DESC LIMIT 250");
        PopulateGridFromDictionaries(grid, rows);
        _contentHost.Controls.Add(grid);
    }

    // =========================================================================
    // 6. CUSTOMER & SUPPLIER KHATA LEDGERS (F5 / F6)
    // =========================================================================
    private async void ShowLedgerScreen(string partyType)
    {
        var isCustomer = partyType.Equals("customer", StringComparison.OrdinalIgnoreCase);
        HighlightNav(isCustomer ? "customer_ledger" : "supplier_ledger");
        _headerTitleLabel.Text = isCustomer ? "📒 CUSTOMER KHATA LEDGER (F5)" : "🏢 SUPPLIER KHATA LEDGER (F6)";
        _contentHost.Controls.Clear();

        var grid = CreateStyledDataGrid();
        var rows = isCustomer
            ? await _db.QueryListAsync(
                @"SELECT cl.id, cl.entry_date, c.name AS customer, cl.entry_type, cl.posted_number,
                         cl.invoice_total, cl.total_received, cl.credited_amount, cl.balance_row, cl.description
                  FROM customer_ledgers cl
                  LEFT JOIN customers c ON c.id = cl.customer_id
                  ORDER BY cl.id DESC LIMIT 250")
            : await _db.QueryListAsync(
                @"SELECT sl.id, sl.entry_date, s.name AS supplier, sl.entry_type, sl.posted_number,
                         sl.invoice_total, sl.debited_amount, sl.credit_adj, sl.balance_row, sl.description
                  FROM supplier_ledgers sl
                  LEFT JOIN suppliers s ON s.id = sl.supplier_id
                  ORDER BY sl.id DESC LIMIT 250");

        PopulateGridFromDictionaries(grid, rows);
        _contentHost.Controls.Add(grid);
    }

    // =========================================================================
    // 7. CASH DRAWER & FINANCIAL ACCOUNTS (F7)
    // =========================================================================
    private async void ShowCashDrawerScreen()
    {
        HighlightNav("drawer");
        _headerTitleLabel.Text = "💵 CASH DRAWER & TREASURY ACCOUNTS (F7)";
        _contentHost.Controls.Clear();

        var grid = CreateStyledDataGrid();
        var rows = await _db.QueryListAsync(
            "SELECT id, code, name, type, current_balance, is_default, is_active, updated_at FROM accounts ORDER BY id ASC");
        PopulateGridFromDictionaries(grid, rows);
        _contentHost.Controls.Add(grid);
    }

    // =========================================================================
    // 8. ANALYTICAL REPORTS CENTER (F8)
    // =========================================================================
    private async void ShowReportsScreen()
    {
        HighlightNav("reports");
        _headerTitleLabel.Text = "📈 ANALYTICAL REPORTS — CURRENT STOCK & VALUATION (F8)";
        _contentHost.Controls.Clear();

        var grid = CreateStyledDataGrid();
        var rows = await _db.QueryListAsync(
            @"SELECT p.id, p.product_code, p.name, b.name AS brand, p.quantity AS available_units,
                     p.avg_price AS unit_cost, p.unit_sale_price AS retail_price,
                     ROUND(COALESCE(p.quantity, 0) * COALESCE(p.avg_price, 0), 2) AS total_stock_value
              FROM products p
              LEFT JOIN brands b ON b.id = p.brand_id
              ORDER BY total_stock_value DESC LIMIT 300");
        PopulateGridFromDictionaries(grid, rows);
        _contentHost.Controls.Add(grid);
    }

    // =========================================================================
    // 9. ABUZAR WASEELA & SQLITE ONE-CLICK IMPORTER
    // =========================================================================
    private void ShowWaseelaImporterScreen()
    {
        HighlightNav("waseela");
        _headerTitleLabel.Text = "🔄 ABUZAR WASEELA & EXTERNAL DATABASE IMPORTER";
        _contentHost.Controls.Clear();

        var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(28) };
        var title = new Label
        {
            Text = "One-Click Abuzar Waseela & External SQLite Database Migrator",
            Font = new Font("Segoe UI", 15F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(24, 24)
        };
        var desc = new Label
        {
            Text = "Automatically detects Abuzar Waseela tables (tblItems, tblBatch, tblKhata, tblSuppliers) or Karobar SQLite schemas\nand imports Products, Batches, Expiry Dates, Suppliers, Customers, and Opening Khata Balances.",
            Font = new Font("Segoe UI", 10.5F),
            ForeColor = Color.FromArgb(71, 85, 105),
            AutoSize = true,
            Location = new Point(24, 65)
        };
        var openImporterBtn = new Button
        {
            Text = "Launch Interactive Database Mapping Wizard",
            Location = new Point(24, 130),
            Width = 320,
            Height = 44,
            BackColor = Color.FromArgb(2, 132, 199),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold)
        };
        openImporterBtn.FlatAppearance.BorderSize = 0;
        openImporterBtn.Click += (_, _) => OpenHybridWebViewWindow("/settings/sqlite-import");

        card.Controls.Add(title);
        card.Controls.Add(desc);
        card.Controls.Add(openImporterBtn);
        _contentHost.Controls.Add(card);
    }

    private void OpenHybridWebViewWindow(string route)
    {
        var baseUrl = _networkConfig.GetAppBaseUrl();
        var fullUrl = route.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? route
            : $"{baseUrl}{(route.StartsWith('/') ? "" : "/")}{route}";
        WebView2WindowManager.TryOpenExternalUrl(fullUrl);
    }

    // =========================================================================
    // UI HELPERS
    // =========================================================================
    private static Panel CreateKpiCard(string label, string value, Color accentColor)
    {
        var panel = new Panel
        {
            Width = 225,
            Height = 110,
            BackColor = Color.White,
            Margin = new Padding(0, 0, 14, 0),
            Padding = new Padding(14)
        };
        var strip = new Panel { Dock = DockStyle.Top, Height = 5, BackColor = accentColor };
        var lbl = new Label
        {
            Text = label,
            ForeColor = Color.FromArgb(100, 116, 139),
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            Location = new Point(14, 18),
            AutoSize = true
        };
        var val = new Label
        {
            Text = value,
            ForeColor = Color.FromArgb(15, 23, 42),
            Font = new Font("Segoe UI", 14.5F, FontStyle.Bold),
            Location = new Point(14, 46),
            AutoSize = true
        };
        panel.Controls.Add(val);
        panel.Controls.Add(lbl);
        panel.Controls.Add(strip);
        return panel;
    }

    private static DataGridView CreateStyledDataGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = Color.FromArgb(226, 232, 240),
            RowHeadersVisible = false,
            AllowUserToAddRows = false,
            AllowUserToResizeRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ReadOnly = true,
            Font = new Font("Segoe UI", 9.5F)
        };
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(15, 23, 42);
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        grid.ColumnHeadersHeight = 36;
        grid. EnableHeadersVisualStyles = false;
        grid.RowTemplate.Height = 32;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(224, 242, 254);
        grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(15, 23, 42);
        return grid;
    }

    private static void PopulateGridFromDictionaries(DataGridView grid, List<Dictionary<string, object?>> rows)
    {
        var table = new DataTable();
        if (rows.Count == 0)
        {
            table.Columns.Add("Info");
            table.Rows.Add("No records found.");
            grid.DataSource = table;
            return;
        }

        foreach (var col in rows[0].Keys)
        {
            table.Columns.Add(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(col.Replace('_', ' ')));
        }

        foreach (var r in rows)
        {
            var values = r.Values.Select(v => v?.ToString() ?? "").ToArray<object>();
            table.Rows.Add(values);
        }

        grid.DataSource = table;
    }

    private sealed record ComboItem(long Id, string Label)
    {
        public override string ToString() => Label;
    }
}
/// <summary>
/// Frameless native Windows Splash Form shown for &lt;1 second while Kestrel &amp; SQLite initialize.
/// </summary>
public sealed class NativeSplashForm : Form
{
    public NativeSplashForm(string? iconPath)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        Width = 480;
        Height = 280;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(15, 23, 42);

        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath) && iconPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
        {
            try { Icon = new Icon(iconPath); } catch { }
        }

        var titleLabel = new Label
        {
            Text = "Karobar-App Native",
            ForeColor = Color.FromArgb(248, 250, 252),
            Font = new Font("Segoe UI", 22F, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 130,
            Padding = new Padding(0, 45, 0, 0)
        };

        var subLabel = new Label
        {
            Text = "Starting Native Windows C# / .NET 9 Engine…",
            ForeColor = Color.FromArgb(56, 189, 248),
            Font = new Font("Segoe UI", 10.5F, FontStyle.Regular),
            AutoSize = false,
            TextAlign = ContentAlignment.TopCenter,
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 12, 0, 0)
        };

        Controls.Add(subLabel);
        Controls.Add(titleLabel);
    }
}
#endif

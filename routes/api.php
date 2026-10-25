<?php

use Illuminate\Support\Facades\Route;
use App\Http\Controllers\Auth\MeController;
use App\Http\Controllers\AccountController;
use App\Http\Controllers\AuthController;
use App\Http\Controllers\BatchController;
use App\Http\Controllers\BatchExpiryController;
use App\Http\Controllers\BackupController;
use App\Http\Controllers\BrandController;
use App\Http\Controllers\BrandImportController;
use App\Http\Controllers\CashDrawerController;
use App\Http\Controllers\CategoryController;
use App\Http\Controllers\CategoryImportController;
use App\Http\Controllers\CustomerController;
use App\Http\Controllers\CustomerImportController;
use App\Http\Controllers\CustomerLedgerController;
use App\Http\Controllers\DashboardController;
use App\Http\Controllers\DocumentHistoryController;
use App\Http\Controllers\ExpenseCategoryController;
use App\Http\Controllers\ExpenseController;
use App\Http\Controllers\InvoiceDraftController;
use App\Http\Controllers\PaymentController;
use App\Http\Controllers\LicenseController;
use App\Http\Controllers\PermissionController;
use App\Http\Controllers\ProductController;
use App\Http\Controllers\ProductImportController;
use App\Http\Controllers\PurchaseInvoiceController;
use App\Http\Controllers\PurchaseOrderController;
use App\Http\Controllers\PurchaseReturnController;
use App\Http\Controllers\ReportsController;
use App\Http\Controllers\RoleController;
use App\Http\Controllers\SaleInvoiceController;
use App\Http\Controllers\SaleReturnController;
use App\Http\Controllers\SettingController;
use App\Http\Controllers\StockAdjustmentController;
use App\Http\Controllers\SupplierController;
use App\Http\Controllers\SupplierImportController;
use App\Http\Controllers\SupplierLedgerController;
use App\Http\Controllers\ThemeSettingController;
use App\Http\Controllers\UserController;
use App\Http\Controllers\UserPreferenceController;
use App\Http\Controllers\NotificationController;
use App\Http\Controllers\UserDemandController;

/*
|--------------------------------------------------------------------------
| Public/Auth endpoints
|--------------------------------------------------------------------------
*/
Route::post('/login', [AuthController::class, 'login']);
Route::middleware(['auth:sanctum', 'token.expiry'])->get('/user', [AuthController::class, 'user']);
Route::middleware(['auth:sanctum', 'token.expiry'])->put('/profile', [AuthController::class, 'updateProfile']);
Route::middleware(['auth:sanctum', 'token.expiry'])->post('/logout', [AuthController::class, 'logout']);

// Password reset routes (public)
Route::post('/forgot-password', [AuthController::class, 'forgotPassword']);
Route::post('/reset-password', [AuthController::class, 'resetPassword']);

/*
|--------------------------------------------------------------------------
| Auth-only (NO license gate) — allow shell to render & license to activate
|--------------------------------------------------------------------------
*/
Route::middleware(['auth:sanctum', 'token.expiry'])->group(function () {
    // Password verification (for license management in settings)
    Route::post('/verify-password', [AuthController::class, 'confirmPassword']);

    // License endpoints
    Route::get('/license/status',     [LicenseController::class, 'status']);
    Route::post('/license/activate',  [LicenseController::class, 'activate']);
    Route::post('/license/deactivate',[LicenseController::class, 'deactivate']); // optional

    // Keep /me here so dashboard shell can mount pre-activation
    Route::get('/me', MeController::class);

    // Theme Settings - available before license activation (for shell rendering)
    Route::get('/theme-settings/active',          [ThemeSettingController::class, 'getActive']);
    Route::put('/theme-settings/active',          [ThemeSettingController::class, 'updateActive']);

    // Sale system mode & Currency — app-wide UI gating and formatting.
    // Any authenticated user needs this to render the correct UI, so it is
    // intentionally NOT gated by license or the settings.view permission.
    Route::get('/sale-system', [SettingController::class, 'saleSystem']);
    Route::get('/currency',    [SettingController::class, 'currency']);
});

/*
|--------------------------------------------------------------------------
| Auth + Licensed — EVERYTHING ELSE is gated
|--------------------------------------------------------------------------
*/
Route::middleware(['auth:sanctum', 'licensed', 'token.expiry'])->group(function () {
    Route::post('/auth/confirm-password', [AuthController::class, 'confirmPassword']);

// User preferences (window positions, sizes, etc.)
    Route::get('/preferences', [UserPreferenceController::class, 'index']);
    Route::put('/preferences', [UserPreferenceController::class, 'update']);
    Route::get('/preferences/{key}', [UserPreferenceController::class, 'show']);
    Route::post('/preferences/{key}', [UserPreferenceController::class, 'store']);

    // Users
    Route::resource('users', UserController::class);
    Route::put('/users/{user}/roles', [UserController::class, 'syncRoles']);
    Route::put('/users/{user}/permissions', [UserController::class, 'syncPermissions']);

    // Roles
    Route::get('/roles',          [RoleController::class, 'index']);
    Route::post('/roles',         [RoleController::class, 'store']);
    Route::get('/roles/{role}',   [RoleController::class, 'show']);
    Route::put('/roles/{role}',   [RoleController::class, 'update']);
    Route::delete('/roles/{role}',[RoleController::class, 'destroy']);

    // Permissions (single set — removed duplicate index)
    Route::get('/permissions',                     [PermissionController::class, 'index'])->middleware('permission:permission.view');
    Route::post('/permissions',                    [PermissionController::class, 'store'])->middleware('permission:permission.create');
    Route::delete('/permissions/{permission}',     [PermissionController::class, 'destroy'])->middleware('permission:permission.delete');

    // Quick searches
    Route::get('/brands/search',      [BrandController::class, 'search']);
    Route::get('/categories/search',  [CategoryController::class, 'search']);
    Route::get('/suppliers/search',   [SupplierController::class, 'search']);
    Route::get('/customers/search',   [CustomerController::class, 'search']);

    // Master data
    Route::get('/categories/export', [CategoryController::class, 'export'])->name('categories.export');
    Route::apiResource('categories', CategoryController::class);

    Route::get('/brands/export', [BrandController::class, 'export'])->name('brands.export');
    Route::apiResource('brands', BrandController::class);

    Route::get('/customers/export', [CustomerController::class, 'export'])->name('customers.export');
    Route::get('/customers/{customer}/credit-summary', [CustomerController::class, 'creditSummary']);
    Route::apiResource('customers', CustomerController::class);

    Route::get('/suppliers/export', [SupplierController::class, 'export']);
    Route::get('/suppliers/{supplier}/credit-summary', [SupplierController::class, 'creditSummary']);
    Route::apiResource('suppliers', SupplierController::class);

    // Products & batches
    Route::get('products/new-code',                [ProductController::class, 'generateNewCode']);
    Route::get('products/{product}/batches',      [BatchController::class, 'index']);
    Route::post('products/{product}/batches',     [BatchController::class, 'store']);
    Route::put('batches/{batch}',                 [BatchController::class, 'update']);
    Route::delete('batches/{batch}',              [BatchController::class, 'destroy']);
    Route::get('products/available-quantity',     [ProductController::class, 'availableQuantity']);
    Route::get('products/{product}/available-quantity', [ProductController::class, 'availableQuantity']);
    Route::get('/products/search',                [ProductController::class, 'search']);
    Route::get('/products/by-ids',               [ProductController::class, 'search']);
        // Optional extra endpoints (if you rely on them)
    Route::get('/products/export',                 [ProductController::class, 'export'])->middleware('can:product.export');
    Route::patch('/products/bulk-update-meta',     [ProductController::class, 'bulkUpdateMeta'])->middleware('can:product.update');
    Route::post('/products/bulk-destroy', [ProductController::class, 'bulkDestroy']);
    Route::apiResource('products', ProductController::class);

    // Purchases
    Route::get('purchase-invoices/new-code',      [PurchaseInvoiceController::class, 'generateNewCode']);
    Route::get('purchase-invoices/check-unique',  [PurchaseInvoiceController::class, 'checkUnique']);
    Route::get('purchase-invoices/search',       [PurchaseInvoiceController::class, 'search']);
    Route::post('purchase-invoices/{id}/reverse', [PurchaseInvoiceController::class, 'reverse']);
    Route::apiResource('purchase-invoices',       PurchaseInvoiceController::class);

    // Purchase returns
    Route::get('purchase-returns/new-code',       [PurchaseReturnController::class, 'generateNewCode']);
    Route::apiResource('purchase-returns',        PurchaseReturnController::class);

    // Drafts
    Route::post('invoice-drafts/{id}/post',       [InvoiceDraftController::class, 'post']);
    Route::apiResource('invoice-drafts',          InvoiceDraftController::class);

    // Sales
    Route::get('sale-invoices/new-code',          [SaleInvoiceController::class, 'generateNewCode']);
    Route::get('sale-invoices/search',            [SaleInvoiceController::class, 'search']);
    Route::post('sale-invoices/{id}/reverse',     [SaleInvoiceController::class, 'reverse']);
    Route::apiResource('sale-invoices',           SaleInvoiceController::class);

    // Document Audit History
    Route::get('/document-histories',             [DocumentHistoryController::class, 'index']);

    // Payments & Allocations (Project 3)
    Route::get('payments/open-invoices',          [PaymentController::class, 'openInvoices']);
    Route::post('payments/{id}/reverse',          [PaymentController::class, 'reverse']);
    Route::apiResource('payments',                PaymentController::class)->only(['index', 'show', 'store']);

    // Cash Drawer Register Management
    Route::get('cash-drawer/current',             [CashDrawerController::class, 'current']);
    Route::get('cash-drawer/session/{id}',        [CashDrawerController::class, 'show']);
    Route::post('cash-drawer/open',               [CashDrawerController::class, 'open']);
    Route::post('cash-drawer/transaction',        [CashDrawerController::class, 'recordTransaction']);
    Route::post('cash-drawer/close',              [CashDrawerController::class, 'close']);
    Route::get('cash-drawer/history',             [CashDrawerController::class, 'history']);

    // Financial Accounts & Balances
    Route::get('accounts',                        [AccountController::class, 'index']);
    Route::get('accounts/presets',                [AccountController::class, 'presets']);
    Route::post('accounts/seed-defaults',         [AccountController::class, 'seedDefaults']);
    Route::post('accounts',                       [AccountController::class, 'store']);
    Route::put('accounts/{id}',                   [AccountController::class, 'update']);
    Route::get('accounts/{id}/transactions',      [AccountController::class, 'transactions']);
    Route::post('accounts/transfer',              [AccountController::class, 'transfer']);
    Route::post('accounts/{id}/adjust',           [AccountController::class, 'adjust']);

    // Operating Expenses & Categories
    Route::get('expenses/summary',                [ExpenseController::class, 'summary']);
    Route::apiResource('expenses',                ExpenseController::class);
    Route::apiResource('expense-categories',      ExpenseCategoryController::class);

    // Sale returns
    Route::get('sale-returns/new-code',           [SaleReturnController::class, 'generateNewCode']);
    Route::apiResource('sale-returns',            SaleReturnController::class);

    // Purchase Orders
    Route::get('/purchase-orders/forecast',       [PurchaseOrderController::class, 'forecast']);
    Route::get('/purchase-orders/supplier-sort-order/{supplier}', [PurchaseOrderController::class, 'supplierSortOrder']);
    Route::post('/purchase-orders/{purchaseOrder}/receive', [PurchaseOrderController::class, 'receive']);
    Route::post('/purchase-orders/{purchaseOrder}/cancel', [PurchaseOrderController::class, 'cancel']);
    Route::apiResource('purchase-orders', PurchaseOrderController::class);

// Dashboard
    Route::get('/dashboard/summary',              [DashboardController::class, 'summary']);
    Route::get('/dashboard/near-expiry',          [DashboardController::class, 'nearExpiry']);
    Route::get('/dashboard/near-expiry/filters',  [DashboardController::class, 'nearExpiryFilters']);
    Route::get('/dashboard/invoice-counts',       [DashboardController::class, 'invoiceCounts']);
    Route::get('/dashboard/sales-by-brands',      [DashboardController::class, 'salesByBrands']);
    Route::get('/dashboard/top-products',         [DashboardController::class, 'topProducts']);
    Route::get('/dashboard/kpi-metrics',          [DashboardController::class, 'kpiMetrics']);

// Notifications
    Route::get('/notifications/low-stock',        [NotificationController::class, 'lowStock']);
    Route::get('/notifications/expiry-alerts',    [BatchExpiryController::class, 'expiryAlerts']);
    Route::post('/notifications/dismiss',         [NotificationController::class, 'dismiss']);
    Route::post('/notifications/dismiss-all',     [NotificationController::class, 'dismissAll']);

    // Batch Expiry Tracking & Warning System
    Route::get('/batch-expiry',                           [BatchExpiryController::class, 'index']);
    Route::put('/batch-expiry/product-warning/{product}', [BatchExpiryController::class, 'updateProductWarning']);
    Route::put('/batch-expiry/global-warning',            [BatchExpiryController::class, 'updateGlobalWarning']);

    // User Demands
    Route::get('/user-demands/count',             [UserDemandController::class, 'count']);
    Route::apiResource('user-demands',            UserDemandController::class);

    // Reports
    Route::get('/reports/cost-of-sale',           [ReportsController::class, 'costOfSale']);
    Route::get('/reports/cost-of-sale/pdf',       [ReportsController::class, 'costOfSalePdf']);
    Route::get('/reports/cost-of-sale-detail',    [ReportsController::class, 'costOfSaleDetail']);
    Route::get('/reports/cost-of-sale-detail/pdf', [ReportsController::class, 'costOfSaleDetailPdf']);
    Route::get('/reports/purchase-detail',        [ReportsController::class, 'purchaseDetail']);
    Route::get('/reports/purchase-detail/pdf',    [ReportsController::class, 'purchaseDetailPdf']);
    Route::get('/reports/sale-detail',            [ReportsController::class, 'saleDetail']);
    Route::get('/reports/sale-detail/pdf',        [ReportsController::class, 'saleDetailPdf']);
    Route::get('/reports/current-stock',          [ReportsController::class, 'currentStock']);
    Route::get('/reports/current-stock/pdf',      [ReportsController::class, 'currentStockPdf']);
    Route::get('/reports/stock-adjustment',       [ReportsController::class, 'stockAdjustment']);
    Route::get('/reports/stock-adjustment/pdf',   [ReportsController::class, 'stockAdjustmentPdf']);
    Route::get('/reports/product-comprehensive',  [ReportsController::class, 'productComprehensive']);
    Route::get('/reports/product-comprehensive/pdf', [ReportsController::class, 'productComprehensivePdf']);
    Route::get('/reports/near-expiry-product',     [ReportsController::class, 'nearExpiryProduct']);
    Route::get('/reports/near-expiry-product/pdf', [ReportsController::class, 'nearExpiryProductPdf']);
    Route::get('/reports/purchase-returns',        [ReportsController::class, 'purchaseReturns']);
    Route::get('/reports/sale-returns',            [ReportsController::class, 'saleReturns']);
    Route::get('/reports/purchase-summary',        [ReportsController::class, 'purchaseSummary']);
    Route::get('/reports/outstanding-aging',       [ReportsController::class, 'outstandingAging']);
    Route::get('/reports/overdue-work-queue',      [ReportsController::class, 'overdueWorkQueue']);
    Route::get('/reports/reorder',                 [ReportsController::class, 'reorderReport']);
    Route::get('/reports/net-profit',              [ReportsController::class, 'netProfit']);
    Route::put('/sale-invoices/{saleInvoice}/meta',[SaleInvoiceController::class, 'updateMeta']);


    // Settings
    Route::get('/settings',                       [SettingController::class, 'show']);
    Route::post('/settings',                      [SettingController::class, 'update']);

    // Theme Settings (CRUD operations require license)
    Route::get('/theme-settings',                 [ThemeSettingController::class, 'index']);
    Route::get('/theme-settings/{id}',            [ThemeSettingController::class, 'show']);
    Route::post('/theme-settings',                [ThemeSettingController::class, 'store']);
    Route::put('/theme-settings/{id}',            [ThemeSettingController::class, 'update']);
    Route::put('/theme-settings/{id}/activate',  [ThemeSettingController::class, 'activate']);
    Route::delete('/theme-settings/{id}',        [ThemeSettingController::class, 'destroy']);

    // Backups & Storage Destination
    Route::get('/backups/stats',                  [BackupController::class, 'stats']);
    Route::get('/backups/schedule',               [BackupController::class, 'getSchedule']);
    Route::post('/backups/schedule',              [BackupController::class, 'updateSchedule']);
    Route::post('/backups/test-destination',      [BackupController::class, 'testDestination']);
    Route::get('/backups/browse-directories',     [BackupController::class, 'browseDirectories']);
    Route::post('/backups/create-directory',      [BackupController::class, 'createDirectory']);
    Route::post('/backups/run-now',               [BackupController::class, 'runScheduledNow']);
    Route::get('/backups',                        [BackupController::class, 'index']);
    Route::post('/backups',                       [BackupController::class, 'store']);
    Route::get('/backups/{id}/download',          [BackupController::class, 'download']);
    Route::post('/backups/{id}/restore',          [BackupController::class, 'restore']);
    Route::delete('/backups/{id}',                [BackupController::class, 'destroy']);
    Route::post('/backups/upload/validate',       [BackupController::class, 'validateUpload']);
    Route::post('/backups/upload',                [BackupController::class, 'upload']);
    Route::post('/backups/upload/restore',        [BackupController::class, 'restoreFromUpload']);

    // Supplier Import
    Route::get('/suppliers/import/template',      [SupplierImportController::class, 'template'])->middleware('permission:supplier.import');
    Route::post('/suppliers/import/validate',     [SupplierImportController::class, 'validateUpload'])->middleware('permission:supplier.import');
    Route::post('/suppliers/import/commit',       [SupplierImportController::class, 'commit'])->middleware('permission:supplier.import');

    // Brands import
    Route::get('/brands/import/template',         [BrandImportController::class, 'template'])->middleware('permission:brand.import');
    Route::post('/brands/import/validate',        [BrandImportController::class, 'validateUpload'])->middleware('permission:brand.import');
    Route::post('/brands/import/commit',          [BrandImportController::class, 'commit'])->middleware('permission:brand.import');

    // Categories import
    Route::get('/categories/import/template',     [CategoryImportController::class, 'template'])->middleware('permission:category.import');
    Route::post('/categories/import/validate',    [CategoryImportController::class, 'validateUpload'])->middleware('permission:category.import');
    Route::post('/categories/import/commit',      [CategoryImportController::class, 'commit'])->middleware('permission:category.import');

    // Customers import
    Route::get('/customers/import/template',      [CustomerImportController::class, 'template'])->middleware('permission:customer.import');
    Route::post('/customers/import/validate',     [CustomerImportController::class, 'validateUpload'])->middleware('permission:customer.import');
    Route::post('/customers/import/commit',       [CustomerImportController::class, 'commit'])->middleware('permission:customer.import');

    // Customer wholesale prices
    Route::get('/customers/{customerId}/whole-sale-prices', function ($customerId) {
        $prices = \App\Models\CustomerWholeSalePrice::where('customer_id', $customerId)->get();
        return response()->json($prices);
    });

    // Product import
    Route::get('/products/import/template',       [ProductImportController::class, 'template'])->middleware('permission:product.import');
    Route::post('/products/import/validate',      [ProductImportController::class, 'validateUpload'])->middleware('permission:product.import');
    Route::post('/products/import/commit',        [ProductImportController::class, 'commit'])->middleware('permission:product.import');

    // Stock Adjustments
    Route::get('/stock-adjustments',              [StockAdjustmentController::class, 'index']);
    Route::post('/stock-adjustments',             [StockAdjustmentController::class, 'store']);
    Route::get('/stock-adjustments/new-code',     [StockAdjustmentController::class, 'newCode']);
    Route::get('/stock-adjustments/{id}',         [StockAdjustmentController::class, 'show']);
    Route::put('/stock-adjustments/{id}',         [StockAdjustmentController::class, 'update']);
    Route::delete('/stock-adjustments/{id}',      [StockAdjustmentController::class, 'destroy']);

    // Supplier Ledger
    Route::get('/supplier-ledger',                [SupplierLedgerController::class, 'index']);
    Route::post('/supplier-ledger',               [SupplierLedgerController::class, 'store']);
    Route::put('/supplier-ledger/bulk',           [SupplierLedgerController::class, 'bulkUpdate']);
    Route::delete('/supplier-ledger/{id}',        [SupplierLedgerController::class, 'destroy']);
    Route::post('/supplier-ledger/rebuild',       [SupplierLedgerController::class, 'rebuild']);
    Route::post('/supplier-ledger/settle-invoice',[SupplierLedgerController::class, 'settleInvoice']);

    // Customer Ledger
    Route::get('/customer-ledger',                [CustomerLedgerController::class, 'index']);
    Route::post('/customer-ledger',               [CustomerLedgerController::class, 'store']);
    Route::put('/customer-ledger/bulk',           [CustomerLedgerController::class, 'bulkUpdate']);
    Route::delete('/customer-ledger/{customerLedger}', [CustomerLedgerController::class, 'destroy']);
    Route::post('/customer-ledger/rebuild',       [CustomerLedgerController::class, 'rebuild']);
    Route::post('/customer-ledger/settle-invoice', [CustomerLedgerController::class, 'settleInvoice']);

    // Barcode Designer & Label Printing
    Route::get('/barcodes/templates',              [\App\Http\Controllers\BarcodeController::class, 'getTemplates']);
    Route::post('/barcodes/templates',             [\App\Http\Controllers\BarcodeController::class, 'saveTemplate']);
    Route::put('/barcodes/templates/{id}',         [\App\Http\Controllers\BarcodeController::class, 'updateTemplate']);
    Route::delete('/barcodes/templates/{id}',      [\App\Http\Controllers\BarcodeController::class, 'deleteTemplate']);
    Route::get('/barcodes/products-search',        [\App\Http\Controllers\BarcodeController::class, 'searchProducts']);
    Route::get('/barcodes/purchase-orders',        [\App\Http\Controllers\BarcodeController::class, 'getPurchaseOrders']);
    Route::get('/barcodes/scan',                   [\App\Http\Controllers\BarcodeController::class, 'scanProduct']);
});

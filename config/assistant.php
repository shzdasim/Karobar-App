<?php

return [
    'navigation' => [
        ['label' => 'Dashboard', 'path' => '/dashboard'],
        ['label' => 'Products', 'path' => '/products', 'permission' => 'product.view'],
        ['label' => 'Barcode POS', 'path' => '/sale-invoices/pos', 'permission_any' => ['barcode-pos.view', 'barcode-pos.create']],
        ['label' => 'Sale Invoices', 'path' => '/sale-invoices', 'permission' => 'sale-invoice.view'],
        ['label' => 'Purchase Invoices', 'path' => '/purchase-invoices', 'permission' => 'purchase-invoice.view'],
        ['label' => 'Purchase Orders', 'path' => '/purchase-orders', 'permission' => 'purchase-order.view'],
        ['label' => 'Customers', 'path' => '/customers', 'permission' => 'customer.view'],
        ['label' => 'Suppliers', 'path' => '/suppliers', 'permission' => 'supplier.view'],
        ['label' => 'Batch & Expiry', 'path' => '/batch-expiry', 'permission' => 'batch-expiry.view'],
        ['label' => 'Barcode Studio', 'path' => '/barcode-labels', 'permission_any' => ['barcode.view', 'barcode.print']],
        ['label' => 'User Demands', 'path' => '/user-demands', 'permission' => 'user-demands.view'],
        ['label' => 'Stocktake Sessions', 'path' => '/stocktake-sessions', 'permission' => 'stocktake.view'],
        ['label' => 'Quarantine & Disposal', 'path' => '/stock-disposals', 'permission' => 'stock-disposal.view'],
        ['label' => 'Customer Ledger', 'path' => '/customer-ledger', 'permission' => 'customer-ledger.view'],
        ['label' => 'Supplier Ledger', 'path' => '/supplier-ledger', 'permission' => 'supplier-ledger.view'],
        ['label' => 'Payment Vouchers', 'path' => '/payments', 'permission' => 'payment.view'],
        ['label' => 'Cash Drawer', 'path' => '/cash-drawer', 'permission' => 'cash-drawer.view'],
        ['label' => 'Financial Accounts', 'path' => '/accounts', 'permission' => 'account.view'],
        ['label' => 'Operating Expenses', 'path' => '/expenses', 'permission' => 'expense.view'],
        ['label' => 'Net Profit Report', 'path' => '/reports/net-profit', 'permission' => 'report.net-profit.view'],
        ['label' => 'Settings', 'path' => '/settings', 'permission' => 'settings.view'],
        ['label' => 'Users', 'path' => '/users', 'permission' => 'user.view'],
        ['label' => 'Backups', 'path' => '/backups', 'permission' => 'backup.view'],
    ],
];

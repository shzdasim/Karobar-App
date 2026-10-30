<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    /**
     * Add missing foreign-key, date, and search indexes across core ERP tables.
     * SQLite does not automatically index foreign keys, so these indexes eliminate
     * full-table scans on joins, eager loads, date filters, and clock/status lookups.
     */
    public function up(): void
    {
        $indexSpecs = [
            'products' => [
                'idx_products_brand_id'       => ['brand_id'],
                'idx_products_category_id'    => ['category_id'],
                'idx_products_supplier_id'    => ['supplier_id'],
                'idx_products_name'           => ['name'],
                'idx_products_quantity'       => ['quantity'],
                'idx_products_rack'           => ['rack'],
                'idx_products_active_qty'     => ['is_active', 'quantity'],
            ],
            'batches' => [
                'idx_batches_expiry_date'     => ['expiry_date'],
                'idx_batches_prod_expiry'     => ['product_id', 'expiry_date'],
                'idx_batches_prod_qty'        => ['product_id', 'quantity'],
                'idx_batches_qty_expiry'      => ['quantity', 'expiry_date'],
            ],
            'sale_invoices' => [
                'idx_sale_inv_customer_id'    => ['customer_id'],
                'idx_sale_inv_user_id'        => ['user_id'],
                'idx_sale_inv_date'           => ['date'],
                'idx_sale_inv_invoice_type'   => ['invoice_type'],
                'idx_sale_inv_due_date'       => ['due_date'],
                'idx_sale_inv_created_at'     => ['created_at'],
                'idx_sale_inv_cust_type_date' => ['customer_id', 'invoice_type', 'date'],
            ],
            'sale_invoice_items' => [
                'idx_sii_sale_invoice_id'     => ['sale_invoice_id'],
                'idx_sii_product_id'          => ['product_id'],
                'idx_sii_batch_number'        => ['batch_number'],
            ],
            'purchase_invoices' => [
                'idx_pur_inv_supplier_id'     => ['supplier_id'],
                'idx_pur_inv_posted_date'     => ['posted_date'],
                'idx_pur_inv_posted_number'   => ['posted_number'],
                'idx_pur_inv_invoice_number'  => ['invoice_number'],
                'idx_pur_inv_invoice_type'    => ['invoice_type'],
                'idx_pur_inv_due_date'        => ['due_date'],
                'idx_pur_inv_created_at'      => ['created_at'],
                'idx_pur_inv_sup_type_date'   => ['supplier_id', 'invoice_type', 'posted_date'],
            ],
            'purchase_invoice_items' => [
                'idx_pii_purchase_invoice_id' => ['purchase_invoice_id'],
                'idx_pii_product_id'          => ['product_id'],
                'idx_pii_po_item_id'          => ['purchase_order_item_id'],
                'idx_pii_batch'               => ['batch'],
            ],
            'sale_returns' => [
                'idx_sr_sale_invoice_id'      => ['sale_invoice_id'],
                'idx_sr_user_id'              => ['user_id'],
                'idx_sr_customer_id'          => ['customer_id'],
                'idx_sr_date'                 => ['date'],
                'idx_sr_settlement_mode'      => ['settlement_mode'],
            ],
            'sale_return_items' => [
                'idx_sri_sale_return_id'      => ['sale_return_id'],
                'idx_sri_product_id'          => ['product_id'],
            ],
            'purchase_returns' => [
                'idx_pr_supplier_id'          => ['supplier_id'],
                'idx_pr_purchase_invoice_id'  => ['purchase_invoice_id'],
                'idx_pr_date'                 => ['date'],
                'idx_pr_settlement_mode'      => ['settlement_mode'],
            ],
            'purchase_return_items' => [
                'idx_pri_purchase_return_id'  => ['purchase_return_id'],
                'idx_pri_product_id'          => ['product_id'],
            ],
            'stock_adjustments' => [
                'idx_sa_posted_date'          => ['posted_date'],
                'idx_sa_user_id'              => ['user_id'],
            ],
            'stock_adjustment_items' => [
                'idx_sai_stock_adjustment_id' => ['stock_adjustment_id'],
                'idx_sai_product_id'          => ['product_id'],
            ],
            'stock_disposals' => [
                'idx_sd_stock_adjustment_id'  => ['stock_adjustment_id'],
                'idx_sd_expense_id'           => ['expense_id'],
            ],
            'stock_disposal_items' => [
                'idx_sdi_stock_disposal_id'   => ['stock_disposal_id'],
                'idx_sdi_product_id'          => ['product_id'],
                'idx_sdi_batch_id'            => ['batch_id'],
            ],
            'purchase_orders' => [
                'idx_po_supplier_id'          => ['supplier_id'],
                'idx_po_date'                 => ['date'],
            ],
            'purchase_order_items' => [
                'idx_poi_product_id'          => ['product_id'],
            ],
            'payments' => [
                'idx_payments_created_at'     => ['created_at'],
                'idx_payments_party_status'   => ['party_type', 'party_id', 'status'],
            ],
            'payment_allocations' => [
                'idx_pa_payment_id'           => ['payment_id'],
                'idx_pa_invoice_lookup'       => ['invoice_type', 'invoice_id'],
            ],
            'personal_access_tokens' => [
                'idx_pat_created_at'          => ['created_at'],
            ],
            'journal_entry_lines' => [
                'idx_jel_journal_entry_id'    => ['journal_entry_id'],
                'idx_jel_party'               => ['party_type', 'party_id'],
            ],
            'customer_ledgers' => [
                'idx_cl_cust_type_date'       => ['customer_id', 'entry_type', 'entry_date'],
                'idx_cl_sale_invoice_id'      => ['sale_invoice_id'],
            ],
            'supplier_ledgers' => [
                'idx_sl_sup_type_date'        => ['supplier_id', 'entry_type', 'entry_date'],
                'idx_sl_purchase_invoice_id'  => ['purchase_invoice_id'],
            ],
            'customers' => [
                'idx_customers_name'          => ['name'],
                'idx_customers_phone'         => ['phone'],
            ],
            'suppliers' => [
                'idx_suppliers_name'          => ['name'],
                'idx_suppliers_phone'         => ['phone'],
            ],
        ];

        foreach ($indexSpecs as $table => $indexes) {
            if (!Schema::hasTable($table)) {
                continue;
            }

            $existingColumns = array_flip(Schema::getColumnListing($table));
            $existingIndexes = [];
            try {
                foreach (Schema::getIndexes($table) as $idx) {
                    if (!empty($idx['name'])) {
                        $existingIndexes[strtolower($idx['name'])] = true;
                    }
                }
            } catch (\Throwable) {
                // Fallback if driver does not support getIndexes()
            }

            foreach ($indexes as $indexName => $columns) {
                if (isset($existingIndexes[strtolower($indexName)])) {
                    continue;
                }

                $allColumnsExist = true;
                foreach ($columns as $col) {
                    if (!isset($existingColumns[$col])) {
                        $allColumnsExist = false;
                        break;
                    }
                }
                if (!$allColumnsExist) {
                    continue;
                }

                try {
                    Schema::table($table, function (Blueprint $blueprint) use ($columns, $indexName) {
                        $blueprint->index($columns, $indexName);
                    });
                } catch (\Throwable) {
                    // Ignore duplicate index errors
                }
            }
        }
    }

    public function down(): void
    {
        // Indexes can remain safely or be dropped with tables
    }
};


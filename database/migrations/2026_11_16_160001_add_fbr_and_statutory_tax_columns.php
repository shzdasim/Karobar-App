<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        if (Schema::hasTable('settings')) {
            Schema::table('settings', function (Blueprint $table) {
                if (!Schema::hasColumn('settings', 'fbr_enabled')) {
                    $table->boolean('fbr_enabled')->default(false);
                }
                if (!Schema::hasColumn('settings', 'fbr_environment')) {
                    $table->string('fbr_environment', 20)->default('sandbox');
                }
                if (!Schema::hasColumn('settings', 'fbr_mode')) {
                    $table->string('fbr_mode', 30)->default('digital_invoicing');
                }
                if (!Schema::hasColumn('settings', 'fbr_bearer_token')) {
                    $table->text('fbr_bearer_token')->nullable();
                }
                if (!Schema::hasColumn('settings', 'fbr_pos_id')) {
                    $table->string('fbr_pos_id', 50)->nullable();
                }
                if (!Schema::hasColumn('settings', 'fbr_seller_ntn')) {
                    $table->string('fbr_seller_ntn', 50)->nullable();
                }
                if (!Schema::hasColumn('settings', 'fbr_seller_strn')) {
                    $table->string('fbr_seller_strn', 50)->nullable();
                }
                if (!Schema::hasColumn('settings', 'fbr_seller_province')) {
                    $table->string('fbr_seller_province', 60)->default('Punjab');
                }
                if (!Schema::hasColumn('settings', 'fbr_sandbox_url')) {
                    $table->string('fbr_sandbox_url', 500)->nullable();
                }
                if (!Schema::hasColumn('settings', 'fbr_production_url')) {
                    $table->string('fbr_production_url', 500)->nullable();
                }
                if (!Schema::hasColumn('settings', 'fbr_default_hs_code')) {
                    $table->string('fbr_default_hs_code', 30)->default('3004.9099');
                }
                if (!Schema::hasColumn('settings', 'fbr_scenario_id')) {
                    $table->string('fbr_scenario_id', 30)->default('SN001');
                }
                if (!Schema::hasColumn('settings', 'fbr_pos_fee_enabled')) {
                    $table->boolean('fbr_pos_fee_enabled')->default(false);
                }
                if (!Schema::hasColumn('settings', 'fbr_pos_fee_amount')) {
                    $table->decimal('fbr_pos_fee_amount', 8, 2)->default(1.00);
                }
                if (!Schema::hasColumn('settings', 'fbr_auto_sync')) {
                    $table->boolean('fbr_auto_sync')->default(true);
                }
            });
        }

        if (Schema::hasTable('products')) {
            Schema::table('products', function (Blueprint $table) {
                if (!Schema::hasColumn('products', 'hs_code')) {
                    $table->string('hs_code', 30)->nullable();
                }
                if (!Schema::hasColumn('products', 'tax_type')) {
                    $table->string('tax_type', 30)->default('exempt');
                }
                if (!Schema::hasColumn('products', 'sales_tax_rate')) {
                    $table->decimal('sales_tax_rate', 5, 2)->default(0.00);
                }
                if (!Schema::hasColumn('products', 'further_tax_rate')) {
                    $table->decimal('further_tax_rate', 5, 2)->default(0.00);
                }
                if (!Schema::hasColumn('products', 'sro_schedule_no')) {
                    $table->string('sro_schedule_no', 80)->nullable();
                }
                if (!Schema::hasColumn('products', 'sro_item_serial_no')) {
                    $table->string('sro_item_serial_no', 40)->nullable();
                }
            });
        }

        if (Schema::hasTable('customers')) {
            Schema::table('customers', function (Blueprint $table) {
                if (!Schema::hasColumn('customers', 'strn')) {
                    $table->string('strn', 50)->nullable();
                }
                if (!Schema::hasColumn('customers', 'province')) {
                    $table->string('province', 60)->nullable();
                }
            });
        }

        if (Schema::hasTable('suppliers')) {
            Schema::table('suppliers', function (Blueprint $table) {
                if (!Schema::hasColumn('suppliers', 'strn')) {
                    $table->string('strn', 50)->nullable();
                }
                if (!Schema::hasColumn('suppliers', 'province')) {
                    $table->string('province', 60)->nullable();
                }
            });
        }

        if (Schema::hasTable('sale_invoices')) {
            Schema::table('sale_invoices', function (Blueprint $table) {
                if (!Schema::hasColumn('sale_invoices', 'fbr_invoice_number')) {
                    $table->string('fbr_invoice_number', 80)->nullable()->index();
                }
                if (!Schema::hasColumn('sale_invoices', 'fbr_usin')) {
                    $table->string('fbr_usin', 60)->nullable();
                }
                if (!Schema::hasColumn('sale_invoices', 'fbr_qr_data')) {
                    $table->text('fbr_qr_data')->nullable();
                }
                if (!Schema::hasColumn('sale_invoices', 'fbr_sync_status')) {
                    $table->string('fbr_sync_status', 25)->default('not_applicable')->index();
                }
                if (!Schema::hasColumn('sale_invoices', 'fbr_synced_at')) {
                    $table->timestamp('fbr_synced_at')->nullable();
                }
                if (!Schema::hasColumn('sale_invoices', 'fbr_error_message')) {
                    $table->text('fbr_error_message')->nullable();
                }
                if (!Schema::hasColumn('sale_invoices', 'fbr_response_payload')) {
                    $table->json('fbr_response_payload')->nullable();
                }
                if (!Schema::hasColumn('sale_invoices', 'fbr_pos_fee')) {
                    $table->decimal('fbr_pos_fee', 10, 2)->default(0.00);
                }
                if (!Schema::hasColumn('sale_invoices', 'further_tax_amount')) {
                    $table->decimal('further_tax_amount', 12, 2)->default(0.00);
                }
                if (!Schema::hasColumn('sale_invoices', 'advance_wht_percentage')) {
                    $table->decimal('advance_wht_percentage', 5, 2)->default(0.00);
                }
                if (!Schema::hasColumn('sale_invoices', 'advance_wht_amount')) {
                    $table->decimal('advance_wht_amount', 12, 2)->default(0.00);
                }
            });
        }

        if (Schema::hasTable('sale_invoice_items')) {
            Schema::table('sale_invoice_items', function (Blueprint $table) {
                if (!Schema::hasColumn('sale_invoice_items', 'hs_code')) {
                    $table->string('hs_code', 30)->nullable();
                }
                if (!Schema::hasColumn('sale_invoice_items', 'tax_percentage')) {
                    $table->decimal('tax_percentage', 5, 2)->default(0.00);
                }
                if (!Schema::hasColumn('sale_invoice_items', 'tax_amount')) {
                    $table->decimal('tax_amount', 12, 2)->default(0.00);
                }
                if (!Schema::hasColumn('sale_invoice_items', 'further_tax_amount')) {
                    $table->decimal('further_tax_amount', 12, 2)->default(0.00);
                }
                if (!Schema::hasColumn('sale_invoice_items', 'retail_mrp')) {
                    $table->decimal('retail_mrp', 12, 2)->nullable();
                }
            });
        }

        if (Schema::hasTable('sale_returns')) {
            Schema::table('sale_returns', function (Blueprint $table) {
                if (!Schema::hasColumn('sale_returns', 'fbr_invoice_number')) {
                    $table->string('fbr_invoice_number', 80)->nullable();
                }
                if (!Schema::hasColumn('sale_returns', 'fbr_usin')) {
                    $table->string('fbr_usin', 60)->nullable();
                }
                if (!Schema::hasColumn('sale_returns', 'fbr_qr_data')) {
                    $table->text('fbr_qr_data')->nullable();
                }
                if (!Schema::hasColumn('sale_returns', 'fbr_sync_status')) {
                    $table->string('fbr_sync_status', 25)->default('not_applicable');
                }
                if (!Schema::hasColumn('sale_returns', 'fbr_synced_at')) {
                    $table->timestamp('fbr_synced_at')->nullable();
                }
                if (!Schema::hasColumn('sale_returns', 'fbr_error_message')) {
                    $table->text('fbr_error_message')->nullable();
                }
            });
        }

        if (Schema::hasTable('payments')) {
            Schema::table('payments', function (Blueprint $table) {
                if (!Schema::hasColumn('payments', 'wht_tax_rate_id')) {
                    $table->unsignedBigInteger('wht_tax_rate_id')->nullable();
                }
                if (!Schema::hasColumn('payments', 'wht_percentage')) {
                    $table->decimal('wht_percentage', 5, 2)->default(0.00);
                }
                if (!Schema::hasColumn('payments', 'wht_amount')) {
                    $table->decimal('wht_amount', 12, 2)->default(0.00);
                }
            });
        }
    }

    public function down(): void
    {
        // Safe non-destructive down
    }
};

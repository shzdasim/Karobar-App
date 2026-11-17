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
                if (!Schema::hasColumn('settings', 'enable_dosage_stickers')) {
                    $table->boolean('enable_dosage_stickers')->default(true);
                }
                if (!Schema::hasColumn('settings', 'enable_prescription_attachments')) {
                    $table->boolean('enable_prescription_attachments')->default(true);
                }
                if (!Schema::hasColumn('settings', 'enable_drug_interaction_warnings')) {
                    $table->boolean('enable_drug_interaction_warnings')->default(true);
                }
                if (!Schema::hasColumn('settings', 'enable_cold_chain_log')) {
                    $table->boolean('enable_cold_chain_log')->default(true);
                }
            });
        }

        if (Schema::hasTable('products')) {
            Schema::table('products', function (Blueprint $table) {
                if (!Schema::hasColumn('products', 'drug_schedule')) {
                    $table->string('drug_schedule', 32)->nullable()->default('otc');
                }
                if (!Schema::hasColumn('products', 'requires_cold_chain')) {
                    $table->boolean('requires_cold_chain')->default(false);
                }
                if (!Schema::hasColumn('products', 'default_dosage')) {
                    $table->string('default_dosage', 255)->nullable();
                }
            });
        }

        if (Schema::hasTable('sale_invoices')) {
            Schema::table('sale_invoices', function (Blueprint $table) {
                if (!Schema::hasColumn('sale_invoices', 'prescription_image_path')) {
                    $table->string('prescription_image_path', 500)->nullable();
                }
            });
        }

        if (Schema::hasTable('sale_invoice_items')) {
            Schema::table('sale_invoice_items', function (Blueprint $table) {
                if (!Schema::hasColumn('sale_invoice_items', 'dosage_instruction')) {
                    $table->string('dosage_instruction', 255)->nullable();
                }
            });
        }

        if (!Schema::hasTable('cold_chain_logs')) {
            Schema::create('cold_chain_logs', function (Blueprint $table) {
                $table->id();
                $table->unsignedBigInteger('storage_location_id')->nullable()->index();
                $table->unsignedBigInteger('recorded_by_id')->nullable()->index();
                $table->timestamp('recorded_at')->useCurrent()->index();
                $table->string('shift', 32)->default('morning');
                $table->decimal('temperature_c', 5, 2);
                $table->decimal('humidity_pct', 5, 2)->nullable();
                $table->string('status', 32)->default('normal'); // normal | excursion
                $table->string('corrective_action', 500)->nullable();
                $table->string('notes', 500)->nullable();
                $table->timestamps();
            });
        }
    }

    public function down(): void
    {
        Schema::dropIfExists('cold_chain_logs');

        if (Schema::hasTable('sale_invoice_items')) {
            Schema::table('sale_invoice_items', function (Blueprint $table) {
                if (Schema::hasColumn('sale_invoice_items', 'dosage_instruction')) {
                    $table->dropColumn('dosage_instruction');
                }
            });
        }

        if (Schema::hasTable('sale_invoices')) {
            Schema::table('sale_invoices', function (Blueprint $table) {
                if (Schema::hasColumn('sale_invoices', 'prescription_image_path')) {
                    $table->dropColumn('prescription_image_path');
                }
            });
        }

        if (Schema::hasTable('products')) {
            Schema::table('products', function (Blueprint $table) {
                $cols = array_filter(
                    ['drug_schedule', 'requires_cold_chain', 'default_dosage'],
                    fn ($c) => Schema::hasColumn('products', $c)
                );
                if (!empty($cols)) {
                    $table->dropColumn($cols);
                }
            });
        }

        if (Schema::hasTable('settings')) {
            Schema::table('settings', function (Blueprint $table) {
                $cols = array_filter([
                    'enable_dosage_stickers',
                    'enable_prescription_attachments',
                    'enable_drug_interaction_warnings',
                    'enable_cold_chain_log',
                ], fn ($c) => Schema::hasColumn('settings', $c));
                if (!empty($cols)) {
                    $table->dropColumn($cols);
                }
            });
        }
    }
};

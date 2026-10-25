<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    /**
     * Run the migrations.
     */
    public function up(): void
    {
        if (Schema::hasTable('sale_returns')) {
            Schema::table('sale_returns', function (Blueprint $table) {
                if (!Schema::hasColumn('sale_returns', 'settlement_mode')) {
                    $table->string('settlement_mode', 30)->default('cash_refund')->after('total');
                }
                if (!Schema::hasColumn('sale_returns', 'payment_method')) {
                    $table->string('payment_method', 50)->default('cash')->after('settlement_mode');
                }
                if (!Schema::hasColumn('sale_returns', 'account_id')) {
                    $table->unsignedBigInteger('account_id')->nullable()->after('payment_method');
                }
                if (!Schema::hasColumn('sale_returns', 'credited_amount')) {
                    $table->decimal('credited_amount', 15, 2)->default(0)->after('account_id');
                }
            });
        }

        if (Schema::hasTable('purchase_returns')) {
            Schema::table('purchase_returns', function (Blueprint $table) {
                if (!Schema::hasColumn('purchase_returns', 'settlement_mode')) {
                    $table->string('settlement_mode', 30)->default('cash_refund')->after('total');
                }
                if (!Schema::hasColumn('purchase_returns', 'payment_method')) {
                    $table->string('payment_method', 50)->default('cash')->after('settlement_mode');
                }
                if (!Schema::hasColumn('purchase_returns', 'account_id')) {
                    $table->unsignedBigInteger('account_id')->nullable()->after('payment_method');
                }
                if (!Schema::hasColumn('purchase_returns', 'credited_amount')) {
                    $table->decimal('credited_amount', 15, 2)->default(0)->after('account_id');
                }
            });
        }

        if (Schema::hasTable('customer_ledgers') && !Schema::hasColumn('customer_ledgers', 'sale_return_id')) {
            Schema::table('customer_ledgers', function (Blueprint $table) {
                $table->unsignedBigInteger('sale_return_id')->nullable()->after('sale_invoice_id')->index();
            });
        }

        if (Schema::hasTable('supplier_ledgers') && !Schema::hasColumn('supplier_ledgers', 'purchase_return_id')) {
            Schema::table('supplier_ledgers', function (Blueprint $table) {
                $table->unsignedBigInteger('purchase_return_id')->nullable()->after('purchase_invoice_id')->index();
            });
        }
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        if (Schema::hasTable('customer_ledgers') && Schema::hasColumn('customer_ledgers', 'sale_return_id')) {
            Schema::table('customer_ledgers', function (Blueprint $table) {
                $table->dropColumn('sale_return_id');
            });
        }

        if (Schema::hasTable('supplier_ledgers') && Schema::hasColumn('supplier_ledgers', 'purchase_return_id')) {
            Schema::table('supplier_ledgers', function (Blueprint $table) {
                $table->dropColumn('purchase_return_id');
            });
        }

        if (Schema::hasTable('sale_returns')) {
            Schema::table('sale_returns', function (Blueprint $table) {
                $cols = [];
                foreach (['settlement_mode', 'credited_amount'] as $col) {
                    if (Schema::hasColumn('sale_returns', $col)) {
                        $cols[] = $col;
                    }
                }
                if (!empty($cols)) {
                    $table->dropColumn($cols);
                }
            });
        }

        if (Schema::hasTable('purchase_returns')) {
            Schema::table('purchase_returns', function (Blueprint $table) {
                $cols = [];
                foreach (['settlement_mode', 'credited_amount'] as $col) {
                    if (Schema::hasColumn('purchase_returns', $col)) {
                        $cols[] = $col;
                    }
                }
                if (!empty($cols)) {
                    $table->dropColumn($cols);
                }
            });
        }
    }
};


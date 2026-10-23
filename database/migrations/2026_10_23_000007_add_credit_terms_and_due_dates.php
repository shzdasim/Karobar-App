<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        // 1. Customers: credit_limit, credit_days, credit_status
        Schema::table('customers', function (Blueprint $table) {
            if (!Schema::hasColumn('customers', 'credit_limit')) {
                $table->decimal('credit_limit', 15, 2)->nullable()->after('address');
            }
            if (!Schema::hasColumn('customers', 'credit_days')) {
                $table->integer('credit_days')->nullable()->default(30)->after('credit_limit');
            }
            if (!Schema::hasColumn('customers', 'credit_status')) {
                $table->string('credit_status', 20)->default('active')->after('credit_days'); // 'active', 'on_hold', 'blocked'
            }
        });

        // 2. Suppliers: credit_limit, credit_days
        Schema::table('suppliers', function (Blueprint $table) {
            if (!Schema::hasColumn('suppliers', 'credit_limit')) {
                $table->decimal('credit_limit', 15, 2)->nullable()->after('phone');
            }
            if (!Schema::hasColumn('suppliers', 'credit_days')) {
                $table->integer('credit_days')->nullable()->default(30)->after('credit_limit');
            }
        });

        // 3. Sale Invoices: due_date
        Schema::table('sale_invoices', function (Blueprint $table) {
            if (!Schema::hasColumn('sale_invoices', 'due_date')) {
                $table->date('due_date')->nullable()->after('date');
            }
        });

        // 4. Purchase Invoices: due_date
        Schema::table('purchase_invoices', function (Blueprint $table) {
            if (!Schema::hasColumn('purchase_invoices', 'due_date')) {
                $table->date('due_date')->nullable()->after('posted_date');
            }
        });

        // 5. Customer Ledgers: due_date
        if (Schema::hasTable('customer_ledgers')) {
            Schema::table('customer_ledgers', function (Blueprint $table) {
                if (!Schema::hasColumn('customer_ledgers', 'due_date')) {
                    $table->date('due_date')->nullable()->after('entry_date');
                }
            });
        }

        // 6. Supplier Ledgers: due_date
        if (Schema::hasTable('supplier_ledgers')) {
            Schema::table('supplier_ledgers', function (Blueprint $table) {
                if (!Schema::hasColumn('supplier_ledgers', 'due_date')) {
                    $table->date('due_date')->nullable()->after('entry_date');
                }
            });
        }
    }

    public function down(): void
    {
        Schema::table('customers', function (Blueprint $table) {
            $table->dropColumn(['credit_limit', 'credit_days', 'credit_status']);
        });

        Schema::table('suppliers', function (Blueprint $table) {
            $table->dropColumn(['credit_limit', 'credit_days']);
        });

        Schema::table('sale_invoices', function (Blueprint $table) {
            $table->dropColumn(['due_date']);
        });

        Schema::table('purchase_invoices', function (Blueprint $table) {
            $table->dropColumn(['due_date']);
        });

        if (Schema::hasTable('customer_ledgers')) {
            Schema::table('customer_ledgers', function (Blueprint $table) {
                $table->dropColumn(['due_date']);
            });
        }

        if (Schema::hasTable('supplier_ledgers')) {
            Schema::table('supplier_ledgers', function (Blueprint $table) {
                $table->dropColumn(['due_date']);
            });
        }
    }
};


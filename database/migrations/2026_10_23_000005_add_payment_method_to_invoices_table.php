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
        if (Schema::hasTable('sale_invoices') && !Schema::hasColumn('sale_invoices', 'payment_method')) {
            Schema::table('sale_invoices', function (Blueprint $table) {
                $table->string('payment_method', 50)->default('cash')->after('invoice_type');
            });
        }

        if (Schema::hasTable('purchase_invoices') && !Schema::hasColumn('purchase_invoices', 'payment_method')) {
            Schema::table('purchase_invoices', function (Blueprint $table) {
                $table->string('payment_method', 50)->default('cash')->after('invoice_type');
            });
        }
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        if (Schema::hasTable('sale_invoices') && Schema::hasColumn('sale_invoices', 'payment_method')) {
            Schema::table('sale_invoices', function (Blueprint $table) {
                $table->dropColumn('payment_method');
            });
        }

        if (Schema::hasTable('purchase_invoices') && Schema::hasColumn('purchase_invoices', 'payment_method')) {
            Schema::table('purchase_invoices', function (Blueprint $table) {
                $table->dropColumn('payment_method');
            });
        }
    }
};


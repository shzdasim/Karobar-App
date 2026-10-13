<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::table('purchase_invoices', function (Blueprint $table) {
            // Keep an index supporting the foreign key before dropping uniqueness.
            $table->index('purchase_order_id');
            $table->uuid('receipt_key')->nullable()->unique();
        });
        Schema::table('purchase_invoices', function (Blueprint $table) {
            $table->dropUnique(['purchase_order_id']);
        });
    }

    public function down(): void
    {
        if (DB::table('purchase_invoices')->whereNotNull('purchase_order_id')
            ->groupBy('purchase_order_id')->havingRaw('COUNT(*) > 1')->exists()) {
            throw new RuntimeException('Cannot roll back: purchase orders have multiple receipts.');
        }
        Schema::table('purchase_invoices', function (Blueprint $table) {
            $table->unique('purchase_order_id');
        });
        Schema::table('purchase_invoices', function (Blueprint $table) {
            $table->dropIndex(['purchase_order_id']);
            $table->dropUnique(['receipt_key']);
            $table->dropColumn('receipt_key');
        });
    }
};

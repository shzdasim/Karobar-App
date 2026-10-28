<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::table('sale_invoice_items', function (Blueprint $table) {
            if (!Schema::hasColumn('sale_invoice_items', 'pack_quantity')) {
                $table->integer('pack_quantity')->default(0)->nullable()->after('current_quantity');
            }
            if (!Schema::hasColumn('sale_invoice_items', 'unit_quantity')) {
                $table->integer('unit_quantity')->default(0)->nullable()->after('pack_quantity');
            }
            if (!Schema::hasColumn('sale_invoice_items', 'bonus')) {
                $table->integer('bonus')->default(0)->nullable()->after('quantity');
            }
            if (!Schema::hasColumn('sale_invoice_items', 'pack_price')) {
                $table->decimal('pack_price', 15, 2)->default(0)->nullable()->after('bonus');
            }
        });
    }

    public function down(): void
    {
        Schema::table('sale_invoice_items', function (Blueprint $table) {
            $cols = array_filter(
                ['pack_quantity', 'unit_quantity', 'bonus', 'pack_price'],
                fn ($col) => Schema::hasColumn('sale_invoice_items', $col)
            );
            if (!empty($cols)) {
                $table->dropColumn($cols);
            }
        });
    }
};


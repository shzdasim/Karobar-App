<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::table('sale_invoice_items', function (Blueprint $table) {
            if (!Schema::hasColumn('sale_invoice_items', 'pack_bonus')) {
                $table->integer('pack_bonus')->default(0)->nullable()->after('quantity');
            }
            if (!Schema::hasColumn('sale_invoice_items', 'unit_bonus')) {
                $table->integer('unit_bonus')->default(0)->nullable()->after('pack_bonus');
            }
        });
    }

    public function down(): void
    {
        Schema::table('sale_invoice_items', function (Blueprint $table) {
            $cols = array_filter(
                ['pack_bonus', 'unit_bonus'],
                fn ($col) => Schema::hasColumn('sale_invoice_items', $col)
            );
            if (!empty($cols)) {
                $table->dropColumn($cols);
            }
        });
    }
};


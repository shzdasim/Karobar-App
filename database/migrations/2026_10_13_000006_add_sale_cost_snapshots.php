<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        foreach (['sale_invoice_items', 'sale_return_items'] as $name) {
            Schema::table($name, function (Blueprint $table) {
                // NULL retains the existing live-cost calculation for legacy records.
                $table->decimal('cost_price_snapshot', 24, 8)->nullable();
            });
        }
    }

    public function down(): void
    {
        foreach (['sale_return_items', 'sale_invoice_items'] as $name) {
            Schema::table($name, fn (Blueprint $table) => $table->dropColumn('cost_price_snapshot'));
        }
    }
};

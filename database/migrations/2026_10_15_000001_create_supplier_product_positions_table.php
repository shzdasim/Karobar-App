<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    /**
     * Remembers the product line order a user saved on a Purchase Order based
     * Purchase Invoice, per supplier, so later orders/receipts reuse it.
     */
    public function up(): void
    {
        if (Schema::hasTable('supplier_product_positions')) return; // created by an earlier, reverted build

        Schema::create('supplier_product_positions', function (Blueprint $table) {
            $table->id();
            $table->foreignId('supplier_id')->constrained()->cascadeOnDelete();
            $table->foreignId('product_id')->constrained()->cascadeOnDelete();
            $table->unsignedInteger('position');
            $table->timestamps();
            $table->unique(['supplier_id', 'product_id']);
            $table->index(['supplier_id', 'position']);
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('supplier_product_positions');
    }
};

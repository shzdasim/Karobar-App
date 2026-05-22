<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration {
    public function up(): void
    {
        Schema::create('quotation_items', function (Blueprint $table) {
            $table->id();
            $table->foreignId('quotation_id')->constrained('quotations')->cascadeOnDelete();

            $table->enum('line_type', ['product', 'manual'])->default('product');
            $table->foreignId('product_id')->nullable()->constrained('products')->nullOnDelete();
            $table->string('manual_name')->nullable();

            $table->integer('quantity');
            $table->decimal('price', 15, 2);
            $table->decimal('item_discount_percentage', 15, 2)->default(0);
            $table->decimal('sub_total', 15, 2)->default(0);

            $table->timestamps();
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('quotation_items');
    }
};


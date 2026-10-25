<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::create('stocktake_items', function (Blueprint $table) {
            $table->id();
            $table->foreignId('stocktake_session_id')
                  ->constrained('stocktake_sessions')
                  ->cascadeOnDelete();
            $table->foreignId('product_id')
                  ->constrained('products')
                  ->restrictOnDelete();
            $table->string('batch_number')->nullable();
            $table->date('expiry_date')->nullable();
            // Frozen snapshot at session start — NEVER shown in blind mode
            $table->decimal('system_qty', 18, 3)->default(0);
            // null until counted
            $table->decimal('counted_qty', 18, 3)->nullable();
            // counted - system
            $table->decimal('variance_qty', 18, 3)->default(0);
            // abs(variance_qty) * unit_purchase_price
            $table->decimal('variance_value', 18, 2)->default(0);
            $table->decimal('unit_purchase_price', 18, 4)->default(0);
            $table->enum('status', ['pending', 'counted'])->default('pending');
            $table->text('discrepancy_reason')->nullable();
            $table->foreignId('counted_by')->nullable()->constrained('users')->nullOnDelete();
            $table->timestamp('counted_at')->nullable();
            $table->timestamps();

            $table->index('stocktake_session_id');
            $table->index(['product_id', 'batch_number']);
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('stocktake_items');
    }
};

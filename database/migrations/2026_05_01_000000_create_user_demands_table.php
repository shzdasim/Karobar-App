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
        Schema::create('user_demands', function (Blueprint $table) {
            $table->id();
            // The product (existing or newly created to satisfy the demand)
            $table->unsignedBigInteger('product_id');
            // Who requested it (nullable if not tied to an authenticated user)
            $table->unsignedBigInteger('requested_by')->nullable();
            // Free-text name captured at request time (for context)
            $table->string('requested_name');
            $table->bigInteger('requested_quantity')->default(1);
            $table->text('notes')->nullable();

            // Status lifecycle
            $table->enum('status', ['pending', 'ordered', 'fulfilled', 'cancelled'])
                  ->default('pending');

            // When the daily reminder last notified about this demand
            $table->timestamp('last_notified_at')->nullable();

            $table->timestamps();

            $table->foreign('product_id')->references('id')->on('products')->onDelete('cascade');
            $table->foreign('requested_by')->references('id')->on('users')->onDelete('set null');

            $table->index(['status']);
            $table->index(['product_id']);
        });
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::dropIfExists('user_demands');
    }
};

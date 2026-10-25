<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::create('stocktake_sessions', function (Blueprint $table) {
            $table->id();
            $table->string('reference_no')->unique();
            $table->enum('session_type', ['full', 'cycle'])->default('full');
            $table->boolean('is_blind')->default(true);
            $table->enum('status', ['draft', 'in_progress', 'pending_review', 'finalized', 'cancelled'])->default('draft');
            $table->string('scope_type')->nullable();  // 'category', 'brand', 'supplier' (null for full)
            $table->unsignedBigInteger('scope_id')->nullable(); // FK to respective table (no formal FK)
            $table->text('notes')->nullable();
            $table->foreignId('user_id')->nullable()->constrained('users')->nullOnDelete();
            $table->foreignId('stock_adjustment_id')->nullable()->constrained('stock_adjustments')->nullOnDelete();
            $table->timestamp('started_at')->nullable();
            $table->timestamp('completed_at')->nullable();
            $table->timestamps();
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('stocktake_sessions');
    }
};

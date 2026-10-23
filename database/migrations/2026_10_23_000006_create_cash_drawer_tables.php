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
        if (!Schema::hasTable('cash_drawer_sessions')) {
            Schema::create('cash_drawer_sessions', function (Blueprint $table) {
                $table->id();
                $table->foreignId('user_id')->constrained('users')->cascadeOnDelete();
                $table->dateTime('opened_at')->index();
                $table->dateTime('closed_at')->nullable()->index();
                $table->enum('status', ['open', 'closed'])->default('open')->index();
                $table->decimal('opening_balance', 15, 2)->default(0);
                $table->decimal('closing_balance_expected', 15, 2)->default(0);
                $table->decimal('closing_balance_actual', 15, 2)->nullable();
                $table->decimal('difference', 15, 2)->default(0);
                $table->text('notes')->nullable();
                $table->timestamps();
            });
        }

        if (!Schema::hasTable('cash_drawer_transactions')) {
            Schema::create('cash_drawer_transactions', function (Blueprint $table) {
                $table->id();
                $table->foreignId('session_id')->constrained('cash_drawer_sessions')->cascadeOnDelete();
                $table->foreignId('user_id')->nullable()->constrained('users')->nullOnDelete();
                $table->string('type', 50)->index(); // cash_sale, cash_return, cash_purchase, customer_payment, supplier_payment, cash_in, cash_out
                $table->decimal('amount', 15, 2);
                $table->string('reference_type', 100)->nullable();
                $table->unsignedBigInteger('reference_id')->nullable()->index();
                $table->string('notes', 255)->nullable();
                $table->timestamps();

                $table->index(['session_id', 'type']);
            });
        }
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::dropIfExists('cash_drawer_transactions');
        Schema::dropIfExists('cash_drawer_sessions');
    }
};


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
        Schema::create('bank_ledgers', function (Blueprint $table) {
            $table->id();

            $table->foreignId('bank_id')->constrained('banks')->cascadeOnDelete();

            $table->date('entry_date');

            // Example types: customer_payment, supplier_payment, manual
            $table->string('entry_type');

            // Optionally reference originating document
            $table->string('ref_type')->nullable();
            $table->unsignedBigInteger('ref_id')->nullable();

            // Relative to bank balance
            $table->enum('direction', ['credit', 'debit']);

            $table->decimal('amount', 15, 2);
            $table->string('description')->nullable();

            $table->timestamps();

            $table->index(['bank_id', 'entry_date']);
        });
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::dropIfExists('bank_ledgers');
    }
};

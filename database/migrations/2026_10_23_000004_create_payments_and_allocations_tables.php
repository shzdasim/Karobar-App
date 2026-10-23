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
        if (!Schema::hasTable('payments')) {
            Schema::create('payments', function (Blueprint $table) {
                $table->id();
                $table->string('payment_number', 50)->unique();
                $table->enum('party_type', ['customer', 'supplier'])->index();
                $table->unsignedBigInteger('party_id')->index();
                $table->date('payment_date')->index();
                $table->decimal('amount', 15, 2);
                $table->decimal('allocated_amount', 15, 2)->default(0);
                $table->decimal('unapplied_amount', 15, 2)->default(0);
                $table->string('payment_method', 50)->default('cash');
                $table->string('reference', 100)->nullable();
                $table->string('status', 20)->default('posted')->index();
                $table->text('notes')->nullable();
                $table->string('idempotency_key', 100)->nullable()->unique();
                
                $table->unsignedBigInteger('created_by_id')->nullable()->index();
                $table->timestamp('reversed_at')->nullable();
                $table->unsignedBigInteger('reversed_by_id')->nullable()->index();
                $table->text('reversal_reason')->nullable();

                $table->timestamps();

                $table->index(['party_type', 'party_id']);
                $table->index(['party_type', 'party_id', 'payment_date']);
            });
        }

        if (!Schema::hasTable('payment_allocations')) {
            Schema::create('payment_allocations', function (Blueprint $table) {
                $table->id();
                $table->foreignId('payment_id')->constrained('payments')->cascadeOnDelete();
                $table->enum('invoice_type', ['sale_invoice', 'purchase_invoice'])->index();
                $table->unsignedBigInteger('invoice_id')->index();
                $table->decimal('amount', 15, 2);
                $table->timestamps();

                $table->index(['invoice_type', 'invoice_id']);
            });
        }

        // Add payment_id link to customer_ledgers if missing
        if (Schema::hasTable('customer_ledgers') && !Schema::hasColumn('customer_ledgers', 'payment_id')) {
            Schema::table('customer_ledgers', function (Blueprint $table) {
                $table->unsignedBigInteger('payment_id')->nullable()->index()->after('sale_invoice_id');
            });
        }

        // Add payment_id link to supplier_ledgers if missing
        if (Schema::hasTable('supplier_ledgers') && !Schema::hasColumn('supplier_ledgers', 'payment_id')) {
            Schema::table('supplier_ledgers', function (Blueprint $table) {
                $table->unsignedBigInteger('payment_id')->nullable()->index()->after('purchase_invoice_id');
            });
        }
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        if (Schema::hasTable('customer_ledgers') && Schema::hasColumn('customer_ledgers', 'payment_id')) {
            Schema::table('customer_ledgers', function (Blueprint $table) {
                $table->dropColumn('payment_id');
            });
        }

        if (Schema::hasTable('supplier_ledgers') && Schema::hasColumn('supplier_ledgers', 'payment_id')) {
            Schema::table('supplier_ledgers', function (Blueprint $table) {
                $table->dropColumn('payment_id');
            });
        }

        Schema::dropIfExists('payment_allocations');
        Schema::dropIfExists('payments');
    }
};


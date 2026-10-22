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
        // 1. Audit trail table for all posted business document changes
        Schema::create('document_histories', function (Blueprint $table) {
            $table->id();
            $table->foreignId('user_id')->nullable()->constrained()->nullOnDelete();
            $table->string('entity_type', 120);
            $table->unsignedBigInteger('entity_id');
            $table->string('action', 30); // created, updated, reversed
            $table->text('reason')->nullable();
            $table->json('before_payload')->nullable();
            $table->json('after_payload')->nullable();
            $table->json('diff')->nullable();
            $table->timestamps();

            $table->index(['entity_type', 'entity_id']);
        });

        // 2. Add reversal status & metadata to sale_invoices
        Schema::table('sale_invoices', function (Blueprint $table) {
            $table->string('status', 20)->default('posted')->after('posted_number');
            $table->timestamp('reversed_at')->nullable()->after('status');
            $table->foreignId('reversed_by_id')->nullable()->after('reversed_at')->constrained('users')->nullOnDelete();
            $table->text('reversal_reason')->nullable()->after('reversed_by_id');
            $table->index('status');
        });

        // 3. Add reversal status & metadata to purchase_invoices
        Schema::table('purchase_invoices', function (Blueprint $table) {
            $table->string('status', 20)->default('posted')->after('posted_number');
            $table->timestamp('reversed_at')->nullable()->after('status');
            $table->foreignId('reversed_by_id')->nullable()->after('reversed_at')->constrained('users')->nullOnDelete();
            $table->text('reversal_reason')->nullable()->after('reversed_by_id');
            $table->index('status');
        });
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::table('purchase_invoices', function (Blueprint $table) {
            $table->dropForeign(['reversed_by_id']);
            $table->dropColumn(['status', 'reversed_at', 'reversed_by_id', 'reversal_reason']);
        });

        Schema::table('sale_invoices', function (Blueprint $table) {
            $table->dropForeign(['reversed_by_id']);
            $table->dropColumn(['status', 'reversed_at', 'reversed_by_id', 'reversal_reason']);
        });

        Schema::dropIfExists('document_histories');
    }
};


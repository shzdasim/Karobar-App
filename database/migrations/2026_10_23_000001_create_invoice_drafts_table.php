<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::create('invoice_drafts', function (Blueprint $table) {
            $table->id();
            $table->uuid('uuid')->unique();
            $table->foreignId('user_id')->constrained()->cascadeOnDelete();
            $table->string('type', 32)->index(); // 'sale_retail', 'sale_wholesale', 'purchase'
            $table->string('title')->nullable();
            $table->string('party_name')->nullable();
            $table->decimal('total_amount', 15, 2)->default(0);
            $table->unsignedInteger('items_count')->default(0);
            $table->unsignedInteger('version')->default(1);
            $table->json('payload');
            $table->string('status', 32)->default('draft')->index(); // 'draft', 'posted', 'discarded'
            $table->unsignedBigInteger('posted_invoice_id')->nullable()->index();
            $table->string('posted_invoice_type')->nullable();
            $table->timestamps();
            $table->softDeletes();

            $table->index(['user_id', 'type', 'status']);
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('invoice_drafts');
    }
};


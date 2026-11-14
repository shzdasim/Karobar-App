<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::create('trade_schemes', function (Blueprint $table) {
            $table->id();
            $table->string('name');
            $table->string('scheme_type')->default('ratio'); // 'ratio' (every X get Y) or 'slab' (bracket tiers)
            $table->string('target_type')->default('product'); // 'product', 'brand', 'category'
            $table->unsignedBigInteger('target_id');
            $table->string('bonus_type')->default('pack'); // 'pack' or 'unit'
            $table->unsignedBigInteger('customer_id')->nullable();
            $table->string('sale_type')->default('all'); // 'all', 'wholesale', 'retail'
            $table->date('start_date')->nullable();
            $table->date('end_date')->nullable();
            $table->boolean('is_active')->default(true);
            $table->integer('priority')->default(0);
            $table->text('notes')->nullable();
            $table->timestamps();

            $table->foreign('customer_id')->references('id')->on('customers')->onDelete('cascade');
            $table->index(['is_active', 'target_type', 'target_id']);
        });

        Schema::create('trade_scheme_slabs', function (Blueprint $table) {
            $table->id();
            $table->unsignedBigInteger('trade_scheme_id');
            $table->decimal('min_qty', 10, 2)->default(0);
            $table->decimal('max_qty', 10, 2)->nullable();
            $table->decimal('bonus_qty', 10, 2)->default(0);
            $table->unsignedBigInteger('bonus_product_id')->nullable();
            $table->timestamps();

            $table->foreign('trade_scheme_id')->references('id')->on('trade_schemes')->onDelete('cascade');
            $table->foreign('bonus_product_id')->references('id')->on('products')->onDelete('set null');
            $table->index(['trade_scheme_id', 'min_qty']);
        });

        if (!Schema::hasColumn('customers', 'eligible_for_schemes')) {
            Schema::table('customers', function (Blueprint $table) {
                $table->boolean('eligible_for_schemes')->default(true)->after('is_regular');
            });
        }

        Schema::table('sale_invoice_items', function (Blueprint $table) {
            if (!Schema::hasColumn('sale_invoice_items', 'applied_scheme_id')) {
                $table->unsignedBigInteger('applied_scheme_id')->nullable()->after('bonus');
                $table->foreign('applied_scheme_id')->references('id')->on('trade_schemes')->onDelete('set null');
            }
            if (!Schema::hasColumn('sale_invoice_items', 'applied_scheme_name')) {
                $table->string('applied_scheme_name')->nullable()->after('applied_scheme_id');
            }
        });
    }

    public function down(): void
    {
        Schema::table('sale_invoice_items', function (Blueprint $table) {
            if (Schema::hasColumn('sale_invoice_items', 'applied_scheme_id')) {
                $table->dropForeign(['applied_scheme_id']);
                $table->dropColumn('applied_scheme_id');
            }
            if (Schema::hasColumn('sale_invoice_items', 'applied_scheme_name')) {
                $table->dropColumn('applied_scheme_name');
            }
        });

        if (Schema::hasColumn('customers', 'eligible_for_schemes')) {
            Schema::table('customers', function (Blueprint $table) {
                $table->dropColumn('eligible_for_schemes');
            });
        }

        Schema::dropIfExists('trade_scheme_slabs');
        Schema::dropIfExists('trade_schemes');
    }
};

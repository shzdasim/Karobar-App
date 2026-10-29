<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        if (Schema::hasTable('batches')) {
            Schema::table('batches', function (Blueprint $table) {
                if (!Schema::hasColumn('batches', 'pack_purchase_price')) {
                    $table->decimal('pack_purchase_price', 15, 2)->nullable()->after('quantity');
                }
                if (!Schema::hasColumn('batches', 'unit_purchase_price')) {
                    $table->decimal('unit_purchase_price', 15, 2)->nullable()->after('pack_purchase_price');
                }
                if (!Schema::hasColumn('batches', 'pack_sale_price')) {
                    $table->decimal('pack_sale_price', 15, 2)->nullable()->after('unit_purchase_price');
                }
                if (!Schema::hasColumn('batches', 'unit_sale_price')) {
                    $table->decimal('unit_sale_price', 15, 2)->nullable()->after('pack_sale_price');
                }
                if (!Schema::hasColumn('batches', 'whole_sale_pack_price')) {
                    $table->decimal('whole_sale_pack_price', 15, 2)->nullable()->after('unit_sale_price');
                }
                if (!Schema::hasColumn('batches', 'whole_sale_unit_price')) {
                    $table->decimal('whole_sale_unit_price', 15, 2)->nullable()->after('whole_sale_pack_price');
                }
            });

            // Backfill existing batches with their parent product's current prices so existing batches retain their MRP/cost
            if (Schema::hasTable('products')) {
                $products = DB::table('products')->select([
                    'id',
                    'pack_purchase_price',
                    'unit_purchase_price',
                    'pack_sale_price',
                    'unit_sale_price',
                    'whole_sale_pack_price',
                    'whole_sale_unit_price',
                ])->get();

                foreach ($products as $p) {
                    DB::table('batches')
                        ->where('product_id', $p->id)
                        ->whereNull('unit_sale_price')
                        ->update([
                            'pack_purchase_price'   => $p->pack_purchase_price,
                            'unit_purchase_price'   => $p->unit_purchase_price,
                            'pack_sale_price'       => $p->pack_sale_price,
                            'unit_sale_price'       => $p->unit_sale_price,
                            'whole_sale_pack_price' => $p->whole_sale_pack_price,
                            'whole_sale_unit_price' => $p->whole_sale_unit_price,
                        ]);
                }
            }
        }

        if (!Schema::hasTable('product_suppliers')) {
            Schema::create('product_suppliers', function (Blueprint $table) {
                $table->id();
                $table->foreignId('product_id')->constrained('products')->cascadeOnDelete();
                $table->foreignId('supplier_id')->constrained('suppliers')->cascadeOnDelete();
                $table->string('supplier_sku', 100)->nullable();
                $table->decimal('last_purchase_price', 15, 2)->nullable();
                $table->integer('lead_time_days')->nullable();
                $table->integer('min_order_qty')->default(1);
                $table->boolean('is_preferred')->default(false);
                $table->string('notes', 255)->nullable();
                $table->timestamps();

                $table->unique(['product_id', 'supplier_id']);
            });

            // Backfill existing primary supplier mappings from products table
            if (Schema::hasTable('products')) {
                $now = now();
                $productsWithSuppliers = DB::table('products')
                    ->whereNotNull('supplier_id')
                    ->select(['id', 'supplier_id', 'unit_purchase_price'])
                    ->get();

                foreach ($productsWithSuppliers as $p) {
                    DB::table('product_suppliers')->insertOrIgnore([
                        'product_id'          => $p->id,
                        'supplier_id'         => $p->supplier_id,
                        'last_purchase_price' => $p->unit_purchase_price,
                        'min_order_qty'       => 1,
                        'is_preferred'        => true,
                        'created_at'          => $now,
                        'updated_at'          => $now,
                    ]);
                }
            }
        }
    }

    public function down(): void
    {
        Schema::dropIfExists('product_suppliers');

        if (Schema::hasTable('batches')) {
            Schema::table('batches', function (Blueprint $table) {
                $cols = array_filter([
                    'pack_purchase_price',
                    'unit_purchase_price',
                    'pack_sale_price',
                    'unit_sale_price',
                    'whole_sale_pack_price',
                    'whole_sale_unit_price',
                ], fn ($col) => Schema::hasColumn('batches', $col));

                if (!empty($cols)) {
                    $table->dropColumn($cols);
                }
            });
        }
    }
};


<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        if (!Schema::hasTable('storage_locations')) {
            Schema::create('storage_locations', function (Blueprint $table) {
                $table->id();
                $table->string('code', 40)->unique();
                $table->string('name', 120);
                $table->string('type', 30)->default('shop'); // shop, godown, cold_storage, quarantine, other
                $table->string('default_rack', 100)->nullable();
                $table->boolean('is_default')->default(false);
                $table->boolean('is_active')->default(true);
                $table->string('notes', 255)->nullable();
                $table->timestamps();
            });
        }

        // Seed default locations if empty
        $now = now();
        $defaultSeeds = [
            [
                'code'         => 'SHOP',
                'name'         => 'Shop Counter / Main Shelf',
                'type'         => 'shop',
                'default_rack' => null,
                'is_default'   => true,
                'is_active'    => true,
                'notes'        => 'Primary retail & wholesale selling counter',
                'created_at'   => $now,
                'updated_at'   => $now,
            ],
            [
                'code'         => 'GDN',
                'name'         => 'Main Godown / Backroom',
                'type'         => 'godown',
                'default_rack' => null,
                'is_default'   => false,
                'is_active'    => true,
                'notes'        => 'Reserve bulk storage / backroom godown',
                'created_at'   => $now,
                'updated_at'   => $now,
            ],
            [
                'code'         => 'FRIDGE',
                'name'         => 'Cold Storage / Fridge',
                'type'         => 'cold_storage',
                'default_rack' => 'Fridge-1',
                'is_default'   => false,
                'is_active'    => true,
                'notes'        => 'Temperature-controlled cold chain storage',
                'created_at'   => $now,
                'updated_at'   => $now,
            ],
        ];

        foreach ($defaultSeeds as $seed) {
            if (!DB::table('storage_locations')->where('code', $seed['code'])->exists()) {
                DB::table('storage_locations')->insert($seed);
            }
        }

        $defaultLocationId = DB::table('storage_locations')
            ->where('is_default', true)
            ->value('id')
            ?? DB::table('storage_locations')->orderBy('id')->value('id');

        if (Schema::hasTable('products') && !Schema::hasColumn('products', 'storage_location_id')) {
            Schema::table('products', function (Blueprint $table) {
                $table->unsignedBigInteger('storage_location_id')->nullable()->after('rack')->index();
            });

            if ($defaultLocationId) {
                DB::table('products')
                    ->whereNull('storage_location_id')
                    ->update(['storage_location_id' => $defaultLocationId]);
            }
        }

        if (Schema::hasTable('batches')) {
            // Drop old (product_id, batch_number) unique index so the same batch can be split across Godown & Shop
            try {
                Schema::table('batches', function (Blueprint $table) {
                    $table->dropUnique(['product_id', 'batch_number']);
                });
            } catch (\Throwable $e) {
                // Index may already have been dropped or not exist
            }

            Schema::table('batches', function (Blueprint $table) {
                if (!Schema::hasColumn('batches', 'storage_location_id')) {
                    $table->unsignedBigInteger('storage_location_id')->nullable()->after('quantity')->index();
                }
                if (!Schema::hasColumn('batches', 'rack')) {
                    $table->string('rack', 100)->nullable()->after('storage_location_id');
                }
            });

            try {
                Schema::table('batches', function (Blueprint $table) {
                    $table->index(['product_id', 'batch_number', 'storage_location_id'], 'batches_prod_batch_loc_idx');
                });
            } catch (\Throwable $e) {
                // Ignore if index already exists
            }

            // Backfill existing batches with their product's default location & rack
            if ($defaultLocationId) {
                DB::table('batches')
                    ->whereNull('storage_location_id')
                    ->update(['storage_location_id' => $defaultLocationId]);
            }

            if (Schema::hasTable('products')) {
                $productsWithRack = DB::table('products')
                    ->whereNotNull('rack')
                    ->where('rack', '!=', '')
                    ->select(['id', 'rack', 'storage_location_id'])
                    ->get();

                foreach ($productsWithRack as $p) {
                    DB::table('batches')
                        ->where('product_id', $p->id)
                        ->whereNull('rack')
                        ->update([
                            'rack'                => $p->rack,
                            'storage_location_id' => $p->storage_location_id ?? $defaultLocationId,
                        ]);
                }
            }
        }
    }

    public function down(): void
    {
        if (Schema::hasTable('batches')) {
            Schema::table('batches', function (Blueprint $table) {
                $cols = array_filter([
                    'storage_location_id',
                    'rack',
                ], fn ($col) => Schema::hasColumn('batches', $col));

                if (!empty($cols)) {
                    $table->dropColumn($cols);
                }
            });
        }

        if (Schema::hasTable('products') && Schema::hasColumn('products', 'storage_location_id')) {
            Schema::table('products', function (Blueprint $table) {
                $table->dropColumn('storage_location_id');
            });
        }

        Schema::dropIfExists('storage_locations');
    }
};


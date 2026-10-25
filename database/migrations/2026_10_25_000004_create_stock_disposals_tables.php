<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Schema;
use Spatie\Permission\Models\Permission;
use Spatie\Permission\Models\Role;
use Spatie\Permission\PermissionRegistrar;

return new class extends Migration
{
    public function up(): void
    {
        if (!Schema::hasTable('stock_disposals')) {
            Schema::create('stock_disposals', function (Blueprint $table) {
                $table->id();
                $table->string('disposal_number', 50)->unique()->index();
                $table->date('disposal_date')->index();
                $table->string('status', 30)->default('quarantined')->index(); // quarantined, disposed, restored
                $table->string('cause', 50)->index(); // shelf_expiry, breakage_damage, temperature_spoilage, customer_return_defect, manufacturer_recall, water_pest_damage, theft_shrinkage, other
                $table->string('disposal_method', 50)->default('quarantine_holding'); // incineration, chemical_neutralization, municipal_waste, supplier_destruction, drain_flush, quarantine_holding, other
                $table->string('reference_no', 100)->nullable();
                $table->string('witness_name', 191)->nullable();
                $table->string('witness_designation', 191)->nullable();
                $table->text('notes')->nullable();
                $table->decimal('total_qty', 12, 2)->default(0);
                $table->decimal('total_cost_value', 15, 2)->default(0);
                $table->decimal('total_retail_value', 15, 2)->default(0);
                $table->boolean('has_narcotics')->default(false);
                $table->boolean('record_expense')->default(true);
                $table->foreignId('stock_adjustment_id')->nullable()->constrained('stock_adjustments')->nullOnDelete();
                $table->foreignId('expense_id')->nullable()->constrained('expenses')->nullOnDelete();
                $table->foreignId('created_by')->nullable()->constrained('users')->nullOnDelete();
                $table->foreignId('disposed_by')->nullable()->constrained('users')->nullOnDelete();
                $table->timestamp('disposed_at')->nullable();
                $table->foreignId('restored_by')->nullable()->constrained('users')->nullOnDelete();
                $table->timestamp('restored_at')->nullable();
                $table->timestamps();
            });
        }

        if (!Schema::hasTable('stock_disposal_items')) {
            Schema::create('stock_disposal_items', function (Blueprint $table) {
                $table->id();
                $table->foreignId('stock_disposal_id')->constrained('stock_disposals')->cascadeOnDelete();
                $table->foreignId('product_id')->constrained('products')->cascadeOnDelete();
                $table->foreignId('batch_id')->nullable()->constrained('batches')->nullOnDelete();
                $table->string('batch_number', 191)->nullable();
                $table->date('expiry_date')->nullable();
                $table->unsignedInteger('pack_size')->default(1);
                $table->decimal('previous_qty', 12, 2)->default(0);
                $table->decimal('dispose_qty', 12, 2)->default(0);
                $table->decimal('remaining_qty', 12, 2)->default(0);
                $table->decimal('unit_purchase_price', 15, 2)->default(0);
                $table->decimal('unit_sale_price', 15, 2)->default(0);
                $table->decimal('cost_value', 15, 2)->default(0);
                $table->decimal('retail_value', 15, 2)->default(0);
                $table->string('item_note', 500)->nullable();
                $table->timestamps();
            });
        }

        // Seed dedicated non-cash P&L write-off ExpenseCategory
        if (Schema::hasTable('expense_categories')) {
            if (!DB::table('expense_categories')->where('code', 'stock_disposal_writeoff')->exists()) {
                DB::table('expense_categories')->insert([
                    'name'        => 'Stock Expiry, Damage & Scrap Write-Off',
                    'code'        => 'stock_disposal_writeoff',
                    'description' => 'Non-cash P&L write-off for expired, broken, spoiled, or recalled inventory disposed from stock',
                    'is_active'   => true,
                    'created_at'  => now(),
                    'updated_at'  => now(),
                ]);
            }
        }

        // Register permissions
        $permissions = [
            'stock-disposal.view',
            'stock-disposal.create',
            'stock-disposal.dispose',
            'stock-disposal.restore',
            'stock-disposal.delete',
        ];

        foreach ($permissions as $perm) {
            Permission::findOrCreate($perm, 'sanctum');
        }

        app(PermissionRegistrar::class)->forgetCachedPermissions();

        $adminRole = Role::where('name', 'Admin')->where('guard_name', 'sanctum')->first();
        if ($adminRole) {
            $adminRole->givePermissionTo($permissions);
        }
    }

    public function down(): void
    {
        Schema::dropIfExists('stock_disposal_items');
        Schema::dropIfExists('stock_disposals');
    }
};


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
    /**
     * Run the migrations.
     */
    public function up(): void
    {
        // 1. Expense Categories Table
        if (!Schema::hasTable('expense_categories')) {
            Schema::create('expense_categories', function (Blueprint $table) {
                $table->id();
                $table->string('name', 100);
                $table->string('code', 50)->nullable()->unique();
                $table->text('description')->nullable();
                $table->boolean('is_active')->default(true)->index();
                $table->timestamps();
            });
        }

        // 2. Expenses Table
        if (!Schema::hasTable('expenses')) {
            Schema::create('expenses', function (Blueprint $table) {
                $table->id();
                $table->string('expense_number', 50)->unique()->index();
                $table->foreignId('expense_category_id')->constrained('expense_categories')->cascadeOnDelete();
                $table->foreignId('account_id')->nullable()->constrained('accounts')->nullOnDelete();
                $table->foreignId('user_id')->nullable()->constrained('users')->nullOnDelete();
                $table->foreignId('cash_drawer_session_id')->nullable()->constrained('cash_drawer_sessions')->nullOnDelete();
                $table->decimal('amount', 15, 2);
                $table->date('date')->index();
                $table->string('payment_method', 50)->default('cash'); // cash, bank_transfer, card, mobile_wallet, cheque, other
                $table->string('reference_number', 100)->nullable();
                $table->string('payee', 255)->nullable();
                $table->text('description')->nullable();
                $table->string('receipt_path', 500)->nullable();
                $table->boolean('is_recurring')->default(false);
                $table->string('recurring_frequency', 50)->nullable(); // monthly, weekly, yearly
                $table->timestamps();
                $table->softDeletes();

                $table->index(['date', 'expense_category_id']);
                $table->index(['payment_method', 'date']);
            });
        }

        // 3. Seed Default Popular Categories
        $defaultCategories = [
            ['name' => 'Shop / Premises Rent', 'code' => 'rent', 'description' => 'Store, shop, warehouse or premises rent'],
            ['name' => 'Salaries & Staff Wages', 'code' => 'salaries', 'description' => 'Staff monthly salaries, commission, and daily wages'],
            ['name' => 'Electricity & Utilities', 'code' => 'utilities', 'description' => 'WAPDA / K-Electric, Sui Gas, Water, and utility bills'],
            ['name' => 'Generator & Fuel', 'code' => 'fuel', 'description' => 'Petrol/diesel for generator, delivery bikes, or UPS'],
            ['name' => 'Tea & Refreshments (Kharcha)', 'code' => 'refreshments', 'description' => 'Daily staff tea, customer hospitality, and store petty cash'],
            ['name' => 'Maintenance & Repairs', 'code' => 'maintenance', 'description' => 'AC servicing, electrical, furniture, shop fixtures repairs'],
            ['name' => 'Packaging & Bags', 'code' => 'packaging', 'description' => 'Shopping shopper bags, medicine covers, cartons, tape'],
            ['name' => 'Delivery, Courier & Carriage', 'code' => 'delivery', 'description' => 'Delivery boy fuel, local cargo, courier charges'],
            ['name' => 'Printing & Office Supplies', 'code' => 'stationery', 'description' => 'Thermal paper rolls, invoice pads, stationery, cleaning items'],
            ['name' => 'Licenses, Taxes & Fees', 'code' => 'licenses', 'description' => 'Drug license renewal, trade license, municipal taxes'],
            ['name' => 'Bank & POS Card Charges', 'code' => 'bank_charges', 'description' => 'Bank transaction fees, POS machine merchant swipe charges'],
            ['name' => 'Marketing & Signboards', 'code' => 'marketing', 'description' => 'Shop signboards, banners, social media promotions'],
            ['name' => 'Miscellaneous / General', 'code' => 'miscellaneous', 'description' => 'Other general operational and ad-hoc business expenses'],
        ];

        foreach ($defaultCategories as $cat) {
            if (!DB::table('expense_categories')->where('code', $cat['code'])->exists()) {
                DB::table('expense_categories')->insert([
                    'name'        => $cat['name'],
                    'code'        => $cat['code'],
                    'description' => $cat['description'],
                    'is_active'   => true,
                    'created_at'  => now(),
                    'updated_at'  => now(),
                ]);
            }
        }

        // 4. Register Permissions
        $permissions = [
            'expense.view',
            'expense.create',
            'expense.update',
            'expense.delete',
            'expense-category.view',
            'expense-category.manage',
            'report.net-profit.view',
            'report.net-profit.export',
        ];

        foreach ($permissions as $perm) {
            Permission::findOrCreate($perm, 'sanctum');
        }

        app(PermissionRegistrar::class)->forgetCachedPermissions();

        // Assign to Admin role if exists
        $adminRole = Role::where('name', 'Admin')->where('guard_name', 'sanctum')->first();
        if ($adminRole) {
            $adminRole->givePermissionTo($permissions);
        }
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::dropIfExists('expenses');
        Schema::dropIfExists('expense_categories');
    }
};


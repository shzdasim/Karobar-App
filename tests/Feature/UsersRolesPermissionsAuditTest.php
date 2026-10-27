<?php

namespace Tests\Feature;

use App\Http\Middleware\EnsureLicensed;
use App\Models\Brand;
use App\Models\Category;
use App\Models\Customer;
use App\Models\Product;
use App\Models\Supplier;
use App\Models\ThemeSetting;
use App\Models\User;
use Database\Seeders\RolesAndPermissionsSeeder;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\Hash;
use Laravel\Sanctum\Sanctum;
use Spatie\Permission\Models\Permission;
use Spatie\Permission\Models\Role;
use Spatie\Permission\PermissionRegistrar;
use Tests\TestCase;

class UsersRolesPermissionsAuditTest extends TestCase
{
    use RefreshDatabase;

    private Category $category;
    private Brand $brand;
    private Supplier $supplier;

    protected function setUp(): void
    {
        parent::setUp();

        $this->withoutMiddleware(EnsureLicensed::class);
        $this->seed(RolesAndPermissionsSeeder::class);
        Role::firstOrCreate(['name' => 'Manager', 'guard_name' => 'sanctum']);
        Role::firstOrCreate(['name' => 'Cashier', 'guard_name' => 'sanctum']);
        app(PermissionRegistrar::class)->forgetCachedPermissions();

        $this->category = Category::create(['name' => 'Tablets']);
        $this->brand = Brand::create(['name' => 'GSK']);
        $this->supplier = Supplier::create(['name' => 'Primary Pharma']);
    }

    private function makeUser(array $overrides = []): User
    {
        static $counter = 1;
        $idx = $counter++;

        return User::create(array_merge([
            'name' => "Audit User {$idx}",
            'email' => "audit{$idx}@example.com",
            'password' => Hash::make('password123'),
            'status' => 'active',
        ], $overrides));
    }

    private function makeProduct(array $overrides = []): Product
    {
        static $prodCounter = 1;
        $idx = $prodCounter++;

        return Product::create(array_merge([
            'name' => "Product {$idx}",
            'product_code' => "PRD-{$idx}",
            'barcode' => "BAR-{$idx}",
            'category_id' => $this->category->id,
            'brand_id' => $this->brand->id,
            'supplier_id' => $this->supplier->id,
            'pack_size' => 10,
            'quantity' => 100,
            'pack_purchase_price' => 80,
            'unit_purchase_price' => 8,
            'pack_sale_price' => 100,
            'unit_sale_price' => 10,
            'avg_price' => 8,
        ], $overrides));
    }

    /**
     * GAP-01 (Resolved): UserController enforces granular user.* permissions (user.view, user.create,
     * user.update, user.delete) and no longer allows the Manager role to bypass missing user.* permissions.
     */
    public function test_gap01_resolved_granular_user_permissions_enforced_and_manager_role_cannot_bypass(): void
    {
        // 1. Custom role with user.view and user.create (without user.manage) CAN list and create users,
        //    but CANNOT delete users without user.delete
        $hrRole = Role::create(['name' => 'HR-Officer', 'guard_name' => 'sanctum']);
        $hrRole->givePermissionTo(['user.view', 'user.create']);

        $hrUser = $this->makeUser();
        $hrUser->assignRole($hrRole);

        Sanctum::actingAs($hrUser);
        $this->getJson('/api/users')->assertStatus(200);
        $createRes = $this->postJson('/api/users', [
            'name' => 'New Staff',
            'email' => 'newstaff@example.com',
            'password' => 'password123',
        ]);
        $createRes->assertStatus(201);

        $newStaffId = $createRes->json('id');
        // Lacks user.delete -> 403
        $this->deleteJson("/api/users/{$newStaffId}")->assertStatus(403);

        // 2. A Manager role with ONLY user.view CANNOT delete users
        $managerRole = Role::findByName('Manager', 'sanctum');
        $managerRole->syncPermissions(['user.view']);
        app(PermissionRegistrar::class)->forgetCachedPermissions();

        $manager = $this->makeUser();
        $manager->assignRole('Manager');
        $victim = $this->makeUser();

        Sanctum::actingAs($manager);
        $this->getJson('/api/users')->assertStatus(200);
        $this->deleteJson("/api/users/{$victim->id}")->assertStatus(403);
        $this->assertDatabaseHas('users', ['id' => $victim->id]);
    }

    /**
     * GAP-02 (Resolved): Non-Admin cannot grant permissions they do not possess themselves.
     */
    public function test_gap02_resolved_non_admin_cannot_escalate_privileges_via_permission_sync(): void
    {
        $manager = $this->makeUser();
        $manager->assignRole('Manager');
        $manager->givePermissionTo(['user.manage', 'product.view']);

        $targetUser = $this->makeUser();

        Sanctum::actingAs($manager);

        // Manager tries to grant 'backup.restore' and 'account.adjust' (which Manager does not have) -> 403
        $this->putJson("/api/users/{$targetUser->id}/permissions", [
            'permissions' => ['backup.restore', 'account.adjust'],
        ])->assertStatus(403);

        // Manager CAN grant 'product.view' (which Manager possesses) -> 200
        $this->putJson("/api/users/{$targetUser->id}/permissions", [
            'permissions' => ['product.view'],
        ])->assertStatus(200);
    }

    /**
     * GAP-03 (Resolved): Reserved Admin role names ('admin', 'super-admin') cannot be created
     * or used as a rename target by non-Admin users.
     */
    public function test_gap03_resolved_cannot_create_or_rename_role_to_super_admin_or_admin(): void
    {
        $subRole = Role::create(['name' => 'Supervisor', 'guard_name' => 'sanctum']);
        $subRole->givePermissionTo(['role.create', 'role.update', 'user.manage']);

        $attacker = $this->makeUser();
        $attacker->assignRole($subRole);

        Sanctum::actingAs($attacker);

        // Cannot rename role to 'super-admin' or 'admin' (422)
        $this->putJson("/api/roles/{$subRole->id}", [
            'name' => 'super-admin',
        ])->assertStatus(422);

        $this->putJson("/api/roles/{$subRole->id}", [
            'name' => 'admin',
        ])->assertStatus(422);

        // Cannot create a new role named 'super-admin' (422)
        $this->postJson('/api/roles', [
            'name' => 'super-admin',
        ])->assertStatus(422);

        // Also verify renaming a role without passing 'permissions' preserves its existing permissions
        $this->putJson("/api/roles/{$subRole->id}", [
            'name' => 'Senior-Supervisor',
        ])->assertStatus(200);

        $subRole->refresh();
        $this->assertTrue($subRole->hasPermissionTo('role.update'));
    }

    /**
     * GAP-04 (Resolved): GET /api/permissions and GET /api/roles allow role viewers and user managers.
     */
    public function test_gap04_resolved_permissions_and_roles_index_accessible_to_role_and_user_managers(): void
    {
        $roleManager = $this->makeUser();
        $roleManager->givePermissionTo(['role.view', 'role.create', 'role.update']);

        Sanctum::actingAs($roleManager);
        $this->getJson('/api/roles')->assertStatus(200);
        $this->getJson('/api/permissions')->assertStatus(200);

        $userManager = $this->makeUser();
        $userManager->givePermissionTo(['user.view', 'user.create']);

        Sanctum::actingAs($userManager);
        $this->getJson('/api/roles')->assertStatus(200);
        $this->getJson('/api/permissions')->assertStatus(200);
    }

    /**
     * GAP-05 (Resolved): Last active Admin cannot deactivate or demote themselves.
     */
    public function test_gap05_resolved_last_admin_cannot_deactivate_or_demote_themselves(): void
    {
        $soleAdmin = $this->makeUser();
        $soleAdmin->assignRole('Admin');

        Sanctum::actingAs($soleAdmin);

        // Cannot deactivate own account (422)
        $this->putJson("/api/users/{$soleAdmin->id}", [
            'name' => $soleAdmin->name,
            'email' => $soleAdmin->email,
            'status' => 'inactive',
        ])->assertStatus(422);

        // Cannot remove 'Admin' role from own account (422)
        $this->putJson("/api/users/{$soleAdmin->id}", [
            'name' => $soleAdmin->name,
            'email' => $soleAdmin->email,
            'roles' => ['Cashier'],
        ])->assertStatus(422);

        $this->putJson("/api/users/{$soleAdmin->id}/roles", [
            'roles' => ['Cashier'],
        ])->assertStatus(422);

        $soleAdmin->refresh();
        $this->assertEquals('active', $soleAdmin->status);
        $this->assertTrue($soleAdmin->hasRole('Admin'));
    }

    /**
     * GAP-06 (Resolved): CurrentStockReportPolicy, NearExpiryProductReportPolicy, BackupPolicy,
     * and StocktakePolicy are properly mapped and allow authorized non-Admin users.
     */
    public function test_gap06_resolved_mapped_policies_allow_authorized_non_admin_users(): void
    {
        $auditor = $this->makeUser();
        $auditor->givePermissionTo([
            'report.current-stock.view',
            'report.current-stock.export',
            'report.reorder.view',
            'report.reorder.export',
            'report.near-expiry-product.view',
            'report.near-expiry-product.export',
            'backup.view',
            'stocktake.view',
        ]);

        Sanctum::actingAs($auditor);

        $this->getJson('/api/reports/current-stock')->assertStatus(200);
        $this->getJson("/api/reports/reorder?supplier_id={$this->supplier->id}")->assertStatus(200);
        $this->getJson('/api/reports/near-expiry-product')->assertStatus(200);
        $this->getJson('/api/backups')->assertStatus(200);
        $this->getJson('/api/stocktake-sessions')->assertStatus(200);
    }

    /**
     * GAP-07 (Resolved): Policy methods accept nullable model instances when called with class strings,
     * succeeding (200) instead of throwing 500 ArgumentCountError.
     */
    public function test_gap07_resolved_policy_methods_with_class_strings_succeed_for_authorized_non_admins(): void
    {
        $staff = $this->makeUser();
        $staff->givePermissionTo([
            'report.product-comprehensive.view',
            'backup.view',
            'settings.update',
            'batch-expiry.update',
            'product.update',
            'product.delete',
        ]);

        $product = $this->makeProduct();
        ThemeSetting::create([
            'name' => 'Default Theme',
            'primary_color' => '#3b82f6',
            'secondary_color' => '#1e40af',
            'accent_color' => '#f59e0b',
            'background_color' => '#ffffff',
            'text_color' => '#111827',
            'is_active' => true,
        ]);

        Sanctum::actingAs($staff);

        $this->getJson("/api/reports/product-comprehensive?product_id={$product->id}")->assertStatus(200);
        $this->getJson('/api/backups')->assertStatus(200);
        $this->putJson('/api/theme-settings/active', [
            'primary_color' => '#000000',
        ])->assertStatus(200);
        $this->putJson("/api/batch-expiry/product-warning/{$product->id}", [
            'expiry_warning_days' => 60,
        ])->assertStatus(200);
        $this->postJson('/api/products/bulk-destroy', [
            'product_ids' => [$product->id],
        ])->assertStatus(200);
    }

    /**
     * GAP-08 (Resolved): payment.* and stocktake.* permissions are seeded and protected as core permissions.
     */
    public function test_gap08_resolved_payment_and_stocktake_permissions_seeded_and_protected(): void
    {
        $this->assertNotNull(Permission::where('name', 'payment.view')->first());
        $this->assertNotNull(Permission::where('name', 'payment.create')->first());
        $this->assertNotNull(Permission::where('name', 'payment.delete')->first());

        $stocktakePerm = Permission::where('name', 'stocktake.view')->first();
        $paymentPerm = Permission::where('name', 'payment.view')->first();
        $this->assertNotNull($stocktakePerm);
        $this->assertNotNull($paymentPerm);

        $admin = $this->makeUser();
        $admin->assignRole('Admin');
        Sanctum::actingAs($admin);

        // Both stocktake.* and payment.* are protected as core system permissions (422)
        $this->deleteJson("/api/permissions/{$stocktakePerm->id}")->assertStatus(422);
        $this->deleteJson("/api/permissions/{$paymentPerm->id}")->assertStatus(422);
    }

    /**
     * GAP-12 (Resolved): Cashier with sale-invoice.create can search products & customers
     * and check available quantity / credit summary during POS invoicing.
     */
    public function test_gap12_resolved_sale_invoice_creator_can_perform_pos_lookups(): void
    {
        $posClerk = $this->makeUser();
        $posClerk->givePermissionTo(['sale-invoice.view', 'sale-invoice.create']);

        $product = $this->makeProduct();
        $customer = Customer::create(['name' => 'Walk-in', 'phone' => '03000000000']);

        Sanctum::actingAs($posClerk);

        $this->getJson("/api/products/{$product->id}/batches")->assertStatus(200);
        $this->getJson("/api/customers/{$customer->id}/whole-sale-prices")->assertStatus(200);
        $this->getJson('/api/products/search?q=Product')->assertStatus(200);
        $this->getJson("/api/products/available-quantity?product_id={$product->id}")->assertStatus(200);
        $this->getJson('/api/customers/search?q=Walk')->assertStatus(200);
        $this->getJson("/api/customers/{$customer->id}/credit-summary")->assertStatus(200);
    }

    /**
     * GAP-15 (Resolved): Deleting custom roles and non-core permissions succeeds (200 / 204)
     * without throwing a 500 error now that config/auth.php defines the 'sanctum' guard provider.
     */
    public function test_gap15_resolved_deleting_custom_role_or_permission_succeeds_cleanly(): void
    {
        $admin = $this->makeUser();
        $admin->assignRole('Admin');
        Sanctum::actingAs($admin);

        $customRole = Role::create(['name' => 'TempRole', 'guard_name' => 'sanctum']);
        $customPerm = Permission::create(['name' => 'custom.action', 'guard_name' => 'sanctum']);

        $this->deleteJson("/api/roles/{$customRole->id}")->assertStatus(200);
        $this->deleteJson("/api/permissions/{$customPerm->id}")->assertStatus(204);

        $this->assertDatabaseMissing('roles', ['id' => $customRole->id]);
        $this->assertDatabaseMissing('permissions', ['id' => $customPerm->id]);
    }

    /**
     * Barcode Studio / Generator (barcode.*) is strictly isolated from Products (product.*)
     * and Purchase Orders (purchase-order.*).
     */
    public function test_barcode_studio_permissions_strictly_isolated_from_products_and_pos(): void
    {
        $product = $this->makeProduct(['barcode' => 'BAR-999']);

        // 1. User with product.view / product.update / sale-invoice.create CANNOT access Barcode Studio
        $productViewer = $this->makeUser();
        $productViewer->givePermissionTo(['product.view', 'product.update', 'sale-invoice.create']);
        Sanctum::actingAs($productViewer);

        $this->getJson('/api/barcodes/templates')->assertStatus(403);
        $this->getJson('/api/barcodes/products-search?q=Product')->assertStatus(403);
        $this->getJson('/api/barcodes/purchase-orders')->assertStatus(403);
        $this->postJson('/api/barcodes/templates', [
            'name' => 'Custom 50x25',
            'config' => ['width' => 50, 'height' => 25],
        ])->assertStatus(403);

        // 2. User with barcode.view / barcode.print CAN access Barcode Studio lookups and templates,
        //    but CANNOT access the Products module index or Purchase Orders module index, nor create templates
        $barcodePrinter = $this->makeUser();
        $barcodePrinter->givePermissionTo(['barcode.view', 'barcode.print']);
        Sanctum::actingAs($barcodePrinter);

        $this->getJson('/api/barcodes/templates')->assertStatus(200);
        $this->getJson('/api/barcodes/products-search?q=Product')->assertStatus(200);
        $this->getJson('/api/barcodes/purchase-orders')->assertStatus(200);
        $this->getJson('/api/products/search?q=Product')->assertStatus(200);
        $this->getJson('/api/categories/search')->assertStatus(200);
        $this->getJson('/api/brands/search')->assertStatus(200);
        $this->getJson('/api/suppliers/search')->assertStatus(200);

        // Strict isolation: cannot list main Products, Purchase Orders, Categories, or Scan POS
        $this->getJson('/api/products')->assertStatus(403);
        $this->getJson('/api/purchase-orders')->assertStatus(403);
        $this->getJson('/api/categories')->assertStatus(403);
        $this->getJson('/api/brands')->assertStatus(403);
        $this->getJson('/api/suppliers')->assertStatus(403);
        $this->getJson('/api/barcodes/scan?barcode=BAR-999')->assertStatus(403);
        $this->postJson('/api/barcodes/templates', [
            'name' => 'Unauthorized Template',
            'config' => ['width' => 50, 'height' => 25],
        ])->assertStatus(403);

        // 3. User with barcode.create / barcode.update / barcode.delete can manage templates
        $barcodeDesigner = $this->makeUser();
        $barcodeDesigner->givePermissionTo(['barcode.view', 'barcode.create', 'barcode.update', 'barcode.delete']);
        Sanctum::actingAs($barcodeDesigner);

        $createRes = $this->postJson('/api/barcodes/templates', [
            'name' => 'Shelf Tag 40x30',
            'paper_type' => 'roll',
            'width_mm' => 40,
            'height_mm' => 30,
            'orientation' => 'landscape',
        ]);
        $createRes->assertStatus(201);
        $tplId = $createRes->json('template.id') ?? $createRes->json('id');

        $this->putJson("/api/barcodes/templates/{$tplId}", [
            'name' => 'Shelf Tag 40x30 v2',
            'paper_type' => 'roll',
            'width_mm' => 40,
            'height_mm' => 30,
            'orientation' => 'landscape',
        ])->assertStatus(200);

        $this->deleteJson("/api/barcodes/templates/{$tplId}")->assertStatus(200);
    }

    /**
     * Barcode POS (barcode-pos.*) is strictly isolated from regular Sale Invoices (sale-invoice.*).
     */
    public function test_barcode_pos_permissions_strictly_isolated_from_sale_invoices(): void
    {
        $product = $this->makeProduct(['barcode' => 'POS-SCAN-001', 'quantity' => 50]);
        $customer = Customer::create(['name' => 'POS Customer', 'phone' => '03001112222']);

        // 1. User with sale-invoice.view & sale-invoice.create CANNOT use Barcode POS scanner endpoint
        $standardClerk = $this->makeUser();
        $standardClerk->givePermissionTo(['sale-invoice.view', 'sale-invoice.create']);
        Sanctum::actingAs($standardClerk);

        $this->getJson('/api/barcodes/scan?barcode=POS-SCAN-001')->assertStatus(403);

        // 2. User with barcode-pos.view & barcode-pos.create CAN scan barcodes, search customers,
        //    fetch active accounts, and open/check cash drawer, but CANNOT view the Sale Invoices register
        //    or Customer/Account master lists
        $posOperator = $this->makeUser();
        $posOperator->givePermissionTo(['barcode-pos.view', 'barcode-pos.create']);
        Sanctum::actingAs($posOperator);

        $this->getJson('/api/barcodes/scan?barcode=POS-SCAN-001')->assertStatus(200);
        $this->getJson('/api/customers/search?q=POS')->assertStatus(200);
        $this->getJson("/api/customers/{$customer->id}/credit-summary")->assertStatus(200);
        $this->getJson('/api/accounts?active_only=1')->assertStatus(200);
        $this->getJson('/api/cash-drawer/current')->assertStatus(200);

        // Strict isolation: cannot view Sale Invoices list, Customers list, or Chart of Accounts
        $this->getJson('/api/sale-invoices')->assertStatus(403);
        $this->getJson('/api/customers')->assertStatus(403);
        $this->getJson('/api/accounts')->assertStatus(403);
        $this->getJson('/api/barcodes/templates')->assertStatus(403);
    }

    /**
     * Sale Return Invoices (sale-return.*), Sale Returns Report (report.sale-returns.*),
     * and Sale Invoices (sale-invoice.*) are strictly isolated from one another.
     */
    public function test_sale_return_module_and_sale_returns_report_and_sale_invoices_are_strictly_isolated(): void
    {
        $customer = Customer::create(['name' => 'Return Customer', 'phone' => '03009998888']);

        // 1. User with ONLY sale-return.view can view Sale Return Invoices (/api/sale-returns),
        //    but gets 403 on Sale Returns Report (/api/reports/sale-returns) and Sale Invoices (/api/sale-invoices)
        $returnViewer = $this->makeUser();
        $returnViewer->givePermissionTo(['sale-return.view']);
        Sanctum::actingAs($returnViewer);

        $this->getJson('/api/sale-returns')->assertStatus(200);
        $this->getJson('/api/reports/sale-returns')->assertStatus(403);
        $this->getJson('/api/sale-invoices')->assertStatus(403);
        $this->getJson("/api/sale-invoices?customer_id={$customer->id}")->assertStatus(403);

        // 2. User with ONLY report.sale-returns.view can view the Sale Returns Report,
        //    but gets 403 on Sale Return Invoices (/api/sale-returns) and Sale Invoices (/api/sale-invoices)
        $reportViewer = $this->makeUser();
        $reportViewer->givePermissionTo(['report.sale-returns.view']);
        Sanctum::actingAs($reportViewer);

        $this->getJson('/api/reports/sale-returns')->assertStatus(200);
        $this->getJson('/api/sale-returns')->assertStatus(403);
        $this->getJson('/api/sale-invoices')->assertStatus(403);

        // 3. User with sale-return.create CAN lookup a specific customer's sale invoices (?customer_id=...)
        //    and search customers/products to create a return, WITHOUT getting access to the full
        //    unfiltered Sale Invoices register (/api/sale-invoices) or the Sale Returns Report
        $returnCreator = $this->makeUser();
        $returnCreator->givePermissionTo(['sale-return.view', 'sale-return.create']);
        Sanctum::actingAs($returnCreator);

        $this->getJson('/api/customers/search?q=Return')->assertStatus(200);
        $this->getJson("/api/sale-invoices?customer_id={$customer->id}")->assertStatus(200);
        $this->getJson('/api/sale-invoices/search?q=INV')->assertStatus(200);
        // Unfiltered sale invoices index and sale returns report remain forbidden!
        $this->getJson('/api/sale-invoices')->assertStatus(403);
        $this->getJson('/api/reports/sale-returns')->assertStatus(403);
    }

    /**
     * Purchase Return Invoices (purchase-return.*), Purchase Returns Report (report.purchase-returns.*),
     * and Purchase Invoices (purchase-invoice.*) are strictly isolated from one another.
     */
    public function test_purchase_return_module_and_purchase_returns_report_and_purchase_invoices_are_strictly_isolated(): void
    {
        // 1. User with ONLY purchase-return.view can view Purchase Return Invoices (/api/purchase-returns),
        //    but gets 403 on Purchase Returns Report and Purchase Invoices
        $returnViewer = $this->makeUser();
        $returnViewer->givePermissionTo(['purchase-return.view']);
        Sanctum::actingAs($returnViewer);

        $this->getJson('/api/purchase-returns')->assertStatus(200);
        $this->getJson('/api/reports/purchase-returns')->assertStatus(403);
        $this->getJson('/api/purchase-invoices')->assertStatus(403);
        $this->getJson("/api/purchase-invoices?supplier_id={$this->supplier->id}")->assertStatus(403);

        // 2. User with ONLY report.purchase-returns.view can view Purchase Returns Report,
        //    but gets 403 on Purchase Return Invoices and Purchase Invoices
        $reportViewer = $this->makeUser();
        $reportViewer->givePermissionTo(['report.purchase-returns.view']);
        Sanctum::actingAs($reportViewer);

        $this->getJson('/api/reports/purchase-returns')->assertStatus(200);
        $this->getJson('/api/purchase-returns')->assertStatus(403);
        $this->getJson('/api/purchase-invoices')->assertStatus(403);

        // 3. User with purchase-return.create CAN lookup a supplier's purchase invoices (?supplier_id=...)
        //    without getting access to the unfiltered Purchase Invoices register or Purchase Returns Report
        $returnCreator = $this->makeUser();
        $returnCreator->givePermissionTo(['purchase-return.view', 'purchase-return.create']);
        Sanctum::actingAs($returnCreator);

        $this->getJson('/api/suppliers/search?q=Primary')->assertStatus(200);
        $this->getJson("/api/purchase-invoices?supplier_id={$this->supplier->id}")->assertStatus(200);
        $this->getJson('/api/purchase-invoices/search?q=PI')->assertStatus(200);
        $this->getJson('/api/purchase-invoices')->assertStatus(403);
        $this->getJson('/api/reports/purchase-returns')->assertStatus(403);
    }

    /**
     * All other shared-permission modules and reports (Batch Expiry, Stock Disposals, Payments,
     * Cost of Sale Detail, Net Profit, Purchase Summary, Reorder, Outstanding Aging) are strictly isolated.
     */
    public function test_all_remaining_modules_and_reports_have_strict_non_overlapping_permissions(): void
    {
        $product = $this->makeProduct();

        // A. Batch Expiry (batch-expiry.*) vs Products (product.*)
        $expiryViewer = $this->makeUser();
        $expiryViewer->givePermissionTo(['batch-expiry.view']);
        Sanctum::actingAs($expiryViewer);
        $this->getJson('/api/batch-expiry')->assertStatus(200);
        $this->getJson('/api/products')->assertStatus(403);
        $this->putJson("/api/batch-expiry/product-warning/{$product->id}", ['expiry_warning_days' => 45])->assertStatus(403);

        // B. Stock Disposals (stock-disposal.*) vs Stock Adjustments (stock-adjustment.*)
        $adjustmentViewer = $this->makeUser();
        $adjustmentViewer->givePermissionTo(['stock-adjustment.view', 'stock-adjustment.create']);
        Sanctum::actingAs($adjustmentViewer);
        $this->getJson('/api/stock-adjustments')->assertStatus(200);
        $this->getJson('/api/stock-disposals')->assertStatus(403);

        $disposalViewer = $this->makeUser();
        $disposalViewer->givePermissionTo(['stock-disposal.view']);
        Sanctum::actingAs($disposalViewer);
        $this->getJson('/api/stock-disposals')->assertStatus(200);
        $this->getJson('/api/stock-adjustments')->assertStatus(403);

        // C. Payment Vouchers (payment.*) vs Customer/Supplier Ledger (customer-ledger.* / supplier-ledger.*)
        $ledgerViewer = $this->makeUser();
        $ledgerViewer->givePermissionTo(['customer-ledger.view', 'supplier-ledger.view']);
        Sanctum::actingAs($ledgerViewer);
        $this->getJson('/api/payments')->assertStatus(403);
        $this->getJson('/api/reports/outstanding-aging')->assertStatus(403);

        $paymentViewer = $this->makeUser();
        $paymentViewer->givePermissionTo(['payment.view']);
        Sanctum::actingAs($paymentViewer);
        $this->getJson('/api/payments')->assertStatus(200);
        $this->getJson('/api/reports/outstanding-aging')->assertStatus(403);

        // D. Cost of Sale vs Cost of Sale Detail vs Net Profit (P&L)
        $cosViewer = $this->makeUser();
        $cosViewer->givePermissionTo(['report.cost-of-sale.view']);
        Sanctum::actingAs($cosViewer);
        $this->getJson('/api/reports/cost-of-sale')->assertStatus(200);
        $this->getJson('/api/reports/cost-of-sale-detail')->assertStatus(403);
        $this->getJson('/api/reports/net-profit')->assertStatus(403);

        $cosDetailViewer = $this->makeUser();
        $cosDetailViewer->givePermissionTo(['report.cost-of-sale-detail.view']);
        Sanctum::actingAs($cosDetailViewer);
        $this->getJson('/api/reports/cost-of-sale-detail')->assertStatus(200);
        $this->getJson('/api/reports/cost-of-sale')->assertStatus(403);
        $this->getJson('/api/reports/net-profit')->assertStatus(403);

        $netProfitViewer = $this->makeUser();
        $netProfitViewer->givePermissionTo(['report.net-profit.view']);
        Sanctum::actingAs($netProfitViewer);
        $this->getJson('/api/reports/net-profit')->assertStatus(200);
        $this->getJson('/api/reports/cost-of-sale')->assertStatus(403);
        $this->getJson('/api/reports/cost-of-sale-detail')->assertStatus(403);

        // E. Purchase Detail vs Purchase Summary
        $purchaseDetailViewer = $this->makeUser();
        $purchaseDetailViewer->givePermissionTo(['report.purchase-detail.view']);
        Sanctum::actingAs($purchaseDetailViewer);
        $this->getJson('/api/reports/purchase-detail')->assertStatus(200);
        $this->getJson('/api/reports/purchase-summary')->assertStatus(403);

        $purchaseSummaryViewer = $this->makeUser();
        $purchaseSummaryViewer->givePermissionTo(['report.purchase-summary.view']);
        Sanctum::actingAs($purchaseSummaryViewer);
        $this->getJson('/api/reports/purchase-summary')->assertStatus(200);
        $this->getJson('/api/reports/purchase-detail')->assertStatus(403);

        // F. Current Stock vs Low Stock & Reorder
        $currentStockOnly = $this->makeUser();
        $currentStockOnly->givePermissionTo(['report.current-stock.view']);
        Sanctum::actingAs($currentStockOnly);
        $this->getJson('/api/reports/current-stock')->assertStatus(200);
        $this->getJson("/api/reports/reorder?supplier_id={$this->supplier->id}")->assertStatus(403);

        $reorderOnly = $this->makeUser();
        $reorderOnly->givePermissionTo(['report.reorder.view']);
        Sanctum::actingAs($reorderOnly);
        $this->getJson("/api/reports/reorder?supplier_id={$this->supplier->id}")->assertStatus(200);
        $this->getJson('/api/reports/current-stock')->assertStatus(403);

        // G. Outstanding & Aging Report
        $agingViewer = $this->makeUser();
        $agingViewer->givePermissionTo(['report.outstanding-aging.view']);
        Sanctum::actingAs($agingViewer);
        $this->getJson('/api/reports/outstanding-aging')->assertStatus(200);
        $this->getJson('/api/reports/overdue-work-queue')->assertStatus(200);
        $this->getJson('/api/payments')->assertStatus(403);
    }
}

<?php

namespace Tests\Feature;

use App\Models\Batch;
use App\Models\Brand;
use App\Models\Category;
use App\Models\Product;
use App\Models\Supplier;
use App\Models\User;
use Carbon\Carbon;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Spatie\Permission\Models\Role;
use Tests\TestCase;

class ExpiryReturnPolicyTest extends TestCase
{
    use RefreshDatabase;

    private User $admin;

    protected function setUp(): void
    {
        parent::setUp();
        $this->withoutMiddleware(\App\Http\Middleware\EnsureLicensed::class);

        $role = Role::firstOrCreate(['name' => 'Admin', 'guard_name' => 'sanctum']);
        $this->admin = User::create([
            'name'     => 'Test Admin',
            'email'    => 'admin@test.com',
            'password' => bcrypt('password'),
            'status'   => 'active',
        ]);
        $this->admin->assignRole($role);
    }

    public function test_brand_can_configure_expiry_return_policies_and_filter_by_policy(): void
    {
        // 1. Create Brand with 6 Months Before Expiry policy
        $res1 = $this->actingAs($this->admin, 'sanctum')->postJson('/api/brands', [
            'name'                 => 'GSK Pakistan',
            'expiry_return_policy' => 'before_expiry',
            'expiry_return_months' => 6,
            'expiry_return_notes'  => 'Intimate 15 days before 6-month mark',
        ]);
        $res1->assertCreated();
        $this->assertEquals('before_expiry', $res1->json('expiry_return_policy'));
        $this->assertEquals(6, $res1->json('expiry_return_months'));
        $this->assertEquals(180, $res1->json('expiry_return_days'));
        $this->assertEquals('6 Months Before Expiry', $res1->json('expiry_policy_label'));

        // 2. Create Brand that Accepts Expired Products
        $res2 = $this->actingAs($this->admin, 'sanctum')->postJson('/api/brands', [
            'name'                 => 'Hilton Pharma',
            'expiry_return_policy' => 'accepts_expired',
        ]);
        $res2->assertCreated();
        $this->assertEquals('accepts_expired', $res2->json('expiry_return_policy'));
        $this->assertEquals(0, $res2->json('expiry_return_months'));
        $this->assertEquals('Accepts Expired Products', $res2->json('expiry_policy_label'));

        // 3. Create Brand with 1 Year Before Expiry policy
        $res3 = $this->actingAs($this->admin, 'sanctum')->postJson('/api/brands', [
            'name'                 => 'Abbott Labs',
            'expiry_return_policy' => 'before_expiry',
            'expiry_return_months' => 12,
        ]);
        $res3->assertCreated();
        $this->assertEquals(12, $res3->json('expiry_return_months'));
        $this->assertEquals(365, $res3->json('expiry_return_days'));
        $this->assertEquals('1 Year Before Expiry', $res3->json('expiry_policy_label'));

        // 4. Filter brands by policy=before_6
        $list6m = $this->actingAs($this->admin, 'sanctum')->getJson('/api/brands?policy=before_6');
        $list6m->assertOk();
        $this->assertCount(1, $list6m->json('data'));
        $this->assertEquals('GSK Pakistan', $list6m->json('data.0.name'));

        // 5. Filter brands by policy=accepts_expired
        $listExp = $this->actingAs($this->admin, 'sanctum')->getJson('/api/brands?policy=accepts_expired');
        $listExp->assertOk();
        $this->assertCount(1, $listExp->json('data'));
        $this->assertEquals('Hilton Pharma', $listExp->json('data.0.name'));
    }

    public function test_supplier_can_configure_expiry_return_policy_and_intimation_evaluates_batches(): void
    {
        $supRes = $this->actingAs($this->admin, 'sanctum')->postJson('/api/suppliers', [
            'name'                 => 'Premier Pharma Distributors',
            'phone'                => '0300-1112223',
            'expiry_return_policy' => 'before_expiry',
            'expiry_return_months' => 6,
            'expiry_return_notes'  => 'Original invoice required for 6M prior return',
        ]);
        $supRes->assertCreated();
        $supplierId = $supRes->json('id');
        $this->assertEquals('6 Months Before Expiry', $supRes->json('expiry_policy_label'));

        $brand = Brand::create([
            'name'                 => 'Getz Pharma',
            'expiry_return_policy' => 'before_expiry',
            'expiry_return_months' => 6,
            'expiry_return_days'   => 180,
        ]);

        $category = Category::create(['name' => 'Tablets']);

        $product = Product::create([
            'product_code'        => 'PRD-500',
            'barcode'             => '8909999999991',
            'name'                => 'Risek 40mg Capsules',
            'pack_size'           => 14,
            'quantity'            => 100,
            'unit_purchase_price' => 20,
            'unit_sale_price'     => 25,
            'narcotic'            => 'no',
            'category_id'         => $category->id,
            'brand_id'            => $brand->id,
            'supplier_id'         => $supplierId,
        ]);

        // Create a batch expiring in 5 months (already inside the 6-month before-expiry return window!)
        $expDateInsideWindow = Carbon::today()->addMonthsNoOverflow(5)->toDateString();
        Batch::create([
            'product_id'   => $product->id,
            'batch_number' => 'B-6M-DUE',
            'expiry_date'  => $expDateInsideWindow,
            'quantity'     => 40,
        ]);

        // Create a batch expiring in 11 months (safe)
        $expDateSafe = Carbon::today()->addMonthsNoOverflow(11)->toDateString();
        Batch::create([
            'product_id'   => $product->id,
            'batch_number' => 'B-SAFE',
            'expiry_date'  => $expDateSafe,
            'quantity'     => 60,
        ]);

        // Check Brand show endpoint includes intimation_batches with accurate status
        $brandShow = $this->actingAs($this->admin, 'sanctum')->getJson("/api/brands/{$brand->id}");
        $brandShow->assertOk();
        $batches = collect($brandShow->json('intimation_batches'))->keyBy('batch_number');

        $this->assertEquals('return_window_open', $batches['B-6M-DUE']['intimation_status']);
        $this->assertTrue((bool) $batches['B-6M-DUE']['is_intimation_due']);
        $this->assertEquals('safe', $batches['B-SAFE']['intimation_status']);
        $this->assertFalse((bool) $batches['B-SAFE']['is_intimation_due']);

        // Check Supplier show endpoint also includes intimation_batches
        $supShow = $this->actingAs($this->admin, 'sanctum')->getJson("/api/suppliers/{$supplierId}");
        $supShow->assertOk();
        $this->assertCount(2, $supShow->json('intimation_batches'));

        // Check Batch Expiry endpoint includes resolved policy & return_deadline_date
        $batchExpRes = $this->actingAs($this->admin, 'sanctum')->getJson('/api/batch-expiry');
        $batchExpRes->assertOk();
        $rows = collect($batchExpRes->json('rows'))->keyBy('batch_number');
        $this->assertEquals('6 Months Before Expiry', $rows['B-6M-DUE']['expiry_policy_label']);
        $this->assertEquals('return_window_open', $rows['B-6M-DUE']['intimation_status']);
    }
}


<?php

namespace Tests\Feature;

use App\Http\Controllers\ProductController;
use App\Models\User;
use Illuminate\Auth\Access\AuthorizationException;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Gate;
use Illuminate\Support\Facades\Schema;
use Tests\TestCase;

class ProductSearchTest extends TestCase
{
    protected function setUp(): void
    {
        parent::setUp();
        (require database_path('migrations/0001_01_01_000000_create_users_table.php'))->up();
        (require database_path('migrations/2025_08_10_080111_create_permission_tables.php'))->up();
        // Isolate listing behavior from unrelated accounting migrations.
        foreach (['brands', 'suppliers', 'categories'] as $table) {
            Schema::create($table, function (Blueprint $table) {
                $table->id();
                $table->string('name');
            });
        }
        Schema::create('products', function (Blueprint $table) {
            $table->id();
            $table->string('name');
            $table->string('product_code');
            $table->string('image')->nullable();
            $table->unsignedBigInteger('brand_id')->nullable();
            $table->unsignedBigInteger('supplier_id')->nullable();
            $table->unsignedBigInteger('category_id')->nullable();
            $table->integer('quantity');
            $table->integer('pack_size');
        });
        foreach (['batches', 'purchase_invoice_items'] as $table) {
            Schema::create($table, function (Blueprint $table) {
                $table->id();
                $table->unsignedBigInteger('product_id');
            });
        }
        $this->actingAs((new User)->forceFill(['id' => 1, 'name' => 'Search tester']));
        DB::table('brands')->insert(['id' => 1, 'name' => 'Acme medicines']);
        DB::table('suppliers')->insert(['id' => 1, 'name' => 'Central distributor']);
        DB::table('products')->insert([
            ['id' => 1, 'name' => 'Vitamin supplement', 'product_code' => 'P1', 'brand_id' => null, 'supplier_id' => null, 'quantity' => 2, 'pack_size' => 10],
            ['id' => 2, 'name' => 'Pain relief', 'product_code' => 'P2', 'brand_id' => 1, 'supplier_id' => null, 'quantity' => 20, 'pack_size' => 10],
            ['id' => 3, 'name' => 'First aid kit', 'product_code' => 'P3', 'brand_id' => null, 'supplier_id' => 1, 'quantity' => 1, 'pack_size' => 10],
        ]);
        DB::table('purchase_invoice_items')->insert([['product_id' => 1], ['product_id' => 2], ['product_id' => 3]]);
    }

    private function listing(array $params): array
    {
        Gate::before(fn () => true);
        $request = Request::create('/api/products', 'GET', $params);
        $this->app->instance('request', $request);
        return app(ProductController::class)->index($request)->getData(true);
    }

    public function test_search_matches_partial_names_brands_and_suppliers_independently(): void
    {
        foreach (['Vitamin' => 1, 'Pain' => 2, 'First' => 3, 'Acme' => 2, 'Central' => 3] as $term => $id) {
            $this->assertSame([$id], array_column($this->listing(['q' => $term])['data'], 'id'));
        }
        $this->assertSame([], $this->listing(['q' => 'unknown'])['data']);
        $this->assertSame(3, $this->listing(['q' => '   '])['total']);
    }

    public function test_search_matches_prefix_from_start_and_not_middle(): void
    {
        DB::table('products')->insert([
            ['id' => 10, 'name' => 'xyx product', 'product_code' => 'P10', 'brand_id' => null, 'supplier_id' => null, 'quantity' => 10, 'pack_size' => 1],
            ['id' => 11, 'name' => 'axt product', 'product_code' => 'P11', 'brand_id' => null, 'supplier_id' => null, 'quantity' => 10, 'pack_size' => 1],
        ]);

        // Searching 'x' must only return 'xyx product' (starts with x), NOT 'axt product' (contains x)
        $results = $this->listing(['q' => 'x']);
        $matchedIds = array_column($results['data'], 'id');

        $this->assertContains(10, $matchedIds);
        $this->assertNotContains(11, $matchedIds);
    }

    public function test_stock_filter_constrains_supplier_and_brand_matches(): void
    {
        $this->assertSame([], $this->listing(['q' => 'Acme', 'low_stock' => 1])['data']);
        $this->assertSame([3], array_column($this->listing(['q' => 'Central', 'low_stock' => 1])['data'], 'id'));
    }

    public function test_pagination_and_legacy_filters_remain_supported(): void
    {
        $page = $this->listing(['per_page' => 1, 'page' => 2]);
        $this->assertSame(3, $page['total']);
        $this->assertSame(2, $page['current_page']);
        $this->assertCount(1, $page['data']);
        $this->assertSame([2], array_column($this->listing(['q_brand' => 'Acme'])['data'], 'id'));
    }

    public function test_listing_still_requires_authorization(): void
    {
        Gate::before(fn () => false);
        $this->expectException(AuthorizationException::class);
        app(ProductController::class)->index(Request::create('/api/products', 'GET', ['q' => 'Pain']));
    }

    public function test_search_and_index_match_formulation_barcode_and_rack(): void
    {
        Schema::table('products', function (Blueprint $table) {
            $table->string('barcode')->nullable();
            $table->string('formulation')->nullable();
            $table->string('rack')->nullable();
        });

        DB::table('products')->insert([
            [
                'id' => 20,
                'name' => 'Augmentin 625mg',
                'product_code' => 'PRD-0020',
                'barcode' => '8964009988776',
                'formulation' => 'Amoxicillin 500mg + Clavulanic Acid 125mg',
                'rack' => 'RACK-B4',
                'brand_id' => 1,
                'supplier_id' => 1,
                'quantity' => 50,
                'pack_size' => 10,
            ],
        ]);

        // 1. Index endpoint searches by generic/salt formulation (including second salt), barcode, and rack
        $bySalt = $this->listing(['q' => 'Clavulanic']);
        $this->assertSame([20], array_column($bySalt['data'], 'id'));
        $this->assertSame('Amoxicillin 500mg + Clavulanic Acid 125mg', $bySalt['data'][0]['formulation']);
        $this->assertSame('8964009988776', $bySalt['data'][0]['barcode']);
        $this->assertSame('RACK-B4', $bySalt['data'][0]['rack']);

        $byBarcode = $this->listing(['q' => '8964009988']);
        $this->assertSame([20], array_column($byBarcode['data'], 'id'));

        $byRack = $this->listing(['q' => 'RACK-B4']);
        $this->assertSame([20], array_column($byRack['data'], 'id'));

        // 2. Lookup search endpoint (/api/products/search) matches formulation, barcode, and rack
        $lookupSalt = app(ProductController::class)->search(
            Request::create('/api/products/search', 'GET', ['q' => 'Clavulanic'])
        )->getData(true);
        $this->assertSame([20], array_column($lookupSalt, 'id'));
        $this->assertSame('Amoxicillin 500mg + Clavulanic Acid 125mg', $lookupSalt[0]['formulation']);

        $lookupBarcode = app(ProductController::class)->search(
            Request::create('/api/products/search', 'GET', ['q' => '8964009988776'])
        )->getData(true);
        $this->assertSame([20], array_column($lookupBarcode, 'id'));

        $lookupRack = app(ProductController::class)->search(
            Request::create('/api/products/search', 'GET', ['q' => 'RACK-B4'])
        )->getData(true);
        $this->assertSame([20], array_column($lookupRack, 'id'));
    }
}

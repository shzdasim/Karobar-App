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
        foreach (['supplement' => 1, 'medicines' => 2, 'distributor' => 3] as $term => $id) {
            $this->assertSame([$id], array_column($this->listing(['q' => $term])['data'], 'id'));
        }
        $this->assertSame([], $this->listing(['q' => 'unknown'])['data']);
        $this->assertSame(3, $this->listing(['q' => '   '])['total']);
    }

    public function test_stock_filter_constrains_supplier_and_brand_matches(): void
    {
        $this->assertSame([], $this->listing(['q' => 'medicines', 'low_stock' => 1])['data']);
        $this->assertSame([3], array_column($this->listing(['q' => 'distributor', 'low_stock' => 1])['data'], 'id'));
    }

    public function test_pagination_and_legacy_filters_remain_supported(): void
    {
        $page = $this->listing(['q' => 'i', 'per_page' => 1, 'page' => 2]);
        $this->assertSame(3, $page['total']);
        $this->assertSame(2, $page['current_page']);
        $this->assertCount(1, $page['data']);
        $this->assertSame([2], array_column($this->listing(['q_brand' => 'Acme'])['data'], 'id'));
    }

    public function test_listing_still_requires_authorization(): void
    {
        Gate::before(fn () => false);
        $this->expectException(AuthorizationException::class);
        app(ProductController::class)->index(Request::create('/api/products', 'GET', ['q' => 'medicines']));
    }
}

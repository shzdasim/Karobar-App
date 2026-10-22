<?php

namespace Tests\Feature;

use App\Models\Setting;
use App\Models\User;
use Illuminate\Support\Facades\Gate;
use Tests\TestCase;

class CurrencySettingTest extends TestCase
{
    private User $user;

    protected function setUp(): void
    {
        parent::setUp();

        foreach ([
            '0001_01_01_000000_create_users_table.php',
            '2025_08_10_080111_create_permission_tables.php',
            '2025_09_02_133214_create_settings_table.php',
            '2025_09_25_120000_add_thermal_template_to_settings_table.php',
            '2026_02_02_000000_add_navigation_style_to_settings_table.php',
            '2026_04_20_000000_add_sale_system_to_settings_table.php',
            '2026_04_25_000000_add_a4_template_to_settings_table.php',
            '2026_09_09_000000_add_shop_type_to_settings_table.php',
            '2026_10_23_000002_add_currency_to_settings_table.php',
        ] as $file) {
            (require database_path('migrations/'.$file))->up();
        }

        $this->user = User::forceCreate([
            'name' => 'Admin User',
            'email' => 'admin@test.test',
            'password' => bcrypt('secret'),
        ]);

        $this->actingAs($this->user);
        $this->withoutMiddleware(\App\Http\Middleware\EnsureLicensed::class);
        Gate::before(fn () => true);
    }

    public function test_currency_endpoint_returns_default_pkr(): void
    {
        $response = $this->getJson('/api/currency');
        $response->assertStatus(200);
        $response->assertJson([
            'currency_code' => 'PKR',
            'currency_symbol' => 'Rs.',
            'currency_position' => 'before',
            'currency_decimal_places' => 2,
        ]);
    }

    public function test_currency_settings_can_be_updated_to_usd(): void
    {
        $payload = [
            'store_name' => 'Global Store',
            'printer_type' => 'thermal',
            'currency_code' => 'USD',
            'currency_symbol' => '$',
            'currency_position' => 'before',
            'currency_decimal_places' => 2,
        ];

        $response = $this->postJson('/api/settings', $payload);
        $response->assertStatus(200);
        $response->assertJson([
            'currency_code' => 'USD',
            'currency_symbol' => '$',
            'currency_position' => 'before',
            'currency_decimal_places' => 2,
        ]);

        $currencyResp = $this->getJson('/api/currency');
        $currencyResp->assertStatus(200);
        $currencyResp->assertJson([
            'currency_code' => 'USD',
            'currency_symbol' => '$',
        ]);
    }

    public function test_currency_settings_support_custom_symbol_and_position(): void
    {
        $payload = [
            'store_name' => 'Euro Shop',
            'printer_type' => 'a4',
            'currency_code' => 'EUR',
            'currency_symbol' => '€',
            'currency_position' => 'after',
            'currency_decimal_places' => 0,
        ];

        $response = $this->postJson('/api/settings', $payload);
        $response->assertStatus(200);

        $currencyResp = $this->getJson('/api/currency');
        $currencyResp->assertStatus(200);
        $currencyResp->assertJson([
            'currency_code' => 'EUR',
            'currency_symbol' => '€',
            'currency_position' => 'after',
            'currency_decimal_places' => 0,
        ]);
    }
}

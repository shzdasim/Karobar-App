<?php

namespace Tests\Feature;

use App\Http\Controllers\SqliteImportController;
use App\Http\Middleware\EnsureLicensed;
use App\Models\Setting;
use App\Models\User;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\Hash;
use Laravel\Sanctum\Sanctum;
use Spatie\Permission\Models\Role;
use Tests\TestCase;

class SecurityAudit20261116Test extends TestCase
{
    use RefreshDatabase;

    protected function setUp(): void
    {
        parent::setUp();

        $this->withoutMiddleware(EnsureLicensed::class);

        Role::firstOrCreate(['name' => 'Admin', 'guard_name' => 'sanctum']);
        Role::firstOrCreate(['name' => 'Cashier', 'guard_name' => 'sanctum']);
    }

    private function createUser(string $role, string $email, string $password = 'Secret!2026'): User
    {
        $user = User::create([
            'name' => $role . ' User',
            'email' => $email,
            'password' => Hash::make($password),
            'status' => 'active',
        ]);
        $user->assignRole($role);

        return $user;
    }

    public function test_install_update_requires_auth_and_admin_role(): void
    {
        // Unauthenticated request is rejected
        $this->postJson('/api/system/install-update', [
            'download_url' => 'https://evil.example.com/payload.exe',
        ])->assertUnauthorized();

        // Non-admin user is rejected with 403
        $cashier = $this->createUser('Cashier', 'cashier-update@example.com');
        Sanctum::actingAs($cashier);

        $this->postJson('/api/system/install-update', [
            'download_url' => 'https://evil.example.com/payload.exe',
        ])->assertForbidden();

        $this->getJson('/api/system/update-progress')->assertForbidden();
    }

    public function test_sqlite_import_rejects_old_hardcoded_password_and_requires_app_key(): void
    {
        $admin = $this->createUser('Admin', 'admin-sqlite@example.com', 'AdminSecret!2026');
        Sanctum::actingAs($admin);

        // Old hardcoded password must be rejected
        $this->postJson('/api/settings/sqlite-import/unlock', [
            'password' => 'Malik9196.',
        ])->assertUnprocessable();

        // Admin's actual password unlocks the gate
        $this->postJson('/api/settings/sqlite-import/unlock', [
            'password' => 'AdminSecret!2026',
        ])->assertOk()->assertJsonPath('ok', true);

        // Empty app.key must fail closed instead of using a hardcoded fallback secret
        $originalKey = config('app.key');
        config(['app.key' => '']);
        try {
            $controller = app(SqliteImportController::class);
            $this->expectException(\RuntimeException::class);
            $controller->generateSoftwareAdminToken($admin->id);
        } finally {
            config(['app.key' => $originalKey]);
        }
    }

    public function test_server_launch_does_not_bypass_on_localhost_or_get_query_param(): void
    {
        $this->createUser('Admin', 'admin-launch@example.com');

        $keyFile = storage_path('app/server-launch.key');
        $hadKeyFile = is_file($keyFile);
        $originalContent = $hadKeyFile ? file_get_contents($keyFile) : null;
        file_put_contents($keyFile, 'test-launch-secret-123');

        try {
            // GET /server-launch?key=... must NOT mint a token (renders POST form instead)
            $getRes = $this->withServerVariables(['REMOTE_ADDR' => '127.0.0.1'])
                ->get('/server-launch?key=test-launch-secret-123');
            $getRes->assertOk();
            $this->assertStringNotContainsString("localStorage.setItem('token'", $getRes->getContent());
            $this->assertDatabaseCount('personal_access_tokens', 0);

            // POST /server-launch with wrong key from localhost must NOT mint a token
            $badPost = $this->withServerVariables(['REMOTE_ADDR' => '127.0.0.1'])
                ->post('/server-launch', ['key' => 'wrong-key']);
            $badPost->assertRedirect('/server-dashboard');
            $this->assertDatabaseCount('personal_access_tokens', 0);

            // POST /server-launch with valid key mints Admin token
            $goodPost = $this->withServerVariables(['REMOTE_ADDR' => '127.0.0.1'])
                ->post('/server-launch', ['key' => 'test-launch-secret-123']);
            $goodPost->assertOk();
            $this->assertStringContainsString("localStorage.setItem('token'", $goodPost->getContent());
            $this->assertDatabaseCount('personal_access_tokens', 1);
        } finally {
            if ($hadKeyFile) {
                file_put_contents($keyFile, $originalContent);
            } else {
                @unlink($keyFile);
            }
        }
    }

    public function test_diagnostics_and_terminal_config_require_admin_authorization(): void
    {
        $cashier = $this->createUser('Cashier', 'cashier-diag@example.com');
        Sanctum::actingAs($cashier);

        $this->getJson('/api/server/diagnostics')->assertForbidden();
        $this->get('/api/server/terminal-config?name=Counter+2')->assertForbidden();

        // Non-admin user must not receive network_server_token in /api/terminals or /api/settings
        $setting = Setting::create(['store_name' => 'Test Store']);
        $setting->network_server_token = 'KRB-NET-HIDDEN999';
        $setting->save();

        $termRes = $this->getJson('/api/terminals')->assertOk();
        $this->assertNull($termRes->json('server.network_server_token'));
        $this->assertArrayNotHasKey('network_server_token', $setting->toArray());

        // Unprivileged user without customer/sale-invoice permissions cannot mutate doctors
        $this->postJson('/api/doctors', ['name' => 'Dr. Unauthorized'])->assertForbidden();
    }

    public function test_unauthenticated_terminal_ping_without_network_token_does_not_register_terminal(): void
    {
        $setting = Setting::create(['store_name' => 'Test Store']);
        $setting->network_server_token = 'KRB-NET-SECRET1234';
        $setting->terminal_auto_authorize = true;
        $setting->save();

        // Unauthenticated ping without X-Network-Token should succeed as a connectivity check
        // but MUST NOT create a Terminal row in the database
        $this->withHeaders([
            'X-Terminal-Id' => 'TERM-ROGUE-01',
            'X-Terminal-Name' => 'Rogue PC',
        ])->postJson('/api/terminals/ping')->assertOk();

        $this->assertDatabaseMissing('terminals', ['terminal_uid' => 'TERM-ROGUE-01']);

        // Unauthenticated ping WITH valid X-Network-Token registers the terminal
        $this->withHeaders([
            'X-Terminal-Id' => 'TERM-PAIRED-01',
            'X-Terminal-Name' => 'Paired Counter',
            'X-Network-Token' => 'KRB-NET-SECRET1234',
        ])->postJson('/api/terminals/ping')->assertOk();

        $this->assertDatabaseHas('terminals', ['terminal_uid' => 'TERM-PAIRED-01']);
    }
}

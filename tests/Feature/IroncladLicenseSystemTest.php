<?php

namespace Tests\Feature;

use App\Models\License;
use App\Models\User;
use App\Services\LicenseService;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Hash;
use Laravel\Sanctum\Sanctum;
use Spatie\Permission\Models\Role;
use Tests\TestCase;

class IroncladLicenseSystemTest extends TestCase
{
    use RefreshDatabase;

    private string $testSecretKeyRaw;
    private string $testPublicKeyB64u;
    private User $admin;

    protected function setUp(): void
    {
        parent::setUp();

        Role::firstOrCreate(['name' => 'Admin', 'guard_name' => 'sanctum']);
        Role::firstOrCreate(['name' => 'Cashier', 'guard_name' => 'sanctum']);

        $this->admin = User::create([
            'name' => 'Admin User',
            'email' => 'admin@karobar.test',
            'password' => Hash::make('password123'),
            'status' => 'active',
        ]);
        $this->admin->assignRole('Admin');

        // Use a deterministic test-only keypair in memory for PHPUnit tests
        $seed = hash('sha256', 'KAROBAR-CI-TEST-SEED', true);
        $kp = sodium_crypto_sign_seed_keypair($seed);
        $this->testSecretKeyRaw = sodium_crypto_sign_secretkey($kp);
        $this->testPublicKeyB64u = LicenseService::b64uEncode(sodium_crypto_sign_publickey($kp));
        LicenseService::setTestOverrides($this->testPublicKeyB64u, 'KRB-AAAA-1111-2222-3333', 1793000000);
    }

    protected function tearDown(): void
    {
        LicenseService::setTestOverrides(null, null, null);
        parent::tearDown();
    }

    private function makeSignedKey(array $overrides = []): string
    {
        $now = 1793000000;
        $payload = array_merge([
            'iss' => 'Karobar',
            'sub' => 'KarobarApp',
            'ver' => 2,
            'lid' => 'LIC-TEST-001',
            'name' => 'Al-Shifa Pharmacy',
            'Edition' => 'Professional',
            'features' => ['core', 'pos', 'inventory'],
            'seats' => 1,
            'iat' => $now,
            'nbf' => $now - 60,
            'exp' => $now + (365 * 86400),
        ], $overrides);

        $payloadJson = json_encode($payload, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE);
        $sig = sodium_crypto_sign_detached($payloadJson, $this->testSecretKeyRaw);

        return LicenseService::KEY_PREFIX . LicenseService::b64uEncode($sig) . '.' . LicenseService::b64uEncode($payloadJson);
    }

    public function test_pinned_master_public_key_passes_integrity_check_and_ignores_env(): void
    {
        LicenseService::setTestOverrides(null, 'KRB-AAAA-1111-2222-3333', 1793000000);
        config(['license.public_key' => 'attacker_injected_fake_key']);

        $svc = app(LicenseService::class);
        $pubKey = $svc->getPublicKey();

        $this->assertSame(SODIUM_CRYPTO_SIGN_PUBLICKEYBYTES, strlen($pubKey));
        $this->assertSame(LicenseService::MASTER_PUBLIC_KEY, LicenseService::b64uEncode($pubKey));
    }

    public function test_zero_friction_laptop_key_activates_and_auto_locks_to_first_machine(): void
    {
        Sanctum::actingAs($this->admin);

        // Before activation, protected API routes return 402 Payment Required
        $this->getJson('/api/brands')->assertStatus(402);

        // Generate a key on laptop without specifying a machine ID
        $key = $this->makeSignedKey();

        // Paste key with extra whitespace/newlines (simulating WhatsApp/email copy-paste)
        $messyKey = "  \n " . substr($key, 0, 30) . "\n" . substr($key, 30) . "  \n";

        $res = $this->postJson('/api/license/activate', [
            'license_key' => $messyKey,
        ]);

        $res->assertOk()
            ->assertJsonPath('ok', true)
            ->assertJsonPath('payload.name', 'Al-Shifa Pharmacy');

        // Protected route now succeeds
        $this->getJson('/api/brands')->assertOk();

        // Database record is locked to Machine A (KRB-AAAA-1111-2222-3333)
        $rec = License::latest('id')->first();
        $this->assertNotNull($rec);
        $this->assertSame('KRB-AAAA-1111-2222-3333', $rec->machine_hash);
        $this->assertNotEmpty($rec->payload['_seal'] ?? null);
    }

    public function test_cloning_activated_software_and_database_to_second_pc_is_detected_and_revoked(): void
    {
        Sanctum::actingAs($this->admin);

        $key = $this->makeSignedKey();
        $this->postJson('/api/license/activate', ['license_key' => $key])->assertOk();
        $this->getJson('/api/brands')->assertOk();

        // Now simulate copying the entire app + SQLite DB to Machine B
        LicenseService::setTestOverrides($this->testPublicKeyB64u, 'KRB-BBBB-4444-5555-6666', 1793000100);

        // Status immediately reports invalid due to Wrong machine
        $this->getJson('/api/license/status')
            ->assertOk()
            ->assertJsonPath('valid', false)
            ->assertJsonPath('reason', 'Wrong machine');

        // Protected routes immediately return 402
        $this->getJson('/api/brands')->assertStatus(402);

        // Even if the attacker opens SQLite and edits `machine_hash` to match Machine B,
        // the cryptographic row seal (`_seal`) catches the tampering and still blocks access!
        DB::table('licenses')->update(['machine_hash' => 'KRB-BBBB-4444-5555-6666']);

        $this->getJson('/api/license/status')
            ->assertOk()
            ->assertJsonPath('valid', false)
            ->assertJsonPath('reason', 'Wrong machine');

        // Logging in on Machine B automatically deletes the cloned license and notifies the client
        app('auth')->forgetGuards();
        app('auth')->shouldUse('web');
        $loginRes = $this->postJson('/api/login', [
            'email' => 'admin@karobar.test',
            'password' => 'password123',
        ]);

        $loginRes->assertOk()
            ->assertJsonPath('license_revoked', true);

        $this->assertDatabaseCount('licenses', 0);
    }

    public function test_forged_key_is_rejected_and_does_not_overwrite_existing_valid_license(): void
    {
        Sanctum::actingAs($this->admin);

        // First install a valid license
        $validKey = $this->makeSignedKey();
        $this->postJson('/api/license/activate', ['license_key' => $validKey])->assertOk();

        // Attacker generates their own Ed25519 keypair and signs a fake lifetime license
        $attackerKp = sodium_crypto_sign_keypair();
        $attackerSec = sodium_crypto_sign_secretkey($attackerKp);
        $fakePayload = json_encode([
            'iss' => 'Karobar',
            'sub' => 'KarobarApp',
            'ver' => 2,
            'name' => 'Pirated Store',
            'exp' => null,
        ]);
        $fakeSig = sodium_crypto_sign_detached($fakePayload, $attackerSec);
        $forgedKey = 'KRBL.' . LicenseService::b64uEncode($fakeSig) . '.' . LicenseService::b64uEncode($fakePayload);

        // Forged key is rejected with 422 Bad signature
        $this->postJson('/api/license/activate', ['license_key' => $forgedKey])
            ->assertStatus(422)
            ->assertJsonPath('ok', false)
            ->assertJsonPath('reason', 'Bad signature');

        // Existing valid license remains intact and active!
        $this->assertDatabaseCount('licenses', 1);
        $this->getJson('/api/license/status')
            ->assertOk()
            ->assertJsonPath('valid', true)
            ->assertJsonPath('payload.name', 'Al-Shifa Pharmacy');
    }

    public function test_system_clock_rollback_is_detected_and_blocked(): void
    {
        Sanctum::actingAs($this->admin);

        $key = $this->makeSignedKey([
            'iat' => 1793000000,
            'nbf' => 1793000000,
            'exp' => 1795592000, // 30 days later
        ]);

        // Activate at t = 1793000000
        $this->postJson('/api/license/activate', ['license_key' => $key])->assertOk();

        // Advance time by 10 days (t = 1793864000) and verify status updates High-Water Mark
        LicenseService::setTestOverrides($this->testPublicKeyB64u, 'KRB-AAAA-1111-2222-3333', 1793864000);
        $this->getJson('/api/license/status')
            ->assertOk()
            ->assertJsonPath('valid', true);

        // User rolls system clock backward by 5 days (t = 1793432000)
        LicenseService::setTestOverrides($this->testPublicKeyB64u, 'KRB-AAAA-1111-2222-3333', 1793432000);

        $this->getJson('/api/license/status')
            ->assertOk()
            ->assertJsonPath('valid', false)
            ->assertJsonPath('reason', 'System clock tampering detected. Please restore the correct date and time.');

        $this->getJson('/api/brands')->assertStatus(402);
    }

    public function test_pre_bound_machine_license_only_activates_on_target_machine(): void
    {
        Sanctum::actingAs($this->admin);

        $preBoundKey = $this->makeSignedKey([
            'machine' => 'KRB-9999-8888-7777-6666',
        ]);

        // Fails on Machine A (KRB-AAAA-1111-2222-3333)
        $this->postJson('/api/license/activate', ['license_key' => $preBoundKey])
            ->assertStatus(422)
            ->assertJsonPath('ok', false)
            ->assertJsonPath('reason', 'Wrong machine');

        // Succeeds on target machine KRB-9999-8888-7777-6666
        LicenseService::setTestOverrides($this->testPublicKeyB64u, 'KRB-9999-8888-7777-6666', 1793000000);

        $this->postJson('/api/license/activate', ['license_key' => $preBoundKey])
            ->assertOk()
            ->assertJsonPath('ok', true);
    }

    public function test_lifetime_license_never_expires(): void
    {
        Sanctum::actingAs($this->admin);

        $lifetimeKey = $this->makeSignedKey([
            'exp' => null,
            'Edition' => 'Enterprise',
        ]);

        $this->postJson('/api/license/activate', ['license_key' => $lifetimeKey])->assertOk();

        // Advance time by 10 years
        LicenseService::setTestOverrides($this->testPublicKeyB64u, 'KRB-AAAA-1111-2222-3333', 1793000000 + (10 * 365 * 86400));

        $this->getJson('/api/license/status')
            ->assertOk()
            ->assertJsonPath('valid', true)
            ->assertJsonPath('expires_at', null);
    }
}

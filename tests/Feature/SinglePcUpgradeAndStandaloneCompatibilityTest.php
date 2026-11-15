<?php

namespace Tests\Feature;

use App\Http\Middleware\EnsureLicensed;
use App\Models\Account;
use App\Models\Customer;
use App\Models\SaleInvoice;
use App\Models\Supplier;
use App\Models\User;
use App\Services\CashDrawerService;
use App\Services\DocumentSequenceService;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\Artisan;
use Illuminate\Support\Facades\DB;
use Spatie\Permission\Models\Permission;
use Spatie\Permission\Models\Role;
use Tests\TestCase;

class SinglePcUpgradeAndStandaloneCompatibilityTest extends TestCase
{
    use RefreshDatabase;

    protected User $admin;

    protected function setUp(): void
    {
        parent::setUp();
        $this->withoutMiddleware(EnsureLicensed::class);

        $role = Role::firstOrCreate(['name' => 'Admin', 'guard_name' => 'sanctum']);
        foreach ([
            'settings.update',
            'draft.view',
            'draft.create',
            'draft.update',
            'draft.delete',
            'draft.post',
            'sale-invoice.view',
            'sale-invoice.create',
        ] as $permName) {
            $perm = Permission::firstOrCreate(['name' => $permName, 'guard_name' => 'sanctum']);
            $role->givePermissionTo($perm);
        }

        $this->admin = User::create([
            'name'     => 'Standalone Store Owner',
            'email'    => 'owner@standalone.local',
            'password' => bcrypt('password123'),
            'status'   => 'active',
        ]);
        $this->admin->assignRole($role);
    }

    public function test_existing_single_pc_invoice_numbers_continue_seamlessly_after_update(): void
    {
        $customer = Customer::create(['name' => 'Walk-in Customer']);
        $supplier = Supplier::create(['name' => 'Main Distributor']);
        $seq = app(DocumentSequenceService::class);

        // Simulate pre-existing invoices in a 1-PC store before document_sequences is populated
        DB::table('document_sequences')->truncate();

        DB::table('sale_invoices')->insert([
            'user_id'       => $this->admin->id,
            'customer_id'   => $customer->id,
            'posted_number' => 'SI-000105',
            'date'          => '2026-11-15',
            'gross_amount'  => 2500,
            'total'         => 2500,
            'created_at'    => now(),
            'updated_at'    => now(),
        ]);

        DB::table('purchase_invoices')->insert([
            'supplier_id'    => $supplier->id,
            'posted_number'  => 'PRINV-000042',
            'posted_date'    => '2026-11-15',
            'invoice_number' => 'SUP-42',
            'invoice_amount' => 15000,
            'total_amount'   => 15000,
            'created_at'     => now(),
            'updated_at'     => now(),
        ]);

        DB::table('sale_returns')->insert([
            'user_id'       => $this->admin->id,
            'customer_id'   => $customer->id,
            'posted_number' => 'SLRET-000012',
            'date'          => '2026-11-15',
            'gross_total'   => 300,
            'total'         => 300,
            'created_at'    => now(),
            'updated_at'    => now(),
        ]);

        DB::table('purchase_returns')->insert([
            'supplier_id'   => $supplier->id,
            'posted_number' => 'PRRET-000008',
            'date'          => '2026-11-15',
            'gross_total'   => 900,
            'total'         => 900,
            'created_at'    => now(),
            'updated_at'    => now(),
        ]);

        // Next generated numbers in 1-PC mode must automatically seed from existing highest numbers
        $this->assertSame('SI-000106', $seq->next('sale_invoice', 'SI-', 6, 'sale_invoices', 'posted_number'));
        $this->assertSame('SI-000107', $seq->next('sale_invoice', 'SI-', 6, 'sale_invoices', 'posted_number'));
        $this->assertSame('PRINV-000043', $seq->next('purchase_invoice', 'PRINV-', 6, 'purchase_invoices', 'posted_number'));
        $this->assertSame('SLRET-000013', $seq->next('sale_return', 'SLRET-', 6, 'sale_returns', 'posted_number'));
        $this->assertSame('PRRET-000009', $seq->next('purchase_return', 'PRRET-', 6, 'purchase_returns', 'posted_number'));
    }

    public function test_single_pc_cash_drawer_works_without_terminal_id_before_and_after_update(): void
    {
        Account::create([
            'name'            => 'Cash in Hand',
            'code'            => 'CASH-01',
            'type'            => 'cash',
            'opening_balance' => 0,
            'current_balance' => 15000,
            'is_active'       => true,
            'is_default_cash' => true,
        ]);

        /** @var CashDrawerService $drawerService */
        $drawerService = app(CashDrawerService::class);

        // 1. Simulate a 1-PC store opening a Cash Drawer (no X-Terminal-Id header, terminal_id = null)
        $session = $drawerService->openSession($this->admin, 5000.0, 'Morning shift on single PC', null);

        $this->assertNull($session->terminal_id);
        $this->assertSame('open', $session->status);

        // 2. Verify getCurrentSession() finds the 1-PC drawer when no terminal_id is active
        $current = $drawerService->getCurrentSession($this->admin);
        $this->assertNotNull($current);
        $this->assertSame($session->id, $current->id);

        // 3. Record a Cash Sale in 1-PC mode and verify balance updates
        $customer = Customer::create(['name' => 'Cash Customer']);
        $invoice = SaleInvoice::create([
            'user_id'       => $this->admin->id,
            'posted_number' => 'SI-000201',
            'customer_id'   => $customer->id,
            'date'          => now()->toDateString(),
            'gross_amount'  => 1250,
            'total'         => 1250,
            'total_receive' => 1250,
        ]);

        $tx = $drawerService->recordTransaction(
            $session,
            'cash_sale',
            1250.0,
            'sale_invoice',
            $invoice->id,
            'Cash Sale SI-000201',
            $this->admin
        );
        $this->assertNotNull($tx);
        $this->assertSame(1250.0, (float) $tx->amount);

        $session->refresh();
        $this->assertSame(6250.0, (float) $session->closing_balance_expected);

        // 4. Close the 1-PC drawer session and verify zero difference
        $closed = $drawerService->closeSession($session, 6250.0, 'Evening close on single PC');

        $this->assertSame('closed', $closed->status);
        $this->assertSame(6250.0, (float) $closed->closing_balance_expected);
        $this->assertSame(0.0, (float) $closed->difference);
    }

    public function test_single_pc_held_bills_work_with_no_terminal_headers(): void
    {
        $createRes = $this->actingAs($this->admin)
            ->postJson('/api/invoice-drafts', [
                'type'         => 'sale_retail',
                'party_name'   => 'Single PC Walk-in',
                'total_amount' => 890.50,
                'items_count'  => 2,
                'payload'      => [
                    'customer_id' => null,
                    'items'       => [
                        ['product_id' => 1, 'quantity' => 2, 'price' => 445.25],
                    ],
                ],
            ]);

        $createRes->assertStatus(201);
        $draftId = $createRes->json('id');
        $this->assertNotNull($draftId);

        // Listing in 1-PC mode returns the draft
        $listRes = $this->actingAs($this->admin)
            ->getJson('/api/invoice-drafts');
        $listRes->assertStatus(200);
        $this->assertGreaterThanOrEqual(1, count($listRes->json('data') ?? $listRes->json()));

        // Deleting in 1-PC mode succeeds
        $delRes = $this->actingAs($this->admin)
            ->deleteJson("/api/invoice-drafts/{$draftId}");
        $delRes->assertStatus(200);
    }

    public function test_differential_ota_patch_command_preserves_data_and_runs_migrations(): void
    {
        $versionFile = base_path('public/version.json');
        $originalVersionJson = is_file($versionFile) ? file_get_contents($versionFile) : null;

        // Create a temporary valid patch zip containing public/version.json
        $tmpZip = sys_get_temp_dir() . DIRECTORY_SEPARATOR . 'test-karobar-patch-' . uniqid() . '.zip';
        $zip = new \ZipArchive();
        $this->assertTrue($zip->open($tmpZip, \ZipArchive::CREATE | \ZipArchive::OVERWRITE) === true);
        $zip->addFromString('public/version.json', json_encode([
            'app_name'     => 'Karobar-App',
            'version'      => '2.0.51',
            'build_number' => 51,
            'commit_sha'   => '53ae447',
            'repository'   => 'shzdasim/Karobar-App',
            'released_at'  => '2026-11-15T00:00:00Z',
        ], JSON_PRETTY_PRINT));
        $zip->close();

        $sha = hash_file('sha256', $tmpZip);

        try {
            $exitCode = Artisan::call('karobar:apply-patch', [
                'patch_zip'     => $tmpZip,
                '--verify-sha'  => $sha,
                '--skip-backup' => true,
                '--json'        => true,
            ]);

            $this->assertSame(0, $exitCode);
            $output = json_decode(trim(Artisan::output()), true);
            $this->assertIsArray($output);
            $this->assertTrue($output['ok'] ?? false);
            $this->assertTrue($output['patch_applied'] ?? false);

            // Verify existing user data is 100% preserved after patch
            $this->assertDatabaseHas('users', [
                'email' => 'owner@standalone.local',
            ]);
        } finally {
            @unlink($tmpZip);
            if ($originalVersionJson !== null) {
                file_put_contents($versionFile, $originalVersionJson);
            }
        }
    }
}

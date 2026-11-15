<?php

namespace Tests\Feature;

use App\Http\Middleware\EnsureLicensed;
use App\Models\Account;
use App\Models\Customer;
use App\Models\SaleInvoice;
use App\Models\Terminal;
use App\Models\User;
use App\Services\CashDrawerService;
use App\Services\DocumentSequenceService;
use App\Services\SqliteExportService;
use App\Services\SqliteImportService;
use App\Services\SyncPulseService;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\DB;
use Spatie\Permission\Models\Permission;
use Spatie\Permission\Models\Role;
use Tests\TestCase;

class MultiTerminalSyncAndConcurrencyTest extends TestCase
{
    use RefreshDatabase;

    protected function setUp(): void
    {
        parent::setUp();
        $this->withoutMiddleware(EnsureLicensed::class);
    }

    protected function createAuthorizedUser(string $name = 'Cashier One', string $email = 'cashier1@example.com'): User
    {
        $user = User::create([
            'name' => $name,
            'email' => $email,
            'password' => bcrypt('password'),
            'status' => 'active',
        ]);

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
        $user->assignRole($role);

        return $user;
    }

    public function test_document_sequence_service_allocates_monotonic_codes_and_seeds_from_existing_records(): void
    {
        $user = $this->createAuthorizedUser();
        $customer = Customer::create(['name' => 'Walk-in Customer']);
        $seq = app(DocumentSequenceService::class);

        // Preview before any allocation starts at 1
        $this->assertSame('SI-000001', $seq->preview('sale_invoice', 'SI-', 6, 'sale_invoices', 'posted_number'));

        // Insert a pre-existing invoice with SI-000042 to verify automatic seeding from existing table
        DB::table('sale_invoices')->insert([
            'user_id' => $user->id,
            'customer_id' => $customer->id,
            'posted_number' => 'SI-000042',
            'date' => '2026-11-15',
            'gross_amount' => 1000,
            'total' => 1000,
            'created_at' => now(),
            'updated_at' => now(),
        ]);

        $allocated1 = $seq->next('sale_invoice', 'SI-', 6, 'sale_invoices', 'posted_number');
        $allocated2 = $seq->next('sale_invoice', 'SI-', 6, 'sale_invoices', 'posted_number');
        $allocated3 = $seq->next('sale_invoice', 'SI-', 6, 'sale_invoices', 'posted_number');

        $this->assertSame('SI-000043', $allocated1);
        $this->assertSame('SI-000044', $allocated2);
        $this->assertSame('SI-000045', $allocated3);

        // Verify other document sequence types allocate cleanly without collision
        $this->assertSame('SLRET-000001', $seq->next('sale_return', 'SLRET-', 6, 'sale_returns', 'posted_number'));
        $this->assertSame('PRINV-000001', $seq->next('purchase_invoice', 'PRINV-', 6, 'purchase_invoices', 'posted_number'));
        $this->assertSame('PRRET-000001', $seq->next('purchase_return', 'PRRET-', 6, 'purchase_returns', 'posted_number'));
        $this->assertSame('STADJ-000001', $seq->next('stock_adjustment', 'STADJ-', 6, 'stock_adjustments', 'posted_number'));
        $this->assertSame('CR-20261115-0001', $seq->next('payment:CR:20261115', 'CR-20261115-', 4, 'payments', 'payment_number'));
        $this->assertSame('EXP-20261115-0001', $seq->next('expense:20261115', 'EXP-20261115-', 4, 'expenses', 'expense_number'));
        $this->assertSame('JV-20261115-0001', $seq->next('journal_entry:20261115', 'JV-20261115-', 4, 'journal_entries', 'entry_number'));
        $this->assertSame('DSP-20261115-0001', $seq->next('stock_disposal:20261115', 'DSP-20261115-', 4, 'stock_disposals', 'disposal_number'));
        $this->assertSame('ST-2026-0001', $seq->next('stocktake:2026', 'ST-2026-', 4, 'stocktakes', 'reference_no'));
    }

    public function test_terminal_auto_registration_and_authorization_blocking(): void
    {
        $user = $this->createAuthorizedUser();

        // Ping from Terminal 1 auto-registers the terminal
        $res1 = $this->actingAs($user)
            ->withHeaders([
                'X-Terminal-Id' => 'TERM-COUNTER-01',
                'X-Terminal-Name' => 'Counter 1 POS',
                'X-Terminal-Mode' => 'terminal',
                'X-App-Version' => '2.4.0',
            ])
            ->postJson('/api/terminals/ping');

        $res1->assertOk()
            ->assertJsonPath('ok', true)
            ->assertJsonPath('terminal.terminal_uid', 'TERM-COUNTER-01')
            ->assertJsonPath('terminal.name', 'Counter 1 POS')
            ->assertJsonPath('terminal.mode', 'terminal');

        $terminal = Terminal::where('terminal_uid', 'TERM-COUNTER-01')->firstOrFail();
        $this->assertTrue((bool) $terminal->is_authorized);

        // Block Terminal 1 via the Server/Terminals controller
        $blockRes = $this->actingAs($user)
            ->putJson("/api/terminals/{$terminal->id}", [
                'is_authorized' => false,
                'name' => 'Counter 1 POS (Blocked)',
            ]);
        $blockRes->assertOk();

        // Subsequent requests from blocked terminal are rejected with 403
        $blockedPing = $this->actingAs($user)
            ->withHeaders([
                'X-Terminal-Id' => 'TERM-COUNTER-01',
                'X-Terminal-Name' => 'Counter 1 POS (Blocked)',
            ])
            ->getJson('/api/sync/pulse');

        $blockedPing->assertStatus(403)
            ->assertJsonPath('code', 'TERMINAL_UNAUTHORIZED');
    }

    public function test_per_terminal_cash_drawer_sessions_operate_independently_and_reconcile_together(): void
    {
        $user1 = $this->createAuthorizedUser('Cashier 1', 'c1@example.com');
        $user2 = $this->createAuthorizedUser('Cashier 2', 'c2@example.com');
        $customer = Customer::create(['name' => 'Walk-in Customer']);

        $term1 = Terminal::create([
            'terminal_uid' => 'TERM-01',
            'name' => 'Counter 1',
            'mode' => 'terminal',
            'is_authorized' => true,
        ]);

        $term2 = Terminal::create([
            'terminal_uid' => 'TERM-02',
            'name' => 'Counter 2',
            'mode' => 'terminal',
            'is_authorized' => true,
        ]);

        Account::create([
            'name' => 'Cash in Hand',
            'code' => 'CASH-01',
            'type' => 'cash',
            'opening_balance' => 0,
            'current_balance' => 15000,
            'is_active' => true,
            'is_default_cash' => true,
        ]);

        $drawerService = app(CashDrawerService::class);

        // Open Counter 1 Cash Drawer with 5,000
        $session1 = $drawerService->openSession($user1, 5000.0, 'Counter 1 Morning Shift', $term1->id);

        // Open Counter 2 Cash Drawer simultaneously with 10,000
        $session2 = $drawerService->openSession($user2, 10000.0, 'Counter 2 Morning Shift', $term2->id);

        $this->assertNotEquals($session1->id, $session2->id);
        $this->assertEquals($term1->id, $session1->terminal_id);
        $this->assertEquals($term2->id, $session2->terminal_id);

        // Record a cash sale of 1,200 on Counter 1
        $invoice1 = SaleInvoice::create([
            'user_id' => $user1->id,
            'customer_id' => $customer->id,
            'terminal_id' => $term1->id,
            'posted_number' => 'SI-000101',
            'date' => '2026-11-15',
            'gross_amount' => 1200,
            'total' => 1200,
            'total_receive' => 1200,
        ]);
        $drawerService->recordTransaction(
            $session1,
            'cash_sale',
            1200.0,
            'sale_invoice',
            $invoice1->id,
            'Cash Sale SI-000101',
            $user1
        );

        // Record a cash sale of 800 on Counter 2
        $invoice2 = SaleInvoice::create([
            'user_id' => $user2->id,
            'customer_id' => $customer->id,
            'terminal_id' => $term2->id,
            'posted_number' => 'SI-000102',
            'date' => '2026-11-15',
            'gross_amount' => 800,
            'total' => 800,
            'total_receive' => 800,
        ]);
        $drawerService->recordTransaction(
            $session2,
            'cash_sale',
            800.0,
            'sale_invoice',
            $invoice2->id,
            'Cash Sale SI-000102',
            $user2
        );

        $session1->refresh();
        $session2->refresh();

        $this->assertEquals(6200.0, (float) $session1->closing_balance_expected);
        $this->assertEquals(10800.0, (float) $session2->closing_balance_expected);
    }

    public function test_shared_held_bills_park_at_counter_1_and_recall_at_counter_2(): void
    {
        $cashier1 = $this->createAuthorizedUser('Counter 1 Cashier', 'counter1@example.com');
        $cashier2 = $this->createAuthorizedUser('Counter 2 Cashier', 'counter2@example.com');

        // Counter 1 parks a held bill
        $createRes = $this->actingAs($cashier1)
            ->withHeaders([
                'X-Terminal-Id' => 'TERM-COUNTER-1',
                'X-Terminal-Name' => 'Counter 1',
            ])
            ->postJson('/api/invoice-drafts', [
                'type' => 'sale_retail',
                'party_name' => 'Walk-in Patient',
                'total_amount' => 2450,
                'items_count' => 3,
                'payload' => [
                    'customer_id' => null,
                    'items' => [
                        ['product_id' => 1, 'quantity' => 3, 'price' => 816.67],
                    ],
                ],
            ]);

        $createRes->assertStatus(201);
        $draftId = $createRes->json('id');
        $this->assertEquals('Counter 1', $createRes->json('terminal_name'));

        // Counter 2 lists active held bills and sees Counter 1's parked bill
        $listRes = $this->actingAs($cashier2)
            ->withHeaders([
                'X-Terminal-Id' => 'TERM-COUNTER-2',
                'X-Terminal-Name' => 'Counter 2',
            ])
            ->getJson('/api/invoice-drafts');

        $listRes->assertOk();
        $draftIds = collect($listRes->json())->pluck('id')->all();
        $this->assertContains($draftId, $draftIds);

        // Counter 2 loads the draft to finalize payment
        $showRes = $this->actingAs($cashier2)
            ->withHeaders([
                'X-Terminal-Id' => 'TERM-COUNTER-2',
                'X-Terminal-Name' => 'Counter 2',
            ])
            ->getJson("/api/invoice-drafts/{$draftId}");

        $showRes->assertOk()
            ->assertJsonPath('draft.id', $draftId)
            ->assertJsonPath('draft.terminal_name', 'Counter 1');
    }

    public function test_sync_pulse_increments_and_sqlite_portable_export_roundtrips(): void
    {
        $user = $this->createAuthorizedUser();

        $initialVersions = SyncPulseService::getVersions();
        $this->assertEquals(1, $initialVersions['settings_version']);

        // Mutating API request bumps SyncPulse version
        $this->actingAs($user)
            ->withHeaders([
                'X-Terminal-Id' => 'TERM-SERVER',
                'X-Terminal-Name' => 'Main Server PC',
            ])
            ->putJson('/api/terminals/settings/network', [
                'terminal_auto_authorize' => true,
            ])
            ->assertOk();

        $afterVersions = SyncPulseService::getVersions();
        $this->assertGreaterThan($initialVersions['updated_at_ms'], $afterVersions['updated_at_ms']);

        // Test Portable SQLite Export Service creates a valid SQLite file with schema and rows
        $exportService = app(SqliteExportService::class);
        $sqlitePath = $exportService->exportToSqliteFile();

        $this->assertFileExists($sqlitePath);
        $this->assertGreaterThan(4096, filesize($sqlitePath));

        // Verify exported SQLite file can be staged and inspected by SqliteImportService
        $importService = app(SqliteImportService::class);
        $staged = $importService->stageSqliteFromFile($sqlitePath);
        $inspection = $importService->inspectAndProposeMapping($staged['token']);
        $this->assertGreaterThan(0, $inspection['summary']['importable_tables_count']);

        @unlink($sqlitePath);
    }

    public function test_server_diagnostics_troubleshooter_and_terminal_config_generator(): void
    {
        $user = $this->createAuthorizedUser();

        // 1. Live Server Diagnostics suite returns 6 health checks
        $diagRes = $this->actingAs($user)->getJson('/api/server/diagnostics');
        $diagRes->assertOk()
            ->assertJsonPath('ok', true);
        $this->assertCount(6, $diagRes->json('checks'));

        // 2. Self-healing Troubleshooter actions execute cleanly
        $repairRes = $this->actingAs($user)->postJson('/api/server/troubleshoot', [
            'action' => 'resync_sequences',
        ]);
        $repairRes->assertOk()
            ->assertJsonPath('ok', true)
            ->assertJsonPath('action', 'resync_sequences');

        $pulseRes = $this->actingAs($user)->postJson('/api/server/troubleshoot', [
            'action' => 'force_sync_pulse',
        ]);
        $pulseRes->assertOk()
            ->assertJsonPath('ok', true);

        // 3. Pre-configured Terminal Config (.json) download for rapid counter setup
        $cfgRes = $this->actingAs($user)->get('/api/server/terminal-config?name=Counter+3');
        $cfgRes->assertOk();
        $decoded = json_decode($cfgRes->streamedContent(), true);
        $this->assertSame('terminal', $decoded['mode']);
        $this->assertSame('Counter 3', $decoded['terminalName']);
        $this->assertNotEmpty($decoded['serverUrl']);
    }
}

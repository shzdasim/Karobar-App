<?php

namespace Tests\Feature;

use App\Models\BackupLog;
use App\Models\BackupSchedule;
use App\Models\User;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\Artisan;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\File;
use Spatie\Permission\Models\Permission;
use Spatie\Permission\Models\Role;
use Tests\TestCase;

class AutomatedBackupAndOffsiteStorageTest extends TestCase
{
    use RefreshDatabase;

    protected User $adminUser;

    protected function setUp(): void
    {
        parent::setUp();

        $this->withoutMiddleware(\App\Http\Middleware\EnsureLicensed::class);

        // Seed roles & permissions with sanctum guard
        $role = Role::firstOrCreate(['name' => 'Admin', 'guard_name' => 'sanctum']);
        $perms = ['backup.view', 'backup.create', 'backup.restore', 'backup.delete', 'backup.upload'];
        foreach ($perms as $p) {
            $perm = Permission::firstOrCreate(['name' => $p, 'guard_name' => 'sanctum']);
            $role->givePermissionTo($perm);
        }

        $this->adminUser = User::forceCreate([
            'name' => 'Admin User',
            'email' => 'admin@karobar.test',
            'password' => bcrypt('password123'),
        ]);
        $this->adminUser->assignRole($role);
    }

    public function test_can_fetch_backup_schedule_and_stats(): void
    {
        $response = $this->actingAs($this->adminUser)
            ->getJson('/api/backups/schedule');

        $response->assertStatus(200)
            ->assertJsonStructure([
                'schedule' => [
                    'id',
                    'is_automated',
                    'frequency',
                    'time_of_day',
                    'custom_backup_path',
                    'storage_destination',
                ],
            ]);
    }

    public function test_can_update_backup_schedule_settings(): void
    {
        $testPath = sys_get_temp_dir() . '/karobar_test_backups_' . time();

        $payload = [
            'is_automated' => true,
            'frequency' => 'weekly',
            'time_of_day' => '03:30',
            'day_of_week' => 1,
            'day_of_month' => 1,
            'backup_type' => 'database',
            'retention_days' => 45,
            'max_backups_keep' => 15,
            'storage_destination' => 'custom_folder',
            'custom_backup_path' => $testPath,
            'notify_on_failure' => true,
            'notification_email' => 'alerts@karobar.test',
        ];

        $response = $this->actingAs($this->adminUser)
            ->postJson('/api/backups/schedule', $payload);

        $response->assertStatus(200)
            ->assertJson([
                'message' => 'Backup schedule and storage settings updated successfully',
            ]);

        $this->assertDatabaseHas('backup_schedules', [
            'is_automated' => true,
            'frequency' => 'weekly',
            'time_of_day' => '03:30',
            'storage_destination' => 'custom_folder',
            'custom_backup_path' => $testPath,
            'retention_days' => 45,
            'max_backups_keep' => 15,
        ]);
    }

    public function test_can_test_local_storage_destination(): void
    {
        $response = $this->actingAs($this->adminUser)
            ->postJson('/api/backups/test-destination', [
                'destination' => 'local',
            ]);

        $response->assertStatus(200)
            ->assertJson([
                'success' => true,
                'writable' => true,
            ]);
    }

    public function test_can_test_custom_folder_storage_destination(): void
    {
        $tempDir = sys_get_temp_dir() . '/karobar_folder_test_' . uniqid();

        $response = $this->actingAs($this->adminUser)
            ->postJson('/api/backups/test-destination', [
                'destination' => 'custom_folder',
                'custom_backup_path' => $tempDir,
            ]);

        $response->assertStatus(200)
            ->assertJson([
                'success' => true,
                'path' => $tempDir,
                'writable' => true,
            ]);

        // Clean up
        if (File::exists($tempDir)) {
            File::deleteDirectory($tempDir);
        }
    }

    public function test_can_browse_directories(): void
    {
        $response = $this->actingAs($this->adminUser)
            ->getJson('/api/backups/browse-directories');

        $response->assertStatus(200)
            ->assertJsonStructure([
                'current_path',
                'shortcuts',
                'directories',
            ]);
    }

    public function test_can_create_directory(): void
    {
        $tempParent = sys_get_temp_dir() . '/karobar_parent_' . uniqid();
        File::makeDirectory($tempParent, 0755, true, true);

        $response = $this->actingAs($this->adminUser)
            ->postJson('/api/backups/create-directory', [
                'parent_path' => $tempParent,
                'folder_name' => 'MyKarobarBackups',
            ]);

        $response->assertStatus(201)
            ->assertJson([
                'message' => 'Folder created successfully.',
            ]);

        $this->assertDirectoryExists($tempParent . DIRECTORY_SEPARATOR . 'MyKarobarBackups');

        File::deleteDirectory($tempParent);
    }

    public function test_scheduled_artisan_command_respects_disabled_state_unless_forced(): void
    {
        $schedule = BackupSchedule::getSchedule();
        $schedule->update(['is_automated' => false]);

        $exitCode = Artisan::call('backup:run-scheduled');
        $this->assertEquals(0, $exitCode);

        // Forced execution should run
        $forceExit = Artisan::call('backup:run-scheduled', ['--force' => true]);
        $this->assertEquals(0, $forceExit);

        $this->assertDatabaseHas('backup_logs', [
            'is_automated' => true,
            'status' => 'completed',
        ]);
    }

    public function test_backup_is_copied_to_custom_folder_when_configured(): void
    {
        $customDir = sys_get_temp_dir() . '/karobar_custom_archive_test_' . uniqid();
        File::makeDirectory($customDir, 0755, true, true);

        $schedule = BackupSchedule::getSchedule();
        $schedule->update([
            'is_automated' => true,
            'storage_destination' => 'custom_folder',
            'custom_backup_path' => $customDir,
        ]);

        $exitCode = Artisan::call('backup:run-scheduled', ['--force' => true]);
        $this->assertEquals(0, $exitCode);

        $backup = BackupLog::where('is_automated', true)
            ->where('status', 'completed')
            ->orderByDesc('id')
            ->first();

        $this->assertNotNull($backup);
        $this->assertEquals('custom_folder', $backup->storage_destination);
        $this->assertEquals('success', $backup->remote_status);
        $this->assertNotNull($backup->remote_path);
        $this->assertFileExists($backup->remote_path);

        // Verify the file is in our customDir
        $this->assertStringStartsWith($customDir, $backup->remote_path);

        // Clean up
        if (File::exists($customDir)) {
            File::deleteDirectory($customDir);
        }
    }

    public function test_backup_restore_actually_reverts_database_changes(): void
    {
        // 1. Create a database backup
        $response = $this->actingAs($this->adminUser)
            ->postJson('/api/backups', [
                'type' => 'database',
            ]);

        $response->assertStatus(201);
        $backupId = $response->json('backup.id');

        $initialCustomerCount = \App\Models\Customer::count();

        // 2. Perform an action that mutates the database (create a new customer)
        \App\Models\Customer::forceCreate([
            'name' => 'Newly Added Customer After Backup',
            'phone' => '03009999999',
            'address' => 'Test Address',
        ]);

        $this->assertEquals($initialCustomerCount + 1, \App\Models\Customer::count());

        // 3. Restore from the backup taken before the new customer was created
        $restoreResponse = $this->actingAs($this->adminUser)
            ->postJson("/api/backups/{$backupId}/restore", [
                'password' => 'password123',
            ]);

        $restoreResponse->assertStatus(200);

        // 4. Verify that the newly added customer was reverted and database is back to initial count
        $this->assertEquals($initialCustomerCount, \App\Models\Customer::count());
        $this->assertDatabaseMissing('customers', [
            'name' => 'Newly Added Customer After Backup',
        ]);
    }
}

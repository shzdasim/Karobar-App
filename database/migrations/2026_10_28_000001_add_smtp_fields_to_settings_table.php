<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::table('settings', function (Blueprint $table) {
            if (!Schema::hasColumn('settings', 'smtp_enabled')) {
                $table->boolean('smtp_enabled')->default(false);
            }
            if (!Schema::hasColumn('settings', 'smtp_host')) {
                $table->string('smtp_host', 255)->nullable();
            }
            if (!Schema::hasColumn('settings', 'smtp_port')) {
                $table->unsignedInteger('smtp_port')->nullable()->default(587);
            }
            if (!Schema::hasColumn('settings', 'smtp_username')) {
                $table->string('smtp_username', 255)->nullable();
            }
            if (!Schema::hasColumn('settings', 'smtp_password')) {
                $table->text('smtp_password')->nullable();
            }
            if (!Schema::hasColumn('settings', 'smtp_encryption')) {
                $table->string('smtp_encryption', 20)->nullable()->default('tls');
            }
            if (!Schema::hasColumn('settings', 'smtp_from_address')) {
                $table->string('smtp_from_address', 255)->nullable();
            }
            if (!Schema::hasColumn('settings', 'smtp_from_name')) {
                $table->string('smtp_from_name', 255)->nullable();
            }
        });
    }

    public function down(): void
    {
        Schema::table('settings', function (Blueprint $table) {
            $columns = array_filter([
                'smtp_enabled',
                'smtp_host',
                'smtp_port',
                'smtp_username',
                'smtp_password',
                'smtp_encryption',
                'smtp_from_address',
                'smtp_from_name',
            ], fn (string $col) => Schema::hasColumn('settings', $col));

            if (!empty($columns)) {
                $table->dropColumn($columns);
            }
        });
    }
};


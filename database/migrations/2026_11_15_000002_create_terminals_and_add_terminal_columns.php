<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        if (!Schema::hasTable('terminals')) {
            Schema::create('terminals', function (Blueprint $table) {
                $table->id();
                $table->string('terminal_uid', 100)->unique();
                $table->string('name', 120);
                $table->string('hostname', 120)->nullable();
                $table->string('ip_address', 64)->nullable();
                $table->string('os_platform', 64)->nullable();
                $table->string('app_version', 40)->nullable();
                $table->string('mode', 30)->default('terminal'); // standalone, server, terminal, browser
                $table->unsignedBigInteger('current_user_id')->nullable()->index();
                $table->boolean('is_authorized')->default(true)->index();
                $table->timestamp('last_seen_at')->nullable()->index();
                $table->json('meta')->nullable();
                $table->timestamps();
            });
        }

        $tablesNeedTerminalId = [
            'cash_drawer_sessions',
            'cash_drawer_transactions',
            'sale_invoices',
            'sale_returns',
            'payments',
            'expenses',
            'invoice_drafts',
            'document_histories',
        ];

        foreach ($tablesNeedTerminalId as $tableName) {
            if (Schema::hasTable($tableName) && !Schema::hasColumn($tableName, 'terminal_id')) {
                Schema::table($tableName, function (Blueprint $table) {
                    $table->unsignedBigInteger('terminal_id')->nullable()->index();
                });
            }
        }

        if (Schema::hasTable('invoice_drafts') && !Schema::hasColumn('invoice_drafts', 'terminal_name')) {
            Schema::table('invoice_drafts', function (Blueprint $table) {
                $table->string('terminal_name', 120)->nullable();
            });
        }

        if (Schema::hasTable('settings')) {
            Schema::table('settings', function (Blueprint $table) {
                if (!Schema::hasColumn('settings', 'network_server_token')) {
                    $table->string('network_server_token', 120)->nullable();
                }
                if (!Schema::hasColumn('settings', 'terminal_auto_authorize')) {
                    $table->boolean('terminal_auto_authorize')->default(true);
                }
            });
        }
    }

    public function down(): void
    {
        $tablesWithTerminalId = [
            'cash_drawer_sessions',
            'cash_drawer_transactions',
            'sale_invoices',
            'sale_returns',
            'payments',
            'expenses',
            'invoice_drafts',
            'document_histories',
        ];

        foreach ($tablesWithTerminalId as $tableName) {
            if (Schema::hasTable($tableName) && Schema::hasColumn($tableName, 'terminal_id')) {
                Schema::table($tableName, function (Blueprint $table) {
                    $table->dropColumn('terminal_id');
                });
            }
        }

        if (Schema::hasTable('invoice_drafts') && Schema::hasColumn('invoice_drafts', 'terminal_name')) {
            Schema::table('invoice_drafts', function (Blueprint $table) {
                $table->dropColumn('terminal_name');
            });
        }

        Schema::dropIfExists('terminals');
    }
};

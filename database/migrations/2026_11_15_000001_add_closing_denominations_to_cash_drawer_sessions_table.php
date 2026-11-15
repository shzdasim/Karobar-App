<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        if (Schema::hasTable('cash_drawer_sessions') && !Schema::hasColumn('cash_drawer_sessions', 'closing_denominations')) {
            Schema::table('cash_drawer_sessions', function (Blueprint $table) {
                $table->json('closing_denominations')->nullable()->after('difference');
            });
        }
    }

    public function down(): void
    {
        if (Schema::hasTable('cash_drawer_sessions') && Schema::hasColumn('cash_drawer_sessions', 'closing_denominations')) {
            Schema::table('cash_drawer_sessions', function (Blueprint $table) {
                $table->dropColumn('closing_denominations');
            });
        }
    }
};

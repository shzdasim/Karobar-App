<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        if (Schema::hasTable('settings') && !Schema::hasColumn('settings', 'dosage_sticker_config')) {
            Schema::table('settings', function (Blueprint $table) {
                $table->json('dosage_sticker_config')->nullable();
            });
        }
    }

    public function down(): void
    {
        if (Schema::hasTable('settings') && Schema::hasColumn('settings', 'dosage_sticker_config')) {
            Schema::table('settings', function (Blueprint $table) {
                $table->dropColumn('dosage_sticker_config');
            });
        }
    }
};

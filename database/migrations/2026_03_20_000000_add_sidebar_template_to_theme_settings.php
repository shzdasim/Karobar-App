<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    /**
     * Run the migrations.
     */
    public function up(): void
    {
        Schema::table('theme_settings', function (Blueprint $table) {
            $table->string('sidebar_template')->default('classic')->after('button_style');
        });

        // Update existing records to have a sidebar_template value
        \Illuminate\Support\Facades\DB::table('theme_settings')->update(['sidebar_template' => 'classic']);
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::table('theme_settings', function (Blueprint $table) {
            $table->dropColumn('sidebar_template');
        });
    }
};


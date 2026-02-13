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
            $table->string('button_style')->default('rounded')->after('shadow_color');
        });
        
        // Update existing records to have a button_style value
        \Illuminate\Support\Facades\DB::table('theme_settings')->update(['button_style' => 'rounded']);
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::table('theme_settings', function (Blueprint $table) {
            $table->dropColumn('button_style');
        });
    }
};


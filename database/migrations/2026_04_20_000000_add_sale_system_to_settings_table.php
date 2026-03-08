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
        Schema::table('settings', function (Blueprint $table) {
            $table->enum('sale_system', ['retail', 'retail_wholesale'])->default('retail_wholesale')->after('navigation_style');
        });
        
        // Update existing records to have a default value
        \Illuminate\Support\Facades\DB::table('settings')->update(['sale_system' => 'retail_wholesale']);
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::table('settings', function (Blueprint $table) {
            $table->dropColumn('sale_system');
        });
    }
};


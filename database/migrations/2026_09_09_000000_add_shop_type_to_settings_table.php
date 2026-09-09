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
            $table->enum('shop_type', ['pharmacy', 'general_store'])->default('pharmacy')->after('sale_system');
        });

        // Update existing records to have a default value
        \Illuminate\Support\Facades\DB::table('settings')->update(['shop_type' => 'pharmacy']);
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::table('settings', function (Blueprint $table) {
            $table->dropColumn('shop_type');
        });
    }
};
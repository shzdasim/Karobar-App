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
            $table->string('currency_code', 10)->default('PKR')->after('shop_type');
            $table->string('currency_symbol', 15)->default('Rs.')->after('currency_code');
            $table->enum('currency_position', ['before', 'after'])->default('before')->after('currency_symbol');
            $table->unsignedTinyInteger('currency_decimal_places')->default(2)->after('currency_position');
        });
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::table('settings', function (Blueprint $table) {
            $table->dropColumn([
                'currency_code',
                'currency_symbol',
                'currency_position',
                'currency_decimal_places',
            ]);
        });
    }
};


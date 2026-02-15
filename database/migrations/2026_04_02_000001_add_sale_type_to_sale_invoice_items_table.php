<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::table('sale_invoice_items', function (Blueprint $table) {
            $table->enum('sale_type', ['retail', 'wholesale'])->default('retail')->after('sub_total');
            $table->boolean('is_custom_price')->default(false)->after('sale_type');
        });
    }

    public function down(): void
    {
        Schema::table('sale_invoice_items', function (Blueprint $table) {
            $table->dropColumn(['sale_type', 'is_custom_price']);
        });
    }
};


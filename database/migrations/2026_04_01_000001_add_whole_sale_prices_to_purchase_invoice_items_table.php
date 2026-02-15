<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::table('purchase_invoice_items', function (Blueprint $table) {
            $table->decimal('whole_sale_pack_price', 15, 2)->nullable()->after('unit_sale_price');
            $table->decimal('whole_sale_unit_price', 15, 2)->nullable()->after('whole_sale_pack_price');
        });
    }

    public function down(): void
    {
        Schema::table('purchase_invoice_items', function (Blueprint $table) {
            $table->dropColumn(['whole_sale_pack_price', 'whole_sale_unit_price']);
        });
    }
};


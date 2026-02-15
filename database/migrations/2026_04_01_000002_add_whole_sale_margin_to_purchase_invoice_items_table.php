<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::table('purchase_invoice_items', function (Blueprint $table) {
            $table->decimal('whole_sale_margin', 5, 2)->nullable()->after('whole_sale_unit_price');
        });
    }

    public function down(): void
    {
        Schema::table('purchase_invoice_items', function (Blueprint $table) {
            $table->dropColumn('whole_sale_margin');
        });
    }
};


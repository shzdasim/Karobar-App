<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::table('sale_invoices', function (Blueprint $table) {
            $table->enum('sale_type', ['retail', 'wholesale'])->default('retail')->after('invoice_type');
        });
    }

    public function down(): void
    {
        Schema::table('sale_invoices', function (Blueprint $table) {
            $table->dropColumn('sale_type');
        });
    }
};


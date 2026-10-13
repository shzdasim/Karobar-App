<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::table('purchase_orders', function (Blueprint $table) {
            // Historical orders have no known promised date; leave them unscheduled.
            $table->date('expected_received_date')->nullable()->index();
        });
    }

    public function down(): void
    {
        Schema::table('purchase_orders', fn (Blueprint $table) => $table->dropColumn('expected_received_date'));
    }
};

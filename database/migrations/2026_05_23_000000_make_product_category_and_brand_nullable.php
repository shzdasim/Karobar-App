<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        // Make FK columns nullable so deleting brands/categories won't break product inserts.
        Schema::table('products', function (Blueprint $table) {
            // Works for MySQL/Postgres. For SQLite, Laravel may not support dropping NOT NULL
            // without rebuilding the table; we still attempt the standard migration first.
            $table->unsignedBigInteger('category_id')->nullable()->change();
            $table->unsignedBigInteger('brand_id')->nullable()->change();

            // Ensure FK constraints still point to the correct tables
            // (Laravel may keep FK metadata; this is safe to re-declare in most setups).
            $table->foreign('category_id')->references('id')->on('categories')->nullOnDelete();
            $table->foreign('brand_id')->references('id')->on('brands')->nullOnDelete();
        });
    }

    public function down(): void
    {
        Schema::table('products', function (Blueprint $table) {
            $table->unsignedBigInteger('category_id')->nullable(false)->change();
            $table->unsignedBigInteger('brand_id')->nullable(false)->change();
        });
    }
};


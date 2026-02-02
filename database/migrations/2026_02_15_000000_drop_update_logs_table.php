<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    /**
     * Run the migrations.
     */
    public function up(): void
    {
        Schema::dropIfExists('update_logs');
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::create('update_logs', function ($table) {
            $table->id();
            $table->string('type', 50);
            $table->string('level', 20);
            $table->text('message');
            $table->json('context')->nullable();
            $table->timestamps();
            $table->index('type');
            $table->index('level');
            $table->index('created_at');
        });
    }
};


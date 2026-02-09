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
        Schema::create('theme_settings', function (Blueprint $table) {
            $table->id();
            $table->string('name')->default('Default Theme');
            
            // Primary colors (main brand colors)
            $table->string('primary_color')->default('#3b82f6'); // blue-500
            $table->string('primary_hover')->default('#2563eb'); // blue-600
            $table->text('primary_light')->default('#dbeafe'); // blue-100
            
            // Secondary colors (accent colors)
            $table->string('secondary_color')->default('#8b5cf6'); // violet-500
            $table->string('secondary_hover')->default('#7c3aed'); // violet-600
            $table->text('secondary_light')->default('#ede9fe'); // violet-100
            
            // Tertiary colors (complementary colors)
            $table->string('tertiary_color')->default('#06b6d4'); // cyan-500
            $table->string('tertiary_hover')->default('#0891b2'); // cyan-600
            $table->text('tertiary_light')->default('#cffafe'); // cyan-100
            
            // Background and text colors
            $table->string('background_color')->default('#f8fafc'); // slate-50
            $table->string('surface_color')->default('#ffffff'); // white
            $table->string('text_primary')->default('#1e293b'); // slate-800
            $table->string('text_secondary')->default('#64748b'); // slate-500
            
            // Status colors
            $table->string('success_color')->default('#10b981'); // emerald-500
            $table->string('warning_color')->default('#f59e0b'); // amber-500
            $table->string('danger_color')->default('#ef4444'); // red-500
            
            // UI elements
            $table->string('border_color')->default('#e2e8f0'); // slate-200
            $table->string('shadow_color')->default('#1e293b'); // for box-shadows
            
            $table->boolean('is_active')->default(true);
            $table->timestamps();
        });
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::dropIfExists('theme_settings');
    }
};


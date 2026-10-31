<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::table('settings', function (Blueprint $table) {
            if (! Schema::hasColumn('settings', 'cube_ai_enabled')) {
                $table->boolean('cube_ai_enabled')->default(true);
            }
            if (! Schema::hasColumn('settings', 'cube_groq_enabled')) {
                $table->boolean('cube_groq_enabled')->default(true);
            }
            if (! Schema::hasColumn('settings', 'cube_groq_api_key')) {
                $table->text('cube_groq_api_key')->nullable();
            }
            if (! Schema::hasColumn('settings', 'cube_groq_model')) {
                $table->string('cube_groq_model', 120)->nullable()->default('llama-3.3-70b-versatile');
            }
            if (! Schema::hasColumn('settings', 'cube_groq_daily_limit')) {
                $table->unsignedInteger('cube_groq_daily_limit')->nullable()->default(250);
            }
            if (! Schema::hasColumn('settings', 'cube_gemini_enabled')) {
                $table->boolean('cube_gemini_enabled')->default(true);
            }
            if (! Schema::hasColumn('settings', 'cube_gemini_api_key')) {
                $table->text('cube_gemini_api_key')->nullable();
            }
            if (! Schema::hasColumn('settings', 'cube_gemini_model')) {
                $table->string('cube_gemini_model', 120)->nullable()->default('gemini-3.8-flash');
            }
            if (! Schema::hasColumn('settings', 'cube_gemini_daily_limit')) {
                $table->unsignedInteger('cube_gemini_daily_limit')->nullable()->default(250);
            }
        });
    }

    public function down(): void
    {
        Schema::table('settings', function (Blueprint $table) {
            $columns = array_filter([
                'cube_ai_enabled',
                'cube_groq_enabled',
                'cube_groq_api_key',
                'cube_groq_model',
                'cube_groq_daily_limit',
                'cube_gemini_enabled',
                'cube_gemini_api_key',
                'cube_gemini_model',
                'cube_gemini_daily_limit',
            ], fn (string $col) => Schema::hasColumn('settings', $col));

            if (! empty($columns)) {
                $table->dropColumn($columns);
            }
        });
    }
};


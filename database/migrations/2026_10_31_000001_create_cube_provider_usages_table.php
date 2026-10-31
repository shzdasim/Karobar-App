<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::create('cube_provider_usages', function (Blueprint $table) {
            $table->id();
            $table->string('provider', 32);
            $table->date('usage_date');
            $table->unsignedInteger('request_count')->default(0);
            $table->unsignedInteger('success_count')->default(0);
            $table->unsignedInteger('failure_count')->default(0);
            $table->string('status', 64)->default('ready');
            $table->timestamp('cooldown_until')->nullable();
            $table->string('disabled_reason', 191)->nullable();
            $table->string('config_fingerprint', 64)->nullable();
            $table->unsignedSmallInteger('last_http_status')->nullable();
            $table->string('last_error_code', 96)->nullable();
            $table->timestamps();

            $table->unique(['provider', 'usage_date']);
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('cube_provider_usages');
    }
};


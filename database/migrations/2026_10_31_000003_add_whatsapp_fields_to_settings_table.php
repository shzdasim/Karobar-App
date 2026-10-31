<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        Schema::table('settings', function (Blueprint $table) {
            if (! Schema::hasColumn('settings', 'whatsapp_enabled')) {
                $table->boolean('whatsapp_enabled')->default(false);
            }
            if (! Schema::hasColumn('settings', 'whatsapp_provider')) {
                $table->string('whatsapp_provider', 40)->nullable()->default('whapi_cloud');
            }
            if (! Schema::hasColumn('settings', 'whatsapp_access_token')) {
                $table->text('whatsapp_access_token')->nullable();
            }
            if (! Schema::hasColumn('settings', 'whatsapp_phone_number_id')) {
                $table->string('whatsapp_phone_number_id', 120)->nullable();
            }
            if (! Schema::hasColumn('settings', 'whatsapp_business_account_id')) {
                $table->string('whatsapp_business_account_id', 120)->nullable();
            }
            if (! Schema::hasColumn('settings', 'whatsapp_api_version')) {
                $table->string('whatsapp_api_version', 20)->nullable()->default('v21.0');
            }
            if (! Schema::hasColumn('settings', 'whatsapp_api_url')) {
                $table->string('whatsapp_api_url', 500)->nullable();
            }
            if (! Schema::hasColumn('settings', 'whatsapp_default_country_code')) {
                $table->string('whatsapp_default_country_code', 10)->nullable()->default('92');
            }
            if (! Schema::hasColumn('settings', 'whatsapp_template_name')) {
                $table->string('whatsapp_template_name', 120)->nullable();
            }
            if (! Schema::hasColumn('settings', 'whatsapp_template_language')) {
                $table->string('whatsapp_template_language', 20)->nullable()->default('en_US');
            }
            if (! Schema::hasColumn('settings', 'whatsapp_auto_fallback')) {
                $table->boolean('whatsapp_auto_fallback')->default(true);
            }
        });
    }

    public function down(): void
    {
        Schema::table('settings', function (Blueprint $table) {
            $columns = array_filter([
                'whatsapp_enabled',
                'whatsapp_provider',
                'whatsapp_access_token',
                'whatsapp_phone_number_id',
                'whatsapp_business_account_id',
                'whatsapp_api_version',
                'whatsapp_api_url',
                'whatsapp_default_country_code',
                'whatsapp_template_name',
                'whatsapp_template_language',
                'whatsapp_auto_fallback',
            ], fn (string $col) => Schema::hasColumn('settings', $col));

            if (! empty($columns)) {
                $table->dropColumn($columns);
            }
        });
    }
};


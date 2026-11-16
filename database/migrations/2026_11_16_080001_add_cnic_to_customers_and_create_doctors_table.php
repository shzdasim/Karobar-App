<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        if (Schema::hasTable('customers') && !Schema::hasColumn('customers', 'cnic')) {
            Schema::table('customers', function (Blueprint $table) {
                $table->string('cnic', 30)->nullable()->index()->after('phone');
            });
        }

        if (!Schema::hasTable('doctors')) {
            Schema::create('doctors', function (Blueprint $table) {
                $table->id();
                $table->string('name');
                $table->string('pmdc_number', 100)->nullable()->index();
                $table->string('hospital_clinic', 255)->nullable();
                $table->string('specialization', 191)->nullable();
                $table->string('phone', 60)->nullable();
                $table->string('address', 500)->nullable();
                $table->boolean('is_active')->default(true)->index();
                $table->timestamps();
            });
        }

        if (Schema::hasTable('sale_invoices') && !Schema::hasColumn('sale_invoices', 'doctor_id')) {
            Schema::table('sale_invoices', function (Blueprint $table) {
                $table->foreignId('doctor_id')->nullable()->after('customer_id')->constrained('doctors')->nullOnDelete();
            });
        }
    }

    public function down(): void
    {
        if (Schema::hasTable('sale_invoices') && Schema::hasColumn('sale_invoices', 'doctor_id')) {
            Schema::table('sale_invoices', function (Blueprint $table) {
                $table->dropConstrainedForeignId('doctor_id');
            });
        }

        Schema::dropIfExists('doctors');

        if (Schema::hasTable('customers') && Schema::hasColumn('customers', 'cnic')) {
            Schema::table('customers', function (Blueprint $table) {
                $table->dropColumn('cnic');
            });
        }
    }
};

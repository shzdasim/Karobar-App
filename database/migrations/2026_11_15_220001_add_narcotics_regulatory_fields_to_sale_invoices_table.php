<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        if (!Schema::hasTable('sale_invoices')) {
            return;
        }

        Schema::table('sale_invoices', function (Blueprint $table) {
            if (!Schema::hasColumn('sale_invoices', 'doctor_pmdc')) {
                $table->string('doctor_pmdc', 120)->nullable()->after('doctor_name');
            }
            if (!Schema::hasColumn('sale_invoices', 'doctor_hospital')) {
                $table->string('doctor_hospital', 255)->nullable()->after('doctor_pmdc');
            }
            if (!Schema::hasColumn('sale_invoices', 'patient_cnic')) {
                $table->string('patient_cnic', 50)->nullable()->after('patient_name');
            }
            if (!Schema::hasColumn('sale_invoices', 'patient_address')) {
                $table->string('patient_address', 500)->nullable()->after('patient_cnic');
            }
            if (!Schema::hasColumn('sale_invoices', 'prescription_no')) {
                $table->string('prescription_no', 120)->nullable()->after('patient_address');
            }
            if (!Schema::hasColumn('sale_invoices', 'pharmacist_name')) {
                $table->string('pharmacist_name', 180)->nullable()->after('prescription_no');
            }
        });
    }

    public function down(): void
    {
        if (!Schema::hasTable('sale_invoices')) {
            return;
        }

        Schema::table('sale_invoices', function (Blueprint $table) {
            $cols = [
                'doctor_pmdc',
                'doctor_hospital',
                'patient_cnic',
                'patient_address',
                'prescription_no',
                'pharmacist_name',
            ];
            foreach ($cols as $col) {
                if (Schema::hasColumn('sale_invoices', $col)) {
                    $table->dropColumn($col);
                }
            }
        });
    }
};

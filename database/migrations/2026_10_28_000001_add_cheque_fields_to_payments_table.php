<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        if (Schema::hasTable('payments')) {
            Schema::table('payments', function (Blueprint $table) {
                if (!Schema::hasColumn('payments', 'cheque_number')) {
                    $table->string('cheque_number', 100)->nullable()->index()->after('reference');
                }
                if (!Schema::hasColumn('payments', 'bank_name')) {
                    $table->string('bank_name', 150)->nullable()->after('cheque_number');
                }
                if (!Schema::hasColumn('payments', 'cheque_date')) {
                    $table->date('cheque_date')->nullable()->index()->after('bank_name');
                }
                if (!Schema::hasColumn('payments', 'cheque_status')) {
                    $table->string('cheque_status', 30)->nullable()->index()->after('cheque_date');
                }
                if (!Schema::hasColumn('payments', 'cleared_at')) {
                    $table->timestamp('cleared_at')->nullable()->after('cheque_status');
                }
                if (!Schema::hasColumn('payments', 'bounced_at')) {
                    $table->timestamp('bounced_at')->nullable()->after('cleared_at');
                }
            });
        }
    }

    public function down(): void
    {
        if (Schema::hasTable('payments')) {
            Schema::table('payments', function (Blueprint $table) {
                $cols = [];
                foreach (['cheque_number', 'bank_name', 'cheque_date', 'cheque_status', 'cleared_at', 'bounced_at'] as $col) {
                    if (Schema::hasColumn('payments', $col)) {
                        $cols[] = $col;
                    }
                }
                if (!empty($cols)) {
                    $table->dropColumn($cols);
                }
            });
        }
    }
};


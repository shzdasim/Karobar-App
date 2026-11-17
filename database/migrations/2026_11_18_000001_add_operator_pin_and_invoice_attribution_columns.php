<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        if (Schema::hasTable('users') && !Schema::hasColumn('users', 'pin_code')) {
            Schema::table('users', function (Blueprint $table) {
                $table->string('pin_code', 10)->nullable()->unique();
            });
        }

        if (Schema::hasTable('employees') && !Schema::hasColumn('employees', 'pin_code')) {
            Schema::table('employees', function (Blueprint $table) {
                $table->string('pin_code', 10)->nullable()->unique();
            });
        }

        if (Schema::hasTable('purchase_invoices')) {
            Schema::table('purchase_invoices', function (Blueprint $table) {
                if (!Schema::hasColumn('purchase_invoices', 'user_id')) {
                    $table->unsignedBigInteger('user_id')->nullable()->index();
                }
                if (!Schema::hasColumn('purchase_invoices', 'employee_id')) {
                    $table->unsignedBigInteger('employee_id')->nullable()->index();
                }
            });
        }

        if (Schema::hasTable('purchase_returns')) {
            Schema::table('purchase_returns', function (Blueprint $table) {
                if (!Schema::hasColumn('purchase_returns', 'user_id')) {
                    $table->unsignedBigInteger('user_id')->nullable()->index();
                }
                if (!Schema::hasColumn('purchase_returns', 'employee_id')) {
                    $table->unsignedBigInteger('employee_id')->nullable()->index();
                }
            });
        }

        if (Schema::hasTable('sale_returns') && !Schema::hasColumn('sale_returns', 'employee_id')) {
            Schema::table('sale_returns', function (Blueprint $table) {
                $table->unsignedBigInteger('employee_id')->nullable()->index();
            });
        }

        if (Schema::hasTable('settings')) {
            Schema::table('settings', function (Blueprint $table) {
                if (!Schema::hasColumn('settings', 'require_operator_pin')) {
                    $table->boolean('require_operator_pin')->default(false);
                }
                if (!Schema::hasColumn('settings', 'operator_pin_auto_submit')) {
                    $table->boolean('operator_pin_auto_submit')->default(true);
                }
                if (!Schema::hasColumn('settings', 'operator_pin_modules')) {
                    $table->json('operator_pin_modules')->nullable();
                }
            });
        }
    }

    public function down(): void
    {
        if (Schema::hasTable('settings')) {
            Schema::table('settings', function (Blueprint $table) {
                foreach (['require_operator_pin', 'operator_pin_auto_submit', 'operator_pin_modules'] as $col) {
                    if (Schema::hasColumn('settings', $col)) {
                        $table->dropColumn($col);
                    }
                }
            });
        }

        if (Schema::hasTable('sale_returns') && Schema::hasColumn('sale_returns', 'employee_id')) {
            Schema::table('sale_returns', function (Blueprint $table) {
                $table->dropColumn('employee_id');
            });
        }

        if (Schema::hasTable('purchase_returns')) {
            Schema::table('purchase_returns', function (Blueprint $table) {
                foreach (['user_id', 'employee_id'] as $col) {
                    if (Schema::hasColumn('purchase_returns', $col)) {
                        $table->dropColumn($col);
                    }
                }
            });
        }

        if (Schema::hasTable('purchase_invoices')) {
            Schema::table('purchase_invoices', function (Blueprint $table) {
                foreach (['user_id', 'employee_id'] as $col) {
                    if (Schema::hasColumn('purchase_invoices', $col)) {
                        $table->dropColumn($col);
                    }
                }
            });
        }

        if (Schema::hasTable('employees') && Schema::hasColumn('employees', 'pin_code')) {
            Schema::table('employees', function (Blueprint $table) {
                $table->dropUnique(['pin_code']);
                $table->dropColumn('pin_code');
            });
        }

        if (Schema::hasTable('users') && Schema::hasColumn('users', 'pin_code')) {
            Schema::table('users', function (Blueprint $table) {
                $table->dropUnique(['pin_code']);
                $table->dropColumn('pin_code');
            });
        }
    }
};

<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        if (!Schema::hasTable('employees')) {
            Schema::create('employees', function (Blueprint $table) {
                $table->id();
                $table->string('employee_code', 40)->unique();
                $table->unsignedBigInteger('user_id')->nullable()->index();
                $table->string('name', 150);
                $table->string('phone', 50)->nullable();
                $table->string('email', 150)->nullable();
                $table->string('cnic', 50)->nullable();
                $table->string('address', 500)->nullable();
                $table->string('department', 100)->default('Sales & Counter')->index();
                $table->string('designation', 100)->default('Sales Associate');
                $table->string('employment_type', 40)->default('full_time'); // full_time, part_time, commission_only, contract
                $table->date('joining_date')->nullable();
                $table->string('shift_name', 80)->default('Regular Shift');
                $table->string('shift_start', 10)->default('09:00');
                $table->string('shift_end', 10)->default('18:00');
                $table->unsignedSmallInteger('working_days_per_month')->default(26);

                // Compensation & Commission Rules
                $table->decimal('basic_salary', 15, 2)->default(0);
                $table->decimal('default_allowances', 15, 2)->default(0);
                $table->decimal('hourly_overtime_rate', 15, 2)->default(0);
                $table->string('commission_type', 40)->default('none'); // none, net_sales_percent, gross_profit_percent, fixed_per_invoice
                $table->decimal('commission_rate', 10, 2)->default(0);
                $table->decimal('monthly_sales_target', 15, 2)->default(0);
                $table->decimal('target_bonus_amount', 15, 2)->default(0);

                // Payout & Running Advance Balance
                $table->string('payment_method', 30)->default('cash'); // cash, bank_transfer, wallet, cheque
                $table->string('bank_name', 120)->nullable();
                $table->string('bank_account_number', 100)->nullable();
                $table->decimal('advance_balance', 15, 2)->default(0);

                $table->string('status', 30)->default('active')->index(); // active, on_leave, inactive
                $table->text('notes')->nullable();
                $table->timestamps();
            });
        }

        if (!Schema::hasTable('employee_attendances')) {
            Schema::create('employee_attendances', function (Blueprint $table) {
                $table->id();
                $table->foreignId('employee_id')->constrained('employees')->cascadeOnDelete();
                $table->date('date')->index();
                $table->string('status', 30)->default('present')->index(); // present, absent, late, half_day, paid_leave, holiday
                $table->string('check_in', 10)->nullable();
                $table->string('check_out', 10)->nullable();
                $table->unsignedInteger('late_minutes')->default(0);
                $table->decimal('overtime_hours', 6, 2)->default(0);
                $table->decimal('fine_amount', 15, 2)->default(0);
                $table->string('notes', 300)->nullable();
                $table->unsignedBigInteger('recorded_by')->nullable();
                $table->timestamps();

                $table->unique(['employee_id', 'date']);
            });
        }

        if (!Schema::hasTable('employee_advances')) {
            Schema::create('employee_advances', function (Blueprint $table) {
                $table->id();
                $table->string('advance_number', 50)->unique();
                $table->foreignId('employee_id')->constrained('employees')->cascadeOnDelete();
                $table->string('type', 30)->default('advance')->index(); // advance, repayment, payroll_deduction
                $table->decimal('amount', 15, 2);
                $table->decimal('balance_after', 15, 2)->default(0);
                $table->date('date')->index();
                $table->string('payment_method', 30)->default('cash'); // cash, bank_transfer, wallet, cheque, payroll_deduction
                $table->unsignedBigInteger('account_id')->nullable()->index();
                $table->unsignedBigInteger('payroll_item_id')->nullable()->index();
                $table->string('reference_number', 100)->nullable();
                $table->string('description', 400)->nullable();
                $table->unsignedBigInteger('created_by')->nullable();
                $table->timestamps();
            });
        }

        if (!Schema::hasTable('payroll_runs')) {
            Schema::create('payroll_runs', function (Blueprint $table) {
                $table->id();
                $table->string('run_number', 50)->unique();
                $table->string('period_month', 7)->unique(); // YYYY-MM
                $table->date('start_date');
                $table->date('end_date');
                $table->string('status', 30)->default('draft')->index(); // draft, partial, paid
                $table->decimal('total_basic', 15, 2)->default(0);
                $table->decimal('total_allowances', 15, 2)->default(0);
                $table->decimal('total_overtime', 15, 2)->default(0);
                $table->decimal('total_commissions', 15, 2)->default(0);
                $table->decimal('total_bonuses', 15, 2)->default(0);
                $table->decimal('total_deductions', 15, 2)->default(0);
                $table->decimal('total_advance_deductions', 15, 2)->default(0);
                $table->decimal('total_net_payable', 15, 2)->default(0);
                $table->decimal('total_paid', 15, 2)->default(0);
                $table->text('notes')->nullable();
                $table->unsignedBigInteger('generated_by')->nullable();
                $table->timestamp('paid_at')->nullable();
                $table->timestamps();
            });
        }

        if (!Schema::hasTable('payroll_items')) {
            Schema::create('payroll_items', function (Blueprint $table) {
                $table->id();
                $table->foreignId('payroll_run_id')->constrained('payroll_runs')->cascadeOnDelete();
                $table->foreignId('employee_id')->constrained('employees')->restrictOnDelete();
                $table->unsignedSmallInteger('working_days')->default(26);
                $table->unsignedSmallInteger('present_days')->default(0);
                $table->unsignedSmallInteger('absent_days')->default(0);
                $table->unsignedSmallInteger('late_days')->default(0);
                $table->unsignedSmallInteger('half_days')->default(0);
                $table->unsignedSmallInteger('leave_days')->default(0);
                $table->decimal('overtime_hours', 6, 2)->default(0);

                // Earnings
                $table->decimal('basic_salary', 15, 2)->default(0);
                $table->decimal('allowances', 15, 2)->default(0);
                $table->decimal('overtime_pay', 15, 2)->default(0);
                $table->unsignedInteger('sales_invoices_count')->default(0);
                $table->decimal('sales_volume', 15, 2)->default(0);
                $table->decimal('sales_gross_profit', 15, 2)->default(0);
                $table->decimal('sales_commission', 15, 2)->default(0);
                $table->decimal('target_bonus', 15, 2)->default(0);
                $table->decimal('other_bonus', 15, 2)->default(0);
                $table->decimal('gross_earnings', 15, 2)->default(0);

                // Deductions
                $table->decimal('absence_deduction', 15, 2)->default(0);
                $table->decimal('late_or_fine_deduction', 15, 2)->default(0);
                $table->decimal('advance_deduction', 15, 2)->default(0);
                $table->decimal('other_deduction', 15, 2)->default(0);
                $table->decimal('total_deductions', 15, 2)->default(0);

                // Net & Payment Status
                $table->decimal('net_payable', 15, 2)->default(0);
                $table->string('payment_status', 30)->default('unpaid')->index(); // unpaid, paid
                $table->string('payment_method', 30)->nullable();
                $table->unsignedBigInteger('account_id')->nullable();
                $table->unsignedBigInteger('expense_id')->nullable();
                $table->timestamp('paid_at')->nullable();
                $table->string('notes', 400)->nullable();
                $table->timestamps();

                $table->unique(['payroll_run_id', 'employee_id']);
            });
        }

        if (Schema::hasTable('sale_invoices') && !Schema::hasColumn('sale_invoices', 'salesman_id')) {
            Schema::table('sale_invoices', function (Blueprint $table) {
                $table->unsignedBigInteger('salesman_id')->nullable()->after('user_id')->index();
            });
        }
    }

    public function down(): void
    {
        if (Schema::hasTable('sale_invoices') && Schema::hasColumn('sale_invoices', 'salesman_id')) {
            Schema::table('sale_invoices', function (Blueprint $table) {
                $table->dropColumn('salesman_id');
            });
        }
        Schema::dropIfExists('payroll_items');
        Schema::dropIfExists('payroll_runs');
        Schema::dropIfExists('employee_advances');
        Schema::dropIfExists('employee_attendances');
        Schema::dropIfExists('employees');
    }
};


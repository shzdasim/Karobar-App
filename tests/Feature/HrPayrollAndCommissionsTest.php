<?php

namespace Tests\Feature;

use App\Models\Account;
use App\Models\Brand;
use App\Models\Category;
use App\Models\Customer;
use App\Models\Employee;
use App\Models\EmployeeAdvance;
use App\Models\EmployeeAttendance;
use App\Models\Expense;
use App\Models\PayrollItem;
use App\Models\PayrollRun;
use App\Models\Product;
use App\Models\SaleInvoice;
use App\Models\SaleInvoiceItem;
use App\Models\Supplier;
use App\Models\User;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Spatie\Permission\Models\Role;
use Tests\TestCase;

class HrPayrollAndCommissionsTest extends TestCase
{
    use RefreshDatabase;

    private User $admin;
    private User $cashierUser;
    private Account $cashAccount;

    protected function setUp(): void
    {
        parent::setUp();
        $this->withoutMiddleware(\App\Http\Middleware\EnsureLicensed::class);

        $role = Role::firstOrCreate(['name' => 'Admin', 'guard_name' => 'sanctum']);
        $this->admin = User::create([
            'name'     => 'Test Admin',
            'email'    => 'admin@test.com',
            'password' => bcrypt('password'),
            'status'   => 'active',
        ]);
        $this->admin->assignRole($role);

        $this->cashierUser = User::create([
            'name'     => 'Bilal Cashier',
            'email'    => 'bilal@test.com',
            'password' => bcrypt('password'),
            'status'   => 'active',
        ]);

        $this->cashAccount = Account::firstOrCreate(
            ['code' => 'CASH-MAIN'],
            [
                'name'            => 'Main Cash Vault',
                'type'            => 'cash',
                'opening_balance' => 500000,
                'current_balance' => 500000,
                'is_default'      => true,
                'is_active'       => true,
            ]
        );
        $this->cashAccount->update(['current_balance' => 500000]);
    }

    public function test_can_create_and_update_employees_with_shift_and_commission_rules(): void
    {
        $response = $this->actingAs($this->admin, 'sanctum')->postJson('/api/hr/employees', [
            'name'                   => 'Muhammad Bilal',
            'cnic'                   => '35202-1234567-1',
            'phone'                  => '0300-1234567',
            'department'             => 'Sales & POS',
            'designation'            => 'Senior Pharmacist',
            'user_id'                => $this->cashierUser->id,
            'joining_date'           => '2026-01-01',
            'status'                 => 'active',
            'shift_start'            => '09:00',
            'shift_end'              => '18:00',
            'working_days_per_month' => 26,
            'basic_salary'           => 52000,
            'default_allowances'     => 5000,
            'hourly_overtime_rate'   => 300,
            'commission_type'        => 'net_sales_percent',
            'commission_rate'        => 2.5,
            'monthly_sales_target'   => 200000,
            'target_bonus_amount'    => 4000,
        ]);

        $response->assertCreated();
        $data = $response->json();
        $this->assertEquals('EMP-0001', $data['employee_code']);
        $this->assertEquals('Muhammad Bilal', $data['name']);
        $this->assertEquals(52000, (float) $data['basic_salary']);
        $this->assertEquals('net_sales_percent', $data['commission_type']);
        $this->assertEquals(2.5, (float) $data['commission_rate']);
    }

    public function test_bulk_attendance_sheet_and_summary(): void
    {
        $emp1 = Employee::create([
            'employee_code'          => 'EMP-0001',
            'name'                   => 'Ali Raza',
            'status'                 => 'active',
            'basic_salary'           => 39000,
            'working_days_per_month' => 26,
        ]);

        $emp2 = Employee::create([
            'employee_code'          => 'EMP-0002',
            'name'                   => 'Usman Tariq',
            'status'                 => 'active',
            'basic_salary'           => 45000,
            'working_days_per_month' => 26,
        ]);

        $saveResp = $this->actingAs($this->admin, 'sanctum')->postJson('/api/hr/attendance/bulk', [
            'date' => '2026-10-15',
            'entries' => [
                [
                    'employee_id'    => $emp1->id,
                    'status'         => 'late',
                    'check_in'       => '09:35',
                    'check_out'      => '18:00',
                    'late_minutes'   => 35,
                    'overtime_hours' => 0,
                    'fine_amount'    => 250,
                    'notes'          => 'Traffic delay',
                ],
                [
                    'employee_id'    => $emp2->id,
                    'status'         => 'present',
                    'check_in'       => '09:00',
                    'check_out'      => '20:00',
                    'late_minutes'   => 0,
                    'overtime_hours' => 2,
                    'fine_amount'    => 0,
                ],
            ],
        ]);

        $saveResp->assertOk();

        $sheetResp = $this->actingAs($this->admin, 'sanctum')->getJson('/api/hr/attendance?date=2026-10-15');
        $sheetResp->assertOk();
        $sheet = collect($sheetResp->json('sheet'));
        $this->assertCount(2, $sheet);
        $this->assertEquals(1, $sheet->where('status', 'present')->count());
        $this->assertEquals(1, $sheet->where('status', 'late')->count());
        $this->assertEquals(2.0, (float) $sheet->sum('overtime_hours'));
        $this->assertEquals(250.0, (float) $sheet->sum('fine_amount'));
    }

    public function test_staff_salary_advance_disbursement_and_repayment_updates_treasury_and_balance(): void
    {
        $emp = Employee::create([
            'employee_code' => 'EMP-0001',
            'name'          => 'Kamran Khan',
            'status'        => 'active',
            'basic_salary'  => 60000,
        ]);

        $initialVault = (float) $this->cashAccount->fresh()->current_balance;

        // 1. Issue Rs. 15,000 Staff Salary Advance ("Kharcha")
        $advResp = $this->actingAs($this->admin, 'sanctum')->postJson('/api/hr/advances', [
            'employee_id'    => $emp->id,
            'date'           => '2026-10-10',
            'type'           => 'advance',
            'amount'         => 15000,
            'payment_method' => 'cash',
            'account_id'     => $this->cashAccount->id,
            'description'    => 'Medical emergency advance',
        ]);
        $advResp->assertCreated();

        $this->assertEquals(15000.0, (float) $emp->fresh()->advance_balance);
        $this->assertEquals($initialVault - 15000.0, (float) $this->cashAccount->fresh()->current_balance);

        // 2. Staff repays Rs. 5,000 in cash
        $repayResp = $this->actingAs($this->admin, 'sanctum')->postJson('/api/hr/advances', [
            'employee_id'    => $emp->id,
            'date'           => '2026-10-20',
            'type'           => 'repayment',
            'amount'         => 5000,
            'payment_method' => 'cash',
            'account_id'     => $this->cashAccount->id,
            'description'    => 'Partial cash return',
        ]);
        $repayResp->assertCreated();

        $this->assertEquals(10000.0, (float) $emp->fresh()->advance_balance);
        $this->assertEquals($initialVault - 10000.0, (float) $this->cashAccount->fresh()->current_balance);
    }

    public function test_commission_engine_and_monthly_payroll_generation_and_disbursement(): void
    {
        // Employee 1: Linked to cashierUser, earns 3% of Net Sales + Rs. 5,000 bonus if Sales >= Rs. 100,000
        $salesman1 = Employee::create([
            'employee_code'          => 'EMP-0001',
            'name'                   => 'Bilal Pharmacist',
            'user_id'                => $this->cashierUser->id,
            'status'                 => 'active',
            'working_days_per_month' => 26,
            'basic_salary'           => 52000, // Rs. 2,000 / day
            'default_allowances'     => 4000,
            'hourly_overtime_rate'   => 250,
            'commission_type'        => 'net_sales_percent',
            'commission_rate'        => 3.0,
            'monthly_sales_target'   => 100000,
            'target_bonus_amount'    => 5000,
            'advance_balance'        => 8000,
        ]);

        // Employee 2: Order Booker, earns 10% of Gross Profit Margin
        $salesman2 = Employee::create([
            'employee_code'          => 'EMP-0002',
            'name'                   => 'Zain Order Booker',
            'status'                 => 'active',
            'working_days_per_month' => 26,
            'basic_salary'           => 39000, // Rs. 1,500 / day
            'default_allowances'     => 2000,
            'hourly_overtime_rate'   => 200,
            'commission_type'        => 'gross_profit_percent',
            'commission_rate'        => 10.0,
            'monthly_sales_target'   => 500000,
            'target_bonus_amount'    => 10000,
            'advance_balance'        => 0,
        ]);

        $customer = Customer::create(['name' => 'City Clinic']);
        $category = Category::create(['name' => 'Pharma']);
        $brand = Brand::create(['name' => 'GSK']);
        $supplier = Supplier::create(['name' => 'Distributor']);
        $product = Product::create([
            'product_code'        => 'PRD-100',
            'barcode'             => '8901234567890',
            'name'                => 'Augmentin 625mg',
            'pack_size'           => 10,
            'quantity'            => 1000,
            'unit_purchase_price' => 55,
            'avg_price'           => 55,
            'unit_sale_price'     => 80,
            'narcotic'            => 'no',
            'category_id'         => $category->id,
            'brand_id'            => $brand->id,
            'supplier_id'         => $supplier->id,
        ]);

        // Create posted SaleInvoices in October 2026:
        // Invoice 1 attributed to salesman1 via user_id ($this->cashierUser->id): Rs. 120,000 net sales
        $inv1 = SaleInvoice::create([
            'user_id'       => $this->cashierUser->id,
            'customer_id'   => $customer->id,
            'posted_number' => 'SI-2026-001',
            'status'        => 'posted',
            'date'          => '2026-10-08',
            'gross_amount'  => 120000,
            'total'         => 120000,
            'total_receive' => 120000,
        ]);
        SaleInvoiceItem::create([
            'sale_invoice_id'     => $inv1->id,
            'product_id'          => $product->id,
            'pack_size'           => 10,
            'quantity'            => 1500,
            'price'               => 80,
            'cost_price_snapshot' => 60, // COGS = 1500 * 60 = 90,000
            'sub_total'           => 120000,
        ]);

        // Invoice 2 attributed to salesman2 via explicit salesman_id: Rs. 80,000 net sales, COGS Rs. 55,000 (Gross profit = Rs. 25,000)
        $inv2 = SaleInvoice::create([
            'user_id'       => $this->admin->id,
            'salesman_id'   => $salesman2->id,
            'customer_id'   => $customer->id,
            'posted_number' => 'SI-2026-002',
            'status'        => 'posted',
            'date'          => '2026-10-12',
            'gross_amount'  => 80000,
            'total'         => 80000,
            'total_receive' => 80000,
        ]);
        SaleInvoiceItem::create([
            'sale_invoice_id'     => $inv2->id,
            'product_id'          => $product->id,
            'pack_size'           => 10,
            'quantity'            => 1000,
            'price'               => 80,
            'cost_price_snapshot' => 55, // COGS = 1000 * 55 = 55,000 -> Gross Profit = 25,000
            'sub_total'           => 80000,
        ]);

        // Record Attendance in October 2026 for salesman1:
        // 1 absent day (deducts 1 * 2000 = 2000), 1 late day with Rs. 300 fine & 4 overtime hours (4 * 250 = 1000)
        EmployeeAttendance::create([
            'employee_id' => $salesman1->id,
            'date'        => '2026-10-05',
            'status'      => 'absent',
        ]);
        EmployeeAttendance::create([
            'employee_id'    => $salesman1->id,
            'date'           => '2026-10-06',
            'status'         => 'late',
            'late_minutes'   => 40,
            'overtime_hours' => 4,
            'fine_amount'    => 300,
        ]);

        // 1. Verify Commission Report endpoint for 2026-10
        $commResp = $this->actingAs($this->admin, 'sanctum')->getJson('/api/hr/commissions?from=2026-10-01&to=2026-10-31');
        $commResp->assertOk();
        $rows = collect($commResp->json('rows'))->keyBy('employee_id');

        // Salesman 1: 3% of 120,000 = 3,600 commission + 5,000 target bonus = 8,600 total incentive
        $this->assertEquals(120000.0, (float) $rows[$salesman1->id]['net_sales']);
        $this->assertEquals(3600.0, (float) $rows[$salesman1->id]['commission_earned']);
        $this->assertTrue((bool) $rows[$salesman1->id]['target_achieved']);
        $this->assertEquals(5000.0, (float) $rows[$salesman1->id]['target_bonus']);

        // Salesman 2: 10% of 25,000 gross profit = 2,500 commission, target not met (80k < 500k)
        $this->assertEquals(25000.0, (float) $rows[$salesman2->id]['gross_profit']);
        $this->assertEquals(2500.0, (float) $rows[$salesman2->id]['commission_earned']);
        $this->assertFalse((bool) $rows[$salesman2->id]['target_achieved']);
        $this->assertEquals(0.0, (float) $rows[$salesman2->id]['target_bonus']);

        // 2. Generate Monthly Payroll Run for 2026-10
        $genResp = $this->actingAs($this->admin, 'sanctum')->postJson('/api/hr/payroll-runs/generate', [
            'period_month'         => '2026-10',
            'auto_deduct_advances' => true,
            'notes'                => 'October 2026 Payroll',
        ]);
        $genResp->assertCreated();

        $runId = $genResp->json('id');
        $items = collect($genResp->json('items'))->keyBy('employee_id');

        // Verify Salesman 1 Payslip math:
        // Earnings: Basic 52,000 + Allowance 4,000 + Overtime 1,000 + Commission 3,600 + Target Bonus 5,000 = 65,600
        // Deductions: Absent 2,000 + Fine 300 + Advance Recovery 8,000 = 10,300
        // Net Payable: 65,600 - 10,300 = 55,300
        $item1 = $items[$salesman1->id];
        $this->assertEquals(65600.0, (float) $item1['gross_earnings']);
        $this->assertEquals(2000.0, (float) $item1['absence_deduction']);
        $this->assertEquals(300.0, (float) $item1['late_or_fine_deduction']);
        $this->assertEquals(8000.0, (float) $item1['advance_deduction']);
        $this->assertEquals(10300.0, (float) $item1['total_deductions']);
        $this->assertEquals(55300.0, (float) $item1['net_payable']);

        // Verify Salesman 2 Payslip math:
        // Earnings: Basic 39,000 + Allowance 2,000 + Commission 2,500 = 43,500
        // Deductions: 0 -> Net Payable: 43,500
        $item2 = $items[$salesman2->id];
        $this->assertEquals(43500.0, (float) $item2['gross_earnings']);
        $this->assertEquals(43500.0, (float) $item2['net_payable']);

        // 3. Disburse All Payslips in the Run
        $vaultBefore = (float) $this->cashAccount->fresh()->current_balance;

        $payAllResp = $this->actingAs($this->admin, 'sanctum')->postJson("/api/hr/payroll-runs/{$runId}/pay-all", [
            'payment_method' => 'cash',
            'account_id'     => $this->cashAccount->id,
            'date'           => '2026-10-31',
        ]);
        $payAllResp->assertOk();
        $this->assertEquals('paid', $payAllResp->json('status'));

        // Verify Salesman 1's Rs. 8,000 advance balance was automatically recovered to 0
        $this->assertEquals(0.0, (float) $salesman1->fresh()->advance_balance);
        $this->assertDatabaseHas('employee_advances', [
            'employee_id'     => $salesman1->id,
            'type'            => 'payroll_deduction',
            'payroll_item_id' => $item1['id'],
            'amount'          => 8000.00,
        ]);

        // Verify Operating Expenses were created and Treasury Cash Account was deducted by total net payable (55,300 + 43,500 = 98,800)
        $this->assertEquals(2, Expense::count());
        $this->assertEquals($vaultBefore - 98800.0, (float) $this->cashAccount->fresh()->current_balance);
    }
}

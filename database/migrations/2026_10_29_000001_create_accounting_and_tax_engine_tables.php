<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        if (!Schema::hasTable('chart_of_accounts')) {
            Schema::create('chart_of_accounts', function (Blueprint $table) {
                $table->id();
                $table->string('code', 30)->unique();
                $table->string('name', 150);
                $table->string('account_type', 30)->index(); // asset, liability, equity, revenue, expense
                $table->string('sub_type', 50)->nullable()->index();
                $table->string('normal_balance', 10)->default('debit'); // debit, credit
                $table->string('system_key', 60)->nullable()->unique();
                $table->decimal('opening_balance', 15, 2)->default(0);
                $table->boolean('is_system')->default(false);
                $table->boolean('is_active')->default(true);
                $table->string('description', 500)->nullable();
                $table->timestamps();
            });
        }

        if (!Schema::hasTable('journal_entries')) {
            Schema::create('journal_entries', function (Blueprint $table) {
                $table->id();
                $table->string('entry_number', 50)->unique();
                $table->date('entry_date')->index();
                $table->string('reference_type', 50)->default('manual')->index(); // manual, tax_payment, capital, drawing, depreciation, adjustment
                $table->string('reference_number', 100)->nullable();
                $table->unsignedBigInteger('account_id')->nullable()->index(); // optional linked treasury account
                $table->string('description', 500);
                $table->decimal('total_debit', 15, 2)->default(0);
                $table->decimal('total_credit', 15, 2)->default(0);
                $table->string('status', 20)->default('posted')->index(); // posted, reversed
                $table->unsignedBigInteger('created_by')->nullable();
                $table->unsignedBigInteger('reversed_by')->nullable();
                $table->timestamp('reversed_at')->nullable();
                $table->string('reversal_reason', 500)->nullable();
                $table->timestamps();
            });
        }

        if (!Schema::hasTable('journal_entry_lines')) {
            Schema::create('journal_entry_lines', function (Blueprint $table) {
                $table->id();
                $table->foreignId('journal_entry_id')->constrained('journal_entries')->cascadeOnDelete();
                $table->foreignId('chart_of_account_id')->constrained('chart_of_accounts')->restrictOnDelete();
                $table->string('party_type', 30)->nullable(); // customer, supplier
                $table->unsignedBigInteger('party_id')->nullable();
                $table->string('description', 300)->nullable();
                $table->decimal('debit', 15, 2)->default(0);
                $table->decimal('credit', 15, 2)->default(0);
                $table->timestamps();

                $table->index(['chart_of_account_id', 'journal_entry_id']);
            });
        }

        if (!Schema::hasTable('tax_rates')) {
            Schema::create('tax_rates', function (Blueprint $table) {
                $table->id();
                $table->string('name', 120);
                $table->string('code', 40)->unique();
                $table->decimal('rate', 8, 2)->default(0);
                $table->string('type', 30)->default('both'); // both, sales, purchase, withholding
                $table->boolean('is_default')->default(false);
                $table->boolean('is_active')->default(true);
                $table->string('description', 300)->nullable();
                $table->timestamps();
            });
        }

        if (Schema::hasTable('customers')) {
            Schema::table('customers', function (Blueprint $table) {
                if (!Schema::hasColumn('customers', 'tax_number')) {
                    $table->string('tax_number', 60)->nullable()->after('phone');
                }
                if (!Schema::hasColumn('customers', 'tax_status')) {
                    $table->string('tax_status', 30)->default('unregistered')->after('tax_number'); // registered, unregistered, exempt
                }
            });
        }

        if (Schema::hasTable('suppliers')) {
            Schema::table('suppliers', function (Blueprint $table) {
                if (!Schema::hasColumn('suppliers', 'tax_number')) {
                    $table->string('tax_number', 60)->nullable()->after('phone');
                }
                if (!Schema::hasColumn('suppliers', 'tax_status')) {
                    $table->string('tax_status', 30)->default('registered')->after('tax_number'); // registered, unregistered, exempt
                }
            });
        }

        // Seed standard Chart of Accounts
        $now = now();
        $defaultAccounts = [
            // 1000 - ASSETS
            [
                'code'           => '1010',
                'name'           => 'Cash & Bank Equivalents (Treasury)',
                'account_type'   => 'asset',
                'sub_type'       => 'cash_bank',
                'normal_balance' => 'debit',
                'system_key'     => 'cash_and_banks',
                'is_system'      => true,
                'description'    => 'Cash in hand, active drawers, commercial bank accounts, and mobile wallets',
            ],
            [
                'code'           => '1200',
                'name'           => 'Accounts Receivable (Trade Debtors)',
                'account_type'   => 'asset',
                'sub_type'       => 'accounts_receivable',
                'normal_balance' => 'debit',
                'system_key'     => 'accounts_receivable',
                'is_system'      => true,
                'description'    => 'Receivable balances due from credit customers',
            ],
            [
                'code'           => '1300',
                'name'           => 'Merchandise Inventory (Stock-on-Hand)',
                'account_type'   => 'asset',
                'sub_type'       => 'inventory',
                'normal_balance' => 'debit',
                'system_key'     => 'merchandise_inventory',
                'is_system'      => true,
                'description'    => 'On-hand product inventory valued at weighted-average cost',
            ],
            [
                'code'           => '1400',
                'name'           => 'Input Tax Receivable (Purchase GST/VAT)',
                'account_type'   => 'asset',
                'sub_type'       => 'tax_receivable',
                'normal_balance' => 'debit',
                'system_key'     => 'input_tax_receivable',
                'is_system'      => true,
                'description'    => 'Input sales tax / VAT paid on supplier purchases claimable against output tax',
            ],
            [
                'code'           => '1500',
                'name'           => 'Prepayments & Supplier Advances',
                'account_type'   => 'asset',
                'sub_type'       => 'current_asset',
                'normal_balance' => 'debit',
                'system_key'     => 'supplier_advances',
                'is_system'      => false,
                'description'    => 'Advance payments, security deposits, and prepaid store expenses',
            ],
            [
                'code'           => '1600',
                'name'           => 'Store Equipment, Fixtures & Fixed Assets',
                'account_type'   => 'asset',
                'sub_type'       => 'fixed_asset',
                'normal_balance' => 'debit',
                'system_key'     => 'fixed_assets',
                'is_system'      => false,
                'description'    => 'Shelving, refrigerators, generators, POS hardware, and vehicles',
            ],

            // 2000 - LIABILITIES
            [
                'code'           => '2100',
                'name'           => 'Accounts Payable (Trade Creditors)',
                'account_type'   => 'liability',
                'sub_type'       => 'accounts_payable',
                'normal_balance' => 'credit',
                'system_key'     => 'accounts_payable',
                'is_system'      => true,
                'description'    => 'Payable balances owed to pharmaceutical & wholesale suppliers',
            ],
            [
                'code'           => '2200',
                'name'           => 'Output Tax Payable (Sales GST/VAT)',
                'account_type'   => 'liability',
                'sub_type'       => 'tax_payable',
                'normal_balance' => 'credit',
                'system_key'     => 'output_tax_payable',
                'is_system'      => true,
                'description'    => 'Output GST / VAT collected on customer sales invoices',
            ],
            [
                'code'           => '2300',
                'name'           => 'Withholding Tax (WHT) & Statutory Dues Payable',
                'account_type'   => 'liability',
                'sub_type'       => 'tax_payable',
                'normal_balance' => 'credit',
                'system_key'     => 'withholding_tax_payable',
                'is_system'      => false,
                'description'    => 'Withholding tax deducted on supplier payments and statutory obligations',
            ],
            [
                'code'           => '2400',
                'name'           => 'Accrued Expenses & Short-Term Liabilities',
                'account_type'   => 'liability',
                'sub_type'       => 'current_liability',
                'normal_balance' => 'credit',
                'system_key'     => 'accrued_liabilities',
                'is_system'      => false,
                'description'    => 'Unpaid utility bills, rent accruals, and customer credit notes',
            ],

            // 3000 - EQUITY
            [
                'code'           => '3100',
                'name'           => 'Owner\'s Capital & Opening Equity Reserve',
                'account_type'   => 'equity',
                'sub_type'       => 'equity',
                'normal_balance' => 'credit',
                'system_key'     => 'owners_capital',
                'is_system'      => true,
                'description'    => 'Owner capital contributions and opening inventory/treasury equity reserve',
            ],
            [
                'code'           => '3200',
                'name'           => 'Owner\'s Drawings & Distributions',
                'account_type'   => 'equity',
                'sub_type'       => 'equity',
                'normal_balance' => 'debit',
                'system_key'     => 'owners_drawings',
                'is_system'      => false,
                'description'    => 'Personal withdrawals and profit distributions to owners/partners',
            ],
            [
                'code'           => '3300',
                'name'           => 'Retained Earnings & Cumulative Net Income',
                'account_type'   => 'equity',
                'sub_type'       => 'equity',
                'normal_balance' => 'credit',
                'system_key'     => 'retained_earnings',
                'is_system'      => true,
                'description'    => 'Accumulated net operating earnings retained in the business',
            ],

            // 4000 - REVENUE
            [
                'code'           => '4100',
                'name'           => 'Gross Sales Revenue (Retail & Wholesale)',
                'account_type'   => 'revenue',
                'sub_type'       => 'sales_revenue',
                'normal_balance' => 'credit',
                'system_key'     => 'gross_sales_revenue',
                'is_system'      => true,
                'description'    => 'Gross revenue from retail counter and wholesale distribution sales',
            ],
            [
                'code'           => '4200',
                'name'           => 'Sales Returns & Allowances (Contra-Revenue)',
                'account_type'   => 'revenue',
                'sub_type'       => 'contra_revenue',
                'normal_balance' => 'debit',
                'system_key'     => 'sales_returns',
                'is_system'      => true,
                'description'    => 'Merchandise returned by customers (net of tax)',
            ],
            [
                'code'           => '4300',
                'name'           => 'Sales Discounts Allowed (Contra-Revenue)',
                'account_type'   => 'revenue',
                'sub_type'       => 'contra_revenue',
                'normal_balance' => 'debit',
                'system_key'     => 'sales_discounts',
                'is_system'      => true,
                'description'    => 'Line-item and invoice-level promotional/trade discounts granted to customers',
            ],
            [
                'code'           => '4400',
                'name'           => 'Other Operating Income & Supplier Rebates',
                'account_type'   => 'revenue',
                'sub_type'       => 'other_income',
                'normal_balance' => 'credit',
                'system_key'     => 'other_income',
                'is_system'      => false,
                'description'    => 'Purchase trade discounts, supplier rebates, and miscellaneous income',
            ],

            // 5000 - EXPENSES & COST OF SALES
            [
                'code'           => '5100',
                'name'           => 'Cost of Goods Sold (COGS)',
                'account_type'   => 'expense',
                'sub_type'       => 'cogs',
                'normal_balance' => 'debit',
                'system_key'     => 'cost_of_goods_sold',
                'is_system'      => true,
                'description'    => 'Direct inventory cost of items sold (net of returned item cost)',
            ],
            [
                'code'           => '5200',
                'name'           => 'Inventory Shrinkage, Quarantine & Expiry Write-Offs',
                'account_type'   => 'expense',
                'sub_type'       => 'cogs',
                'normal_balance' => 'debit',
                'system_key'     => 'inventory_shrinkage',
                'is_system'      => true,
                'description'    => 'Expired batch disposals, quarantine write-offs, and negative stocktake variances',
            ],
            [
                'code'           => '5300',
                'name'           => 'Store Operating & Administrative Expenses (OpEx)',
                'account_type'   => 'expense',
                'sub_type'       => 'operating_expense',
                'normal_balance' => 'debit',
                'system_key'     => 'operating_expenses',
                'is_system'      => true,
                'description'    => 'Rent, salaries, electricity, delivery fuel, and general store expenses',
            ],
            [
                'code'           => '5400',
                'name'           => 'Depreciation & Amortization Expense',
                'account_type'   => 'expense',
                'sub_type'       => 'operating_expense',
                'normal_balance' => 'debit',
                'system_key'     => 'depreciation_expense',
                'is_system'      => false,
                'description'    => 'Periodic depreciation of store equipment, fixtures, and vehicles',
            ],
        ];

        foreach ($defaultAccounts as $acc) {
            $exists = DB::table('chart_of_accounts')
                ->where('code', $acc['code'])
                ->orWhere('system_key', $acc['system_key'])
                ->exists();
            if (!$exists) {
                DB::table('chart_of_accounts')->insert(array_merge($acc, [
                    'opening_balance' => 0,
                    'is_active'       => true,
                    'created_at'      => $now,
                    'updated_at'      => $now,
                ]));
            }
        }

        // Seed standard Statutory Tax Rates
        $defaultTaxRates = [
            [
                'name'        => 'Standard GST / VAT (18%)',
                'code'        => 'GST-18',
                'rate'        => 18.00,
                'type'        => 'both',
                'is_default'  => true,
                'description' => 'Standard statutory General Sales Tax (18%)',
            ],
            [
                'name'        => 'Standard GST (17%)',
                'code'        => 'GST-17',
                'rate'        => 17.00,
                'type'        => 'both',
                'is_default'  => false,
                'description' => 'Standard Sales Tax 17%',
            ],
            [
                'name'        => 'Reduced Retail / Pharma Tax (5%)',
                'code'        => 'GST-5',
                'rate'        => 5.00,
                'type'        => 'both',
                'is_default'  => false,
                'description' => 'Reduced statutory tax rate (5%)',
            ],
            [
                'name'        => 'Further Tax - Unregistered Buyer (4%)',
                'code'        => 'FT-4',
                'rate'        => 4.00,
                'type'        => 'sales',
                'is_default'  => false,
                'description' => 'Further tax on wholesale supplies to unregistered persons',
            ],
            [
                'name'        => 'Advance / Withholding Tax u/s 236G/H (1%)',
                'code'        => 'WHT-1',
                'rate'        => 1.00,
                'type'        => 'withholding',
                'is_default'  => false,
                'description' => 'Advance withholding tax on distribution / retail supplies',
            ],
            [
                'name'        => 'Zero-Rated / Essential Medicine (0%)',
                'code'        => 'ZERO-0',
                'rate'        => 0.00,
                'type'        => 'both',
                'is_default'  => false,
                'description' => 'Zero-rated / Essential pharmaceutical or grocery supplies',
            ],
        ];

        foreach ($defaultTaxRates as $rate) {
            if (!DB::table('tax_rates')->where('code', $rate['code'])->exists()) {
                DB::table('tax_rates')->insert(array_merge($rate, [
                    'is_active'  => true,
                    'created_at' => $now,
                    'updated_at' => $now,
                ]));
            }
        }
    }

    public function down(): void
    {
        if (Schema::hasTable('suppliers')) {
            Schema::table('suppliers', function (Blueprint $table) {
                $cols = array_filter(['tax_number', 'tax_status'], fn ($c) => Schema::hasColumn('suppliers', $c));
                if (!empty($cols)) {
                    $table->dropColumn($cols);
                }
            });
        }

        if (Schema::hasTable('customers')) {
            Schema::table('customers', function (Blueprint $table) {
                $cols = array_filter(['tax_number', 'tax_status'], fn ($c) => Schema::hasColumn('customers', $c));
                if (!empty($cols)) {
                    $table->dropColumn($cols);
                }
            });
        }

        Schema::dropIfExists('tax_rates');
        Schema::dropIfExists('journal_entry_lines');
        Schema::dropIfExists('journal_entries');
        Schema::dropIfExists('chart_of_accounts');
    }
};


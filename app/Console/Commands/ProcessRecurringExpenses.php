<?php

namespace App\Console\Commands;

use App\Services\ExpenseService;
use Carbon\Carbon;
use Illuminate\Console\Command;

class ProcessRecurringExpenses extends Command
{
    /**
     * The name and signature of the console command.
     *
     * @var string
     */
    protected $signature = 'expenses:process-recurring {--date= : Process recurring expenses due on or before this YYYY-MM-DD date}';

    /**
     * The console command description.
     *
     * @var string
     */
    protected $description = 'Automatically generate due recurring operating expenses (weekly, monthly, yearly) and apply account/drawer deductions.';

    /**
     * Execute the console command.
     */
    public function handle(ExpenseService $expenseService): int
    {
        $dateOption = $this->option('date');
        $asOfDate = $dateOption ? Carbon::parse($dateOption) : Carbon::today();

        $result = $expenseService->processRecurringExpenses($asOfDate);
        $count = $result['processed_count'] ?? 0;

        $this->info("Recurring expenses processed: {$count}");

        return self::SUCCESS;
    }
}


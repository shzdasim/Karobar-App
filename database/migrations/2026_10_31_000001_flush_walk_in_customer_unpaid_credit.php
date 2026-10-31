<?php

use App\Console\Commands\FlushWalkInCreditCommand;
use Illuminate\Database\Migrations\Migration;

return new class extends Migration
{
    /**
     * Run the migrations.
     */
    public function up(): void
    {
        FlushWalkInCreditCommand::flush(false);
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        // Irreversible data cleanup
    }
};


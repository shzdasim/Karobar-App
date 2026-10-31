<?php

use App\Models\Formulation;
use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    public function up(): void
    {
        if (!Schema::hasTable('formulations')) {
            Schema::create('formulations', function (Blueprint $table) {
                $table->id();
                $table->string('name');
                $table->string('normalized_name')->unique();
                $table->timestamps();
            });
        }

        if (Schema::hasTable('products') && Schema::hasColumn('products', 'formulation')) {
            $rawFormulations = DB::table('products')
                ->whereNotNull('formulation')
                ->where('formulation', '!=', '')
                ->pluck('formulation');

            $canonicalByKey = [];
            foreach ($rawFormulations as $raw) {
                $clean = Formulation::normalizeName((string) $raw);
                if ($clean === '') {
                    continue;
                }
                $key = mb_strtolower($clean);
                if (!isset($canonicalByKey[$key])) {
                    $canonicalByKey[$key] = $clean;
                } elseif ($canonicalByKey[$key] === mb_strtolower($canonicalByKey[$key]) && $clean !== mb_strtolower($clean)) {
                    // Prefer capitalized spelling (e.g. "Omeprazole") over all-lowercase ("omeprazole")
                    $canonicalByKey[$key] = $clean;
                }
            }

            $now = now();
            foreach ($canonicalByKey as $key => $canonicalName) {
                $existing = DB::table('formulations')
                    ->where('normalized_name', $key)
                    ->first();

                $finalName = $existing ? $existing->name : $canonicalName;

                if (!$existing) {
                    DB::table('formulations')->insert([
                        'name'            => $finalName,
                        'normalized_name' => $key,
                        'created_at'      => $now,
                        'updated_at'      => $now,
                    ]);
                }

                // Unify any existing products that had different casing/spacing of this formulation
                DB::table('products')
                    ->whereNotNull('formulation')
                    ->whereRaw('LOWER(TRIM(formulation)) = ?', [$key])
                    ->where('formulation', '!=', $finalName)
                    ->update(['formulation' => $finalName]);
            }
        }
    }

    public function down(): void
    {
        Schema::dropIfExists('formulations');
    }
};

<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Support\Facades\Schema;

class Formulation extends Model
{
    use HasFactory;

    protected $fillable = [
        'name',
        'normalized_name',
    ];

    public static function normalizeName(?string $name): string
    {
        $trimmed = trim((string) $name);
        if ($trimmed === '') {
            return '';
        }

        return preg_replace('/\s+/', ' ', $trimmed) ?? $trimmed;
    }

    public static function normalizeKey(?string $name): string
    {
        return mb_strtolower(self::normalizeName($name));
    }

    /**
     * Find an existing formulation case-insensitively or create it once.
     * Always returns the canonical single-spelling Formulation instance.
     */
    public static function resolveOrCreate(?string $rawName): ?self
    {
        $clean = self::normalizeName($rawName);
        if ($clean === '') {
            return null;
        }

        $key = mb_strtolower($clean);

        if (!Schema::hasTable('formulations')) {
            return new self([
                'name'            => $clean,
                'normalized_name' => $key,
            ]);
        }

        $existing = self::query()
            ->where('normalized_name', $key)
            ->orWhereRaw('LOWER(name) = ?', [$key])
            ->first();

        if ($existing) {
            return $existing;
        }

        try {
            return self::create([
                'name'            => $clean,
                'normalized_name' => $key,
            ]);
        } catch (\Throwable $e) {
            return self::query()
                ->where('normalized_name', $key)
                ->orWhereRaw('LOWER(name) = ?', [$key])
                ->first() ?? new self([
                    'name'            => $clean,
                    'normalized_name' => $key,
                ]);
        }
    }

    public function products()
    {
        return $this->hasMany(Product::class, 'formulation', 'name');
    }
}

<?php

namespace App\Http\Controllers;

use App\Models\Formulation;
use App\Models\Product;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Schema;

class FormulationController extends Controller
{
    private function authorizeLookup(): void
    {
        $this->authorize('lookup', Product::class);
    }

    private function authorizeSave(Request $request): void
    {
        $user = $request->user();
        if (
            !$user
            || (
                !$user->hasRole(['Admin', 'admin', 'super-admin'])
                && !$user->can('product.create')
                && !$user->can('product.update')
                && !$user->can('product.import')
            )
        ) {
            abort(403, 'This action is unauthorized.');
        }
    }

    /**
     * GET /api/formulations
     * GET /api/formulations/search
     * Returns deduplicated canonical formulations, optionally filtered by `q` or `q_name`.
     */
    public function index(Request $request)
    {
        $this->authorizeLookup();

        $q = Formulation::normalizeName((string) ($request->input('q') ?? $request->input('q_name', '')));
        $qLower = mb_strtolower($q);
        $limit = (int) $request->input('limit', 0);

        $map = [];

        if (Schema::hasTable('formulations')) {
            $rows = Formulation::query()
                ->select(['id', 'name', 'normalized_name'])
                ->orderBy('name')
                ->get();

            foreach ($rows as $row) {
                $clean = Formulation::normalizeName($row->name);
                if ($clean === '') {
                    continue;
                }
                $key = $row->normalized_name ?: mb_strtolower($clean);
                if (!isset($map[$key])) {
                    $map[$key] = [
                        'id'   => $row->id,
                        'name' => $clean,
                    ];
                }
            }
        }

        // Also include any distinct product formulations not yet synced into `formulations`
        if (Schema::hasTable('products') && Schema::hasColumn('products', 'formulation')) {
            $productFormulations = Product::query()
                ->whereNotNull('formulation')
                ->where('formulation', '!=', '')
                ->distinct()
                ->pluck('formulation');

            foreach ($productFormulations as $raw) {
                $clean = Formulation::normalizeName((string) $raw);
                if ($clean === '') {
                    continue;
                }
                $key = mb_strtolower($clean);
                if (!isset($map[$key])) {
                    $resolved = Formulation::resolveOrCreate($clean);
                    $map[$key] = [
                        'id'   => $resolved?->id,
                        'name' => $resolved?->name ?? $clean,
                    ];
                }
            }
        }

        $items = array_values($map);

        if ($qLower !== '') {
            $items = array_values(array_filter($items, function (array $item) use ($qLower) {
                return str_contains(mb_strtolower($item['name']), $qLower);
            }));

            usort($items, function (array $a, array $b) use ($qLower) {
                $aLower = mb_strtolower($a['name']);
                $bLower = mb_strtolower($b['name']);

                $aExact = $aLower === $qLower ? 0 : 1;
                $bExact = $bLower === $qLower ? 0 : 1;
                if ($aExact !== $bExact) {
                    return $aExact <=> $bExact;
                }

                $aPrefix = str_starts_with($aLower, $qLower) ? 0 : 1;
                $bPrefix = str_starts_with($bLower, $qLower) ? 0 : 1;
                if ($aPrefix !== $bPrefix) {
                    return $aPrefix <=> $bPrefix;
                }

                return strcasecmp($a['name'], $b['name']);
            });
        } else {
            usort($items, fn (array $a, array $b) => strcasecmp($a['name'], $b['name']));
        }

        if ($limit > 0) {
            $items = array_slice($items, 0, min($limit, 5000));
        }

        return response()->json($items);
    }

    public function search(Request $request)
    {
        return $this->index($request);
    }

    /**
     * POST /api/formulations
     * Saves a formulation if it does not already exist (case-insensitively).
     * If it already exists, returns the existing single canonical formulation.
     */
    public function store(Request $request)
    {
        $this->authorizeSave($request);

        $validated = $request->validate([
            'name' => ['required', 'string', 'max:255'],
        ]);

        $clean = Formulation::normalizeName($validated['name']);
        if ($clean === '') {
            return response()->json([
                'message' => 'Formulation name cannot be empty.',
                'errors'  => ['name' => ['Formulation name cannot be empty.']],
            ], 422);
        }

        $key = mb_strtolower($clean);
        $alreadyExisted = Schema::hasTable('formulations')
            && Formulation::query()
                ->where('normalized_name', $key)
                ->orWhereRaw('LOWER(name) = ?', [$key])
                ->exists();

        $formulation = Formulation::resolveOrCreate($clean);

        return response()->json([
            'id'          => $formulation?->id,
            'name'        => $formulation?->name ?? $clean,
            'is_existing' => $alreadyExisted,
        ], $alreadyExisted ? 200 : 201);
    }
}

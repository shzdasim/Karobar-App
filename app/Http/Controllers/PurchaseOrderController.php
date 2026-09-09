<?php

namespace App\Http\Controllers;

use App\Authorizables\PurchaseOrderForecast;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;
use Carbon\Carbon;

class PurchaseOrderController extends Controller
{
    public function forecast(Request $request)
    {
        $this->authorize('generate', PurchaseOrderForecast::class);

        // ✅ added optional knobs (packs)
        $data = $request->validate([
            'date_from'      => 'required|date',
            'date_to'        => 'required|date|after_or_equal:date_from',
            'projected_days' => 'required|integer|min:1',
            'supplier_id'    => 'nullable|integer|exists:suppliers,id',
            'brand_id'       => 'nullable|integer|exists:brands,id',
            'brand_ids'      => 'nullable|array',
            'brand_ids.*'    => 'integer|exists:brands,id',
            'safety_packs'   => 'nullable|integer|min:0',
            'moq_packs'      => 'nullable|integer|min:0',
        ]);

        // Merge single brand_id (backward compatible) with multi brand_ids into a unique list
        $brandIds = collect(array_merge(
            isset($data['brand_id']) ? [(int) $data['brand_id']] : [],
            array_map('intval', $data['brand_ids'] ?? [])
        ))->filter()->unique()->values()->all();

        $from = Carbon::parse($data['date_from'])->startOfDay();
        $to   = Carbon::parse($data['date_to'])->endOfDay();
        $days = max(1, $from->diffInDays($to) + 1);
        $proj = (int) $data['projected_days'];

        $safetyPacks = (int) ($data['safety_packs'] ?? 0);
        $moqPacks    = (int) ($data['moq_packs'] ?? 0);

        // --- sales within selected range (STRICT filter) ---
        $salesInRange = DB::table('sale_invoice_items as sii')
            ->join('sale_invoices as si', 'si.id', '=', 'sii.sale_invoice_id')
            ->whereBetween('si.date', [$from->toDateString(), $to->toDateString()])
            ->groupBy('sii.product_id')
            ->select(
                'sii.product_id',
                DB::raw('SUM(sii.quantity) as units_sold'),
                DB::raw('MAX(si.date) as last_sold_date_in_range')
            );

// ---- Products that SOLD in the selected period (STRICT filter) ----
        $soldRows = DB::table('products as p')
            ->joinSub($salesInRange, 'sx', function ($j) {
                $j->on('p.id', '=', 'sx.product_id');
            })
            ->leftJoin('brands as b', 'b.id', '=', 'p.brand_id')
            ->leftJoin('suppliers as s', 's.id', '=', 'p.supplier_id')
            ->when($data['supplier_id'] ?? null, fn($q, $sid) => $q->where('p.supplier_id', $sid))
            ->when(!empty($brandIds), fn($q) => $q->whereIn('p.brand_id', $brandIds))
            ->where('sx.units_sold', '>', 0)
            ->select(
                'p.id as product_id',
                'p.product_code',
                'p.name as product_name',
                'p.pack_size as product_pack_size',
                'p.quantity as current_stock_units',
                'p.unit_purchase_price',
                'p.pack_purchase_price',
                'p.unit_sale_price',
                'p.brand_id',
                'p.supplier_id',
                'b.name as brand_name',
                's.name as supplier_name',
DB::raw('sx.units_sold as units_sold'),
                DB::raw('sx.last_sold_date_in_range as last_sold_date'),
                DB::raw('0 as is_user_demand')
            )
            ->orderByDesc('sx.units_sold')
            ->get();

        // ---- Products with active (pending) user demands, even if they didn't sell ----
        $demandRows = DB::table('user_demands as ud')
            ->join('products as p', 'p.id', '=', 'ud.product_id')
            ->leftJoin('brands as b', 'b.id', '=', 'p.brand_id')
            ->leftJoin('suppliers as s', 's.id', '=', 'p.supplier_id')
            ->where('ud.status', 'pending')
            ->when($data['supplier_id'] ?? null, fn($q, $sid) => $q->where('p.supplier_id', $sid))
            ->when(!empty($brandIds), fn($q) => $q->whereIn('p.brand_id', $brandIds))
            ->groupBy(
                'p.id', 'p.product_code', 'p.name', 'p.pack_size', 'p.quantity',
                'p.unit_purchase_price', 'p.pack_purchase_price', 'p.unit_sale_price',
                'p.brand_id', 'p.supplier_id', 'b.name', 's.name'
            )
            ->select(
                'p.id as product_id',
                'p.product_code',
                'p.name as product_name',
                'p.pack_size as product_pack_size',
                'p.quantity as current_stock_units',
                'p.unit_purchase_price',
                'p.pack_purchase_price',
                'p.unit_sale_price',
                'p.brand_id',
                'p.supplier_id',
                'b.name as brand_name',
                's.name as supplier_name',
                DB::raw('SUM(ud.requested_quantity) as units_sold'),
                DB::raw('MAX(ud.created_at) as last_sold_date'),
                DB::raw('1 as is_user_demand')
            )
            ->get();

// Pending user-demand info per product (customer names + total requested units)
        $demandInfoRows = DB::table('user_demands as ud')
            ->leftJoin('customers as c', 'c.id', '=', 'ud.customer_id')
            ->where('ud.status', 'pending')
            ->groupBy('ud.product_id')
            ->select(
                'ud.product_id',
                DB::raw('GROUP_CONCAT(DISTINCT COALESCE(c.name, ud.requested_name)) as customer_names'),
                DB::raw('SUM(ud.requested_quantity) as demand_quantity')
            )
            ->get()
            ->keyBy(fn ($r) => (int) $r->product_id);

        $pendingDemandProductIds = $demandInfoRows->keys()->all();

        // ---- Products returned via purchase returns within the selected date range ----
        $returnedRows = DB::table('purchase_return_items as pri')
            ->join('purchase_returns as pr', 'pr.id', '=', 'pri.purchase_return_id')
            ->whereDate('pr.date', '>=', $from->toDateString())
            ->whereDate('pr.date', '<=', $to->toDateString())
            ->groupBy('pri.product_id')
            ->select(
                'pri.product_id',
                DB::raw('SUM(pri.return_unit_quantity) as returned_units'),
                DB::raw('MAX(pr.date) as last_purchase_return_date')
            )
            ->get()
            ->keyBy(fn ($r) => (int) $r->product_id);

        // Merge: sold products first, then any user-demand products not already present.
        // Mark sold products that also have a pending user demand.
        $seen = [];
        $rows = collect();
        foreach ($soldRows as $r) {
            $seen[(int) $r->product_id] = true;
            $r->is_user_demand = in_array((int) $r->product_id, $pendingDemandProductIds, true) ? 1 : 0;
            $rows->push($r);
        }
        foreach ($demandRows as $r) {
            if (isset($seen[(int) $r->product_id])) {
                continue;
            }
            $rows->push($r);
        }

        $items = $rows->map(function ($row) use ($days, $proj, $safetyPacks, $moqPacks, $demandInfoRows, $returnedRows) {
            $packSize = max(1, (int) ($row->product_pack_size ?? 0));

            // ===== Demand model (UNITS) =====
            $unitsSold   = (int) ($row->units_sold ?? 0);
            $dailyUnits  = $days > 0 ? ($unitsSold / $days) : 0.0;
            $projUnits   = $dailyUnits * $proj;

            // ===== Stock & safety (UNITS) =====
            $stockUnits  = (int) ($row->current_stock_units ?? 0);
            $safetyUnits = $safetyPacks * $packSize; // request-level safety; extend here if you add per-product RL

            // Projected ending stock after the projection window
            $endingUnits = $stockUnits - $projUnits;

            // Order is DUE iff projected ending stock falls below safety
            $dueUnits    = max(0, $safetyUnits - $endingUnits);
            $suggested   = (int) ceil($dueUnits / $packSize); // round up to whole packs

            // Apply MOQ (packs) if any
            if ($suggested > 0 && $moqPacks > 0) {
                $suggested = max($suggested, $moqPacks);
            }

            // ===== Pricing =====
            $ppu       = (float) ($row->unit_purchase_price ?? 0);
            $ppp       = (float) ($row->pack_purchase_price ?? 0);
            $packPrice = $ppp > 0 ? $ppp : ($ppu > 0 ? $ppu * $packSize : 0);

            // ===== Flags: user demand & purchase return =====
            $demandInfo   = $demandInfoRows->get((int) $row->product_id);
            $returnInfo   = $returnedRows->get((int) $row->product_id);
            $isUserDemand = ((int) ($row->is_user_demand ?? 0) === 1) || $demandInfo !== null;

            return [
                'product_id'            => (int) $row->product_id,
                'product_code'          => $row->product_code,
                'product_name'          => $row->product_name,
                'brand_id'              => $row->brand_id,
                'brand_name'            => $row->brand_name,
                'supplier_id'           => $row->supplier_id,
                'supplier_name'         => $row->supplier_name,
                'pack_size'             => $packSize,
                'units_sold'            => $unitsSold,
                'current_stock_units'   => $stockUnits,
                'projected_days'        => (int) $proj,
                'suggested_packs'       => (int) $suggested,
                'suggested_units'       => (int) ($suggested * $packSize),
                'pack_price'            => round($packPrice, 2),
                'pack_purchase_price'   => (float) $ppp,         // for "Remove Zero"
'last_sold_date'        => $row->last_sold_date, // within range
                'unit_purchase_price'   => $ppu,
                'unit_sale_price'       => $row->unit_sale_price,
                'is_user_demand'        => $isUserDemand ? 1 : 0, // 1 = originated from a user demand
                'user_demand_customers' => $demandInfo?->customer_names, // comma-separated customer names (pending demands)
                'user_demand_quantity'  => $demandInfo ? (int) $demandInfo->demand_quantity : 0,

                'has_purchase_return'       => $returnInfo ? 1 : 0, // 1 = returned via purchase return in the selected range
                'purchase_return_units'     => $returnInfo ? (int) $returnInfo->returned_units : 0,
                'last_purchase_return_date' => $returnInfo ? substr((string) $returnInfo->last_purchase_return_date, 0, 10) : null,

                // Optional: aid debugging in UI (safe to keep or remove)
                'policy' => [
                    'daily_units'  => round($dailyUnits, 4),
                    'proj_units'   => round($projUnits, 2),
                    'ending_units' => round($endingUnits, 2),
                    'safety_units' => $safetyUnits,
                    'due_units'    => round($dueUnits, 2),
                    'moq_packs'    => (int) $moqPacks,
                ],
            ];
        })->values();

        return response()->json([
            'meta' => [
                'date_from'      => $from->toDateString(),
                'date_to'        => $to->toDateString(),
                'days'           => (int) $days,
                'projected_days' => $proj,
                'filter'         => [
                    'supplier_id' => $data['supplier_id'] ?? null,
                    'brand_id'    => $data['brand_id'] ?? null,
                    'brand_ids'   => !empty($brandIds) ? $brandIds : null,
                    'safety_packs'=> $safetyPacks,
                    'moq_packs'   => $moqPacks,
                ],
            ],
            'items' => $items,
        ]);
    }
}

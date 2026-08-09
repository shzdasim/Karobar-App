<?php

namespace App\Http\Controllers;

use App\Models\Product;
use App\Models\User;
use App\Models\UserDemand;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Auth;
use Illuminate\Support\Facades\DB;

class UserDemandController extends Controller
{
    /**
     * Get the currently authenticated user (typed as App\Models\User).
     */
    private function currentUser(): User
    {
        /** @var \App\Models\User|null $user */
        $user = Auth::user();

        if (!$user instanceof User) {
            abort(401, 'Unauthenticated');
        }

        return $user;
    }

    /**
     * GET /api/user-demands
     *
     * List all user demands, optionally filtered by status.
     */
    public function index(Request $request)
    {
        $this->authorize('viewAny', UserDemand::class);

        $status = $request->query('status'); // pending | ordered | fulfilled | cancelled
        $limit  = max(1, min((int) $request->query('limit', 100), 1000));

$query = UserDemand::with(['product:id,name,product_code,quantity,brand_id,supplier_id', 'customer:id,name', 'requester:id,name'])
            ->orderByDesc('id');

        if ($status && in_array($status, ['pending', 'ordered', 'fulfilled', 'cancelled'], true)) {
            $query->where('status', $status);
        }

        $rows = $query->limit($limit)->get();

        // Attach brand/supplier names for convenience
        $rows->each(function ($d) {
            $d->append('product_brand_name')->append('product_supplier_name');
        });

        return response()->json([
            'count' => UserDemand::count(),
            'pending_count' => UserDemand::pending()->count(),
            'rows' => $rows,
        ]);
    }

    /**
     * POST /api/user-demands
     *
     * Create a user demand. If the product name does not match an existing product,
     * a new product is created (with quantity 0) to satisfy the demand.
     */
    public function store(Request $request)
    {
        $this->authorize('create', UserDemand::class);

$validated = $request->validate([
            'product_id'        => 'nullable|integer|exists:products,id',
            'name'              => 'required_without:product_id|string|max:255',
            'brand_id'          => 'required_without:product_id|integer|exists:brands,id',
            'category_id'       => 'nullable|integer|exists:categories,id',
            'supplier_id'       => 'nullable|integer|exists:suppliers,id',
            'customer_id'       => 'nullable|integer|exists:customers,id',
            'pack_size'         => 'required_without:product_id|integer|min:1',
            'requested_quantity'=> 'nullable|integer|min:1',
            'notes'             => 'nullable|string|max:1000',
            'status'            => 'nullable|in:pending,ordered,fulfilled,cancelled',
        ]);

        $user = $this->currentUser();

        return DB::transaction(function () use ($validated, $user) {
            $quantity = (int) ($validated['requested_quantity'] ?? 1);
            $name     = trim($validated['name'] ?? '');

            // Resolve or create the product
            if (!empty($validated['product_id'])) {
                $product = Product::findOrFail($validated['product_id']);
                $name    = $product->name;
            } else {
                $product = Product::where('name', $name)->first();

                if (!$product) {
                    $product = $this->createProductFromDemand($validated);
                }
            }

$demand = UserDemand::create([
                'product_id'         => $product->id,
                'customer_id'        => $validated['customer_id'] ?? null,
                'requested_by'       => $user->id,
                'requested_name'     => $name,
                'requested_quantity' => $quantity,
                'notes'              => $validated['notes'] ?? null,
                'status'             => $validated['status'] ?? 'pending',
            ]);

            return response()->json($demand->load('product', 'customer', 'requester'), 201);
        });
    }

    /**
     * PUT /api/user-demands/{userDemand}
     *
     * Update a demand (mainly its status: ordered / fulfilled / cancelled).
     */
    public function update(Request $request, UserDemand $userDemand)
    {
        $this->authorize('update', $userDemand);

        $validated = $request->validate([
            'status'             => 'sometimes|in:pending,ordered,fulfilled,cancelled',
            'requested_quantity' => 'sometimes|integer|min:1',
            'notes'              => 'nullable|string|max:1000',
        ]);

$userDemand->update($validated);

        return response()->json($userDemand->load('product', 'customer', 'requester'));
    }

    /**
     * DELETE /api/user-demands/{userDemand}
     *
     * Delete a demand.
     */
    public function destroy(UserDemand $userDemand)
    {
        $this->authorize('delete', $userDemand);

        $userDemand->delete();

        return response()->json(['message' => 'User demand deleted']);
    }

    /**
     * GET /api/user-demands/count
     *
     * Quick pending-count endpoint for the notification badge.
     */
    public function count()
    {
        $this->authorize('viewAny', UserDemand::class);

        return response()->json([
            'count'         => UserDemand::count(),
            'pending_count' => UserDemand::pending()->count(),
        ]);
    }

    /**
     * Create a new product from a demand payload.
     */
    private function createProductFromDemand(array $validated): Product
    {
        $lastProduct = Product::orderBy('id', 'desc')->first();

        if ($lastProduct && preg_match('/PRD-(\d+)/', $lastProduct->product_code, $matches)) {
            $newCodeNum = (int) $matches[1] + 1;
        } else {
            $newCodeNum = 1;
        }

        $productCode = 'PRD-' . str_pad($newCodeNum, 4, '0', STR_PAD_LEFT);
        $barcode     = 'PRD' . str_pad((string) random_int(0, 9999999999), 10, '0', STR_PAD_LEFT);

return Product::create([
            'product_code'          => $productCode,
            'name'                  => trim($validated['name']),
            'pack_size'             => (int) ($validated['pack_size'] ?? 1),
            'quantity'              => 0,
            'category_id'           => $validated['category_id'] ?? null,
            'brand_id'              => $validated['brand_id'],
            'supplier_id'           => $validated['supplier_id'] ?? null,
            'barcode'               => $barcode,
            'narcotic'              => 'no',
            'pack_purchase_price'   => 0,
            'pack_sale_price'       => 0,
            'unit_purchase_price'   => 0,
            'unit_sale_price'       => 0,
            'avg_price'             => 0,
            'margin'                => 0,
        ]);
    }
}

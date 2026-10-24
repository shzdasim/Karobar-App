<?php

namespace App\Http\Controllers;

use App\Models\BarcodeTemplate;
use App\Models\Product;
use App\Models\Setting;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;

class BarcodeController extends Controller
{
    /**
     * Get all barcode templates and current store defaults
     * GET /api/barcodes/templates
     */
    public function getTemplates()
    {
        $templates = BarcodeTemplate::orderByDesc('is_default')
            ->orderBy('id')
            ->get();

        $setting = Setting::first();

        return response()->json([
            'templates' => $templates,
            'store_info' => [
                'store_name' => $setting->store_name ?? config('app.name', 'Karobar Pharmacy'),
                'phone_number' => $setting->phone_number ?? '',
                'currency_symbol' => $setting->currency_symbol ?? 'Rs.',
                'currency_position' => $setting->currency_position ?? 'before',
                'currency_decimal_places' => (int)($setting->currency_decimal_places ?? 2),
            ],
        ]);
    }

    /**
     * Store a new custom barcode template
     * POST /api/barcodes/templates
     */
    public function saveTemplate(Request $request)
    {
        $validated = $request->validate([
            'name' => 'required|string|max:100',
            'paper_type' => 'required|in:roll,sheet',
            'width_mm' => 'required|numeric|min:15|max:300',
            'height_mm' => 'required|numeric|min:10|max:300',
            'orientation' => 'required|in:landscape,portrait',
            'columns' => 'nullable|integer|min:1|max:10',
            'rows' => 'nullable|integer|min:1|max:50',
            'gap_x_mm' => 'nullable|numeric|min:0|max:50',
            'gap_y_mm' => 'nullable|numeric|min:0|max:50',
            'margin_top_mm' => 'nullable|numeric|min:0|max:50',
            'margin_left_mm' => 'nullable|numeric|min:0|max:50',
            'margin_right_mm' => 'nullable|numeric|min:0|max:50',
            'margin_bottom_mm' => 'nullable|numeric|min:0|max:50',
            'layout_options' => 'nullable|array',
            'is_default' => 'nullable|boolean',
        ]);

        if (!empty($validated['is_default'])) {
            BarcodeTemplate::where('is_default', true)->update(['is_default' => false]);
        }

        $template = BarcodeTemplate::create($validated);

        return response()->json([
            'message' => 'Template saved successfully',
            'template' => $template,
        ], 201);
    }

    /**
     * Update an existing barcode template
     * PUT /api/barcodes/templates/{id}
     */
    public function updateTemplate(Request $request, $id)
    {
        $template = BarcodeTemplate::findOrFail($id);

        $validated = $request->validate([
            'name' => 'sometimes|required|string|max:100',
            'paper_type' => 'sometimes|required|in:roll,sheet',
            'width_mm' => 'sometimes|required|numeric|min:15|max:300',
            'height_mm' => 'sometimes|required|numeric|min:10|max:300',
            'orientation' => 'sometimes|required|in:landscape,portrait',
            'columns' => 'nullable|integer|min:1|max:10',
            'rows' => 'nullable|integer|min:1|max:50',
            'gap_x_mm' => 'nullable|numeric|min:0|max:50',
            'gap_y_mm' => 'nullable|numeric|min:0|max:50',
            'margin_top_mm' => 'nullable|numeric|min:0|max:50',
            'margin_left_mm' => 'nullable|numeric|min:0|max:50',
            'margin_right_mm' => 'nullable|numeric|min:0|max:50',
            'margin_bottom_mm' => 'nullable|numeric|min:0|max:50',
            'layout_options' => 'nullable|array',
            'is_default' => 'nullable|boolean',
        ]);

        if (!empty($validated['is_default'])) {
            BarcodeTemplate::where('id', '!=', $id)->where('is_default', true)->update(['is_default' => false]);
        }

        $template->update($validated);

        return response()->json([
            'message' => 'Template updated successfully',
            'template' => $template,
        ]);
    }

    /**
     * Delete a custom barcode template
     * DELETE /api/barcodes/templates/{id}
     */
    public function deleteTemplate($id)
    {
        $template = BarcodeTemplate::findOrFail($id);

        // Keep at least one template in the database
        if (BarcodeTemplate::count() <= 1) {
            return response()->json([
                'message' => 'Cannot delete the only remaining template',
            ], 422);
        }

        $wasDefault = $template->is_default;
        $template->delete();

        if ($wasDefault) {
            BarcodeTemplate::query()->first()?->update(['is_default' => true]);
        }

        return response()->json([
            'message' => 'Template deleted successfully',
        ]);
    }

    /**
     * Search products with active batches for barcode label printing
     * GET /api/barcodes/products-search
     */
    public function searchProducts(Request $request)
    {
        $search = trim((string) $request->input('search', ''));
        $categoryId = $request->input('category_id');
        $productIds = $request->input('product_ids');

        $query = Product::with(['batches' => function ($q) {
            $q->orderBy('expiry_date', 'asc');
        }])
        ->select([
            'id',
            'product_code',
            'name',
            'barcode',
            'unit_sale_price',
            'whole_sale_unit_price',
            'quantity',
            'pack_size',
            'category_id',
            'brand_id',
        ]);

        if (!empty($productIds)) {
            $ids = is_array($productIds) ? $productIds : explode(',', (string)$productIds);
            $query->whereIn('id', $ids);
        } elseif ($search !== '') {
            $query->where(function ($q) use ($search) {
                $q->where('name', 'like', "%{$search}%")
                  ->orWhere('barcode', 'like', "%{$search}%")
                  ->orWhere('product_code', 'like', "%{$search}%");
            });
        } elseif (!empty($categoryId)) {
            $query->where('category_id', $categoryId);
        }

        $products = $query->limit(50)->get();

        return response()->json([
            'products' => $products,
        ]);
    }
}


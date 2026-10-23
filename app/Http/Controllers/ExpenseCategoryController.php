<?php

namespace App\Http\Controllers;

use App\Models\ExpenseCategory;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Validation\Rule;

class ExpenseCategoryController extends Controller
{
    public function index(Request $request): JsonResponse
    {
        $query = ExpenseCategory::query();

        if ($request->boolean('active_only', false)) {
            $query->active();
        }

        $categories = $query->withCount('expenses')
            ->orderBy('name')
            ->get();

        return response()->json($categories);
    }

    public function store(Request $request): JsonResponse
    {
        $validated = $request->validate([
            'name'        => ['required', 'string', 'max:100'],
            'code'        => ['nullable', 'string', 'max:50', 'unique:expense_categories,code'],
            'description' => ['nullable', 'string', 'max:500'],
            'is_active'   => ['nullable', 'boolean'],
        ]);

        $category = ExpenseCategory::create($validated);

        return response()->json($category, 201);
    }

    public function show(ExpenseCategory $expenseCategory): JsonResponse
    {
        return response()->json($expenseCategory->loadCount('expenses'));
    }

    public function update(Request $request, ExpenseCategory $expenseCategory): JsonResponse
    {
        $validated = $request->validate([
            'name'        => ['required', 'string', 'max:100'],
            'code'        => ['nullable', 'string', 'max:50', Rule::unique('expense_categories', 'code')->ignore($expenseCategory->id)],
            'description' => ['nullable', 'string', 'max:500'],
            'is_active'   => ['nullable', 'boolean'],
        ]);

        $expenseCategory->update($validated);

        return response()->json($expenseCategory);
    }

    public function destroy(ExpenseCategory $expenseCategory): JsonResponse
    {
        if ($expenseCategory->expenses()->exists()) {
            return response()->json([
                'message' => 'Cannot delete category that contains recorded expenses. Deactivate it instead.',
            ], 422);
        }

        $expenseCategory->delete();

        return response()->json(['message' => 'Expense category deleted successfully.']);
    }
}


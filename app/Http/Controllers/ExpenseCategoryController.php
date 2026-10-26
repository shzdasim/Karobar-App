<?php

namespace App\Http\Controllers;

use App\Models\ExpenseCategory;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Validation\Rule;

class ExpenseCategoryController extends Controller
{
    private function authorizeCategoryAction(string|array $permissions): void
    {
        $user = request()->user();
        if (!$user) {
            abort(401, 'Unauthenticated.');
        }
        if ($user->hasRole(['Admin', 'admin', 'super-admin'])) {
            return;
        }
        foreach ((array) $permissions as $perm) {
            if ($user->can($perm)) {
                return;
            }
        }
        abort(403, 'This action is unauthorized.');
    }

    public function index(Request $request): JsonResponse
    {
        $this->authorizeCategoryAction('expense.view');
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
        $this->authorizeCategoryAction('expense.create');
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
        $this->authorizeCategoryAction('expense.view');
        return response()->json($expenseCategory->loadCount('expenses'));
    }

    public function update(Request $request, ExpenseCategory $expenseCategory): JsonResponse
    {
        $this->authorizeCategoryAction('expense.update');
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
        $this->authorizeCategoryAction('expense.delete');
        if ($expenseCategory->expenses()->exists()) {
            return response()->json([
                'message' => 'Cannot delete category that contains recorded expenses. Deactivate it instead.',
            ], 422);
        }

        $expenseCategory->delete();

        return response()->json(['message' => 'Expense category deleted successfully.']);
    }
}


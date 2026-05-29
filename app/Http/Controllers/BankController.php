<?php

namespace App\Http\Controllers;

use App\Models\Bank;
use Illuminate\Http\Request;

class BankController extends Controller
{
    public function index(Request $request)
    {
        $validated = $request->validate([
            'q' => ['nullable','string','max:100'],
        ]);

        $q = Bank::query();
        if (!empty($validated['q'])) {
            $term = $validated['q'];
            $q->where('bank_name', 'like', "%{$term}%")
              ->orWhere('account_number', 'like', "%{$term}%");
        }

        $banks = $q->orderBy('bank_name')->get();
        return response()->json(['data' => $banks]);
    }

    public function store(Request $request)
    {
        $validated = $request->validate([
            'bank_name'      => ['required','string','max:191'],
            'account_number' => ['required','string','max:191'],
            'image'          => ['nullable','image','max:4096'],
            'balance'        => ['nullable','numeric','min:0'],
        ]);

        $imagePath = null;
        if ($request->hasFile('image')) {
            $imagePath = $request->file('image')->store('banks', 'public');
        }

        $bank = Bank::create([
            'bank_name'      => $validated['bank_name'],
            'account_number' => $validated['account_number'],
            'image_path'     => $imagePath,
            'balance'        => (float)($validated['balance'] ?? 0),
        ]);

        return response()->json(['data' => $bank], 201);
    }

    public function show(Bank $bank)
    {
        return response()->json(['data' => $bank]);
    }

    public function update(Request $request, Bank $bank)
    {
        $validated = $request->validate([
            'bank_name'      => ['sometimes','string','max:191'],
            'account_number' => ['sometimes','string','max:191'],
            'image'          => ['nullable','image','max:4096'],
            'balance'        => ['sometimes','numeric','min:0'],
        ]);

        if ($request->hasFile('image')) {
            $validated['image_path'] = $request->file('image')->store('banks', 'public');
        }

        $bank->fill([
            'bank_name'      => $validated['bank_name'] ?? $bank->bank_name,
            'account_number' => $validated['account_number'] ?? $bank->account_number,
            'image_path'     => $validated['image_path'] ?? $bank->image_path,
            'balance'        => isset($validated['balance']) ? (float)$validated['balance'] : $bank->balance,
        ]);

        $bank->save();
        return response()->json(['data' => $bank]);
    }

    public function destroy(Bank $bank)
    {
        $bank->delete();
        return response()->json(['status' => 'ok']);
    }
}


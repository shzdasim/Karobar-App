<?php

namespace App\Http\Controllers;

use App\Models\Quotation;
use App\Models\Setting;
use Illuminate\Http\Request;

class QuotationPrintController extends Controller
{
    public function print(Request $request, Quotation $quotation)
    {
        $quotation->load(['items']);
        // For compatibility with SaleInvoice printer, also load customer/user
        $quotation->load(['customer', 'user']);

        $setting = Setting::first();

        $type = strtolower($request->query('type', $setting->printer_type ?? 'a4'));
        if (!in_array($type, ['a4', 'thermal'], true)) {
            $type = 'a4';
        }

        if ($type === 'thermal') {
            $thermalTemplate = $setting->thermal_template ?? 'standard';
            $templateName = "quotation_thermal_{$thermalTemplate}";
        } else {
            $a4Template = $setting->a4_template ?? 'standard';
            $templateName = "quotation_a4_{$a4Template}";
        }

        $gross = (float)($quotation->items?->sum('sub_total') ?? 0);
        $disc = (float)($quotation->discount_amount ?? 0);
        $tax  = (float)($quotation->tax_amount ?? 0);
        $total = (float)($quotation->total ?? ($gross - $disc + $tax));

        // quotations: no receive/remaining fields
        $receivedOnQuotation = 0;
        $remainThis = $total;

        $templatePath = "printer.{$templateName}";

        return view($templatePath, [
            'invoice' => $quotation,
            'setting' => $setting,
            'printTotal' => $total,
            'printReceive' => $receivedOnQuotation,
            'printRemainThis' => $remainThis,
            'printCustomerTotalDue' => null,
        ]);
    }
}


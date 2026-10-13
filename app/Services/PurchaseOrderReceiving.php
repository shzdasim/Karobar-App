<?php

namespace App\Services;

use App\Models\PurchaseInvoiceItem;
use App\Models\PurchaseOrder;
use Illuminate\Validation\ValidationException;

class PurchaseOrderReceiving
{
    // Must run inside the invoice transaction. Serializes competing receipts/edits.
    public function validateReceipt(PurchaseOrder $order, array &$data, ?int $excludeInvoiceId = null): void
    {
        if ((int) $data['supplier_id'] !== (int) $order->supplier_id) {
            throw ValidationException::withMessages(['supplier_id' => 'The supplier must match the source Purchase Order.']);
        }
        $items = $order->items()->get()->keyBy('product_id');
        $existing = PurchaseInvoiceItem::whereIn('purchase_order_item_id', $items->pluck('id'))
            ->when($excludeInvoiceId, fn ($query) => $query->where('purchase_invoice_id', '!=', $excludeInvoiceId))
            ->selectRaw('purchase_order_item_id, SUM(unit_quantity) as paid_units')->groupBy('purchase_order_item_id')
            ->pluck('paid_units', 'purchase_order_item_id');
        $received = [];
        foreach ($data['items'] as $i => &$row) {
            $line = $items->get($row['product_id']);
            // Extra delivered products are permitted, but cannot impersonate an order line.
            if (! empty($row['purchase_order_item_id']) && (! $line || (int) $row['purchase_order_item_id'] !== $line->id)) {
                throw ValidationException::withMessages(["items.$i.purchase_order_item_id" => 'Invalid source order item.']);
            }
            $row['purchase_order_item_id'] = $line?->id;
            foreach (['pack_quantity', 'pack_bonus', 'unit_quantity', 'unit_bonus', 'quantity', 'unit_purchase_price', 'pack_purchase_price', 'unit_sale_price', 'pack_sale_price', 'whole_sale_unit_price', 'whole_sale_pack_price'] as $field) {
                if (($row[$field] ?? 0) < 0) {
                    throw ValidationException::withMessages(["items.$i.$field" => 'The value cannot be negative.']);
                }
            }
            if (isset($row['pack_size']) && $row['pack_size'] < 1) {
                throw ValidationException::withMessages(["items.$i.pack_size" => 'Pack size must be positive.']);
            }
            if (($row['item_discount_percentage'] ?? 0) > 100) {
                throw ValidationException::withMessages(["items.$i.item_discount_percentage" => 'Discount cannot exceed 100%.']);
            }
            if ((int) $row['quantity'] !== (int) $row['unit_quantity'] + (int) ($row['unit_bonus'] ?? 0)) {
                throw ValidationException::withMessages(["items.$i.quantity" => 'Stock quantity must equal paid units plus bonus units.']);
            }
            if ($line) {
                $received[$line->id] = ($received[$line->id] ?? 0) + (int) $row['unit_quantity'];
            }
        }
        unset($row);
        if (array_sum($received) <= 0) {
            throw ValidationException::withMessages(['items' => 'Receive at least one ordered unit.']);
        }
        foreach ($items as $line) {
            if (($received[$line->id] ?? 0) + ($existing[$line->id] ?? 0) > $line->ordered_quantity) {
                throw ValidationException::withMessages(['items' => 'Received paid quantity cannot exceed the ordered quantity.']);
            }
        }
    }

    public function sync(PurchaseOrder $order): void
    {
        $order->load('items.invoiceItems', 'invoices');
        $complete = $order->items->every(fn ($line) => $line->invoiceItems->sum('unit_quantity') >= $line->ordered_quantity);
        $order->update([
            'status' => $order->invoices->isNotEmpty() ? ($complete ? 'received' : 'partially_received') : 'ordered',
            'received_by' => $order->invoices->isNotEmpty() ? ($order->received_by ?? auth()->id()) : null,
            'updated_by' => auth()->id(),
            'received_at' => $order->invoices->isNotEmpty() ? ($order->received_at ?? now()) : null,
        ]);
    }
}

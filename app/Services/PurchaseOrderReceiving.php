<?php

namespace App\Services;

use App\Models\Product;
use App\Models\PurchaseOrder;
use App\Models\UserDemand;
use Illuminate\Validation\ValidationException;

class PurchaseOrderReceiving
{
    // Must run inside the invoice transaction. Serializes competing receipts/edits.
    public function validateReceipt(PurchaseOrder $order, array &$data): void
    {
        if ((int) $data['supplier_id'] !== (int) $order->supplier_id) {
            throw ValidationException::withMessages(['supplier_id' => 'The supplier must match the source Purchase Order.']);
        }
        $items = $order->items()->get()->keyBy('product_id');
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

    }

    public function sync(PurchaseOrder $order): void
    {
        $order->load('items.product', 'items.invoiceItems', 'invoices');
        $hasInvoices = $order->invoices->isNotEmpty();
        $complete = $order->items->every(fn ($line) => $line->invoiceItems->sum('unit_quantity') >= $line->ordered_quantity);
        $status = $hasInvoices ? ($complete ? 'received' : 'partially_received') : 'ordered';

        $order->update([
            'status' => $status,
            'received_by' => $hasInvoices ? ($order->received_by ?? auth()->id()) : null,
            'updated_by' => auth()->id(),
            'received_at' => $hasInvoices ? ($order->received_at ?? now()) : null,
        ]);

        $this->syncUndeliveredUserDemands($order, $status);
    }

    private function syncUndeliveredUserDemands(PurchaseOrder $order, string $status): void
    {
        $orderNumber = $order->number ?: ('PO-' . str_pad((string) $order->id, 5, '0', STR_PAD_LEFT));
        $note = "Undelivered from Purchase Order {$orderNumber}";

        if ($status !== 'partially_received') {
            UserDemand::where('notes', $note)
                ->where('status', 'pending')
                ->delete();
            return;
        }

        foreach ($order->items as $line) {
            $receivedUnits = (int) $line->invoiceItems->sum('unit_quantity');
            $remainingUnits = max(0, (int) $line->ordered_quantity - $receivedUnits);

            if ($remainingUnits <= 0) {
                UserDemand::where('product_id', $line->product_id)
                    ->where('notes', $note)
                    ->where('status', 'pending')
                    ->delete();
                continue;
            }

            // Mark any pre-existing pending demands for this product as ordered
            // so the undelivered balance from this PO is the single active pending demand.
            UserDemand::where('product_id', $line->product_id)
                ->where('status', 'pending')
                ->where(function ($query) use ($note) {
                    $query->whereNull('notes')->orWhere('notes', '!=', $note);
                })
                ->update(['status' => 'ordered']);

            $productName = $line->product?->name
                ?? Product::whereKey($line->product_id)->value('name')
                ?? (string) $line->product_id;

            $existing = UserDemand::where('product_id', $line->product_id)
                ->where('notes', $note)
                ->first();

            if ($existing) {
                $existing->update([
                    'requested_name' => $productName,
                    'requested_quantity' => $remainingUnits,
                    'status' => 'pending',
                ]);
            } else {
                UserDemand::create([
                    'product_id' => $line->product_id,
                    'requested_by' => auth()->id() ?? $order->created_by,
                    'requested_name' => $productName,
                    'requested_quantity' => $remainingUnits,
                    'notes' => $note,
                    'status' => 'pending',
                ]);
            }
        }
    }
}

<?php

namespace App\Services;

use App\Models\PurchaseInvoice;
use App\Models\SaleInvoice;
use Illuminate\Support\Facades\DB;
use Illuminate\Validation\ValidationException;

class ReturnEntitlement
{
    public function remaining(string $kind, $invoice, ?int $exclude = null): array
    {
        $sale = $kind === 'sale';
        $headers = $sale ? 'sale_returns' : 'purchase_returns';
        $lines = $sale ? 'sale_return_items' : 'purchase_return_items';
        $link = $sale ? 'sale_invoice_id' : 'purchase_invoice_id';
        $parent = $sale ? 'sale_return_id' : 'purchase_return_id';
        $quantity = $sale ? 'unit_return_quantity' : 'return_unit_quantity';
        $batch = $sale ? 'batch_number' : 'batch';
        $original = [];
        foreach ($invoice->items()->get() as $item) {
            $key = $this->key($item->product_id, $item->$batch, $item->expiry);
            $original[$key] = ($original[$key] ?? 0) + (int) $item->quantity;
        }
        $returned = DB::table($lines.' as l')->join($headers.' as h', 'h.id', '=', 'l.'.$parent)
            ->where('h.'.$link, $invoice->id)->when($exclude, fn ($q) => $q->where('h.id', '!=', $exclude))
            ->select('l.product_id', 'l.'.$batch.' as batch', 'l.expiry', 'l.'.$quantity.' as quantity')->get();
        foreach ($returned as $line) {
            $key = $this->key($line->product_id, $line->batch, $line->expiry);
            $original[$key] = ($original[$key] ?? 0) - (int) $line->quantity;
        }

        return $original;
    }

    public function annotate(string $kind, $invoice, ?int $exclude = null): void
    {
        $remaining = $this->remaining($kind, $invoice, $exclude);
        foreach ($invoice->items as $item) {
            $batch = $kind === 'sale' ? $item->batch_number : $item->batch;
            $item->returnable_quantity = max(0, $remaining[$this->key($item->product_id, $batch, $item->expiry)] ?? 0);
        }
    }

    public function validate(string $kind, array $data, $existing = null): void
    {
        $sale = $kind === 'sale';
        $link = $sale ? 'sale_invoice_id' : 'purchase_invoice_id';
        $party = $sale ? 'customer_id' : 'supplier_id';
        $model = $sale ? SaleInvoice::class : PurchaseInvoice::class;
        // Lock old and new sources in the same order when editing/relinking.
        $ids = array_filter([$data[$link] ?? null, $existing?->$link]);
        $sources = $model::whereIn('id', $ids)->orderBy('id')->lockForUpdate()->get()->keyBy('id');
        if ($existing) {
            $fresh = $existing->newQuery()->whereKey($existing->id)->lockForUpdate()->firstOrFail();
            if ((int) $fresh->$link !== (int) $existing->$link) {
                abort(409, 'This return changed. Reload it before editing.');
            }
            $existing->setRawAttributes($fresh->getAttributes(), true);
            $existing->unsetRelations();
        }
        if (empty($data[$link])) {
            return;
        } // Existing unlinked-return workflow is preserved.
        $invoice = $sources->get($data[$link]);
        if (! $invoice || (int) $invoice->$party !== (int) $data[$party]) {
            throw ValidationException::withMessages([$link => 'The selected invoice does not belong to this '.($sale ? 'customer.' : 'supplier.')]);
        }
        $remaining = $this->remaining($kind, $invoice, $existing?->id);
        $requested = [];
        foreach ($data['items'] as $index => $row) {
            $field = $sale ? 'unit_return_quantity' : 'return_unit_quantity';
            $key = $this->key($row['product_id'], $row[$sale ? 'batch_number' : 'batch'] ?? '', $row['expiry'] ?? '');
            $available = max(0, $remaining[$key] ?? 0);
            $requested[$key] = ($requested[$key] ?? 0) + (int) $row[$field];
            if (! array_key_exists($key, $remaining) || $requested[$key] > $available) {
                throw ValidationException::withMessages(["items.$index.$field" => "Only $available units remain available to return for this product/batch on the selected invoice."]);
            }
        }
    }

    public function lockSource(string $kind, $return): void
    {
        $link = $kind === 'sale' ? 'sale_invoice_id' : 'purchase_invoice_id';
        $model = $kind === 'sale' ? SaleInvoice::class : PurchaseInvoice::class;
        if ($return->$link) {
            $model::whereKey($return->$link)->lockForUpdate()->first();
        }
        $fresh = $return->newQuery()->whereKey($return->id)->lockForUpdate()->firstOrFail();
        if ((int) $fresh->$link !== (int) $return->$link) {
            abort(409, 'This return changed. Reload it before deleting.');
        }
        $return->setRawAttributes($fresh->getAttributes(), true);
        $return->unsetRelations();
    }

    private function key($product, $batch, $expiry): string
    {
        return json_encode([(int) $product, trim((string) $batch), substr(trim((string) $expiry), 0, 10)]);
    }
}

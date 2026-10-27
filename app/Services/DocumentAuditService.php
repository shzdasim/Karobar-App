<?php

namespace App\Services;

use App\Models\DocumentHistory;
use App\Models\User;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Support\Facades\Schema;

class DocumentAuditService
{
    /**
     * Record creation of a business document.
     */
    public function recordCreate(Model $entity, ?User $user = null, ?string $reason = null): ?DocumentHistory
    {
        if (!Schema::hasTable('document_histories')) {
            return null;
        }

        $after = $this->snapshot($entity);

        return DocumentHistory::create([
            'user_id'        => $user?->id ?? auth()->id(),
            'entity_type'    => get_class($entity),
            'entity_id'      => $entity->getKey(),
            'action'         => 'created',
            'reason'         => $reason,
            'before_payload' => null,
            'after_payload'  => $after,
            'diff'           => ['event' => 'Initial document creation'],
        ]);
    }

    /**
     * Record an auditable update with mandatory or optional reason.
     */
    public function recordUpdate(Model $entity, array $before, ?User $user = null, ?string $reason = null): ?DocumentHistory
    {
        if (!Schema::hasTable('document_histories')) {
            return null;
        }

        $after = $this->snapshot($entity);
        $diff = $this->computeDiff($before, $after);

        return DocumentHistory::create([
            'user_id'        => $user?->id ?? auth()->id(),
            'entity_type'    => get_class($entity),
            'entity_id'      => $entity->getKey(),
            'action'         => 'updated',
            'reason'         => $reason,
            'before_payload' => $before,
            'after_payload'  => $after,
            'diff'           => $diff,
        ]);
    }

    /**
     * Record a void or reversal of a posted document.
     */
    public function recordReversal(Model $entity, ?User $user = null, string $reason = ''): ?DocumentHistory
    {
        if (!Schema::hasTable('document_histories')) {
            return null;
        }

        $snapshot = $this->snapshot($entity);

        return DocumentHistory::create([
            'user_id'        => $user?->id ?? auth()->id(),
            'entity_type'    => get_class($entity),
            'entity_id'      => $entity->getKey(),
            'action'         => 'reversed',
            'reason'         => $reason,
            'before_payload' => $snapshot,
            'after_payload'  => array_merge($snapshot, ['status' => 'reversed']),
            'diff'           => ['status' => ['from' => 'posted', 'to' => 'reversed']],
        ]);
    }

    /**
     * Fetch document audit history.
     */
    public function getHistory(string $entityType, int|string $entityId)
    {
        return DocumentHistory::with('user:id,name,email')
            ->where('entity_type', $entityType)
            ->where('entity_id', $entityId)
            ->orderBy('id', 'desc')
            ->get();
    }

    /**
     * Produce a clean, structured JSON snapshot of an invoice or return entity.
     */
    public function snapshot(Model $entity): array
    {
        $data = $entity->toArray();

        // Include items if relation is loaded or exists
        if (method_exists($entity, 'items') && $entity->relationLoaded('items')) {
            $data['items'] = $entity->items->map(function ($it) {
                return [
                    'id'                  => $it->id,
                    'product_id'          => $it->product_id,
                    'product_name'        => $it->product?->name ?? null,
                    'batch_number'        => $it->batch_number ?? null,
                    'quantity'            => $it->quantity ?? ($it->actual_qty ?? 0),
                    'previous_qty'        => $it->previous_qty ?? null,
                    'actual_qty'          => $it->actual_qty ?? null,
                    'diff_qty'            => $it->diff_qty ?? null,
                    'price'               => $it->price ?? ($it->unit_purchase_price ?? ($it->pack_purchase_price ?? null)),
                    'sub_total'           => $it->sub_total ?? ($it->worth_adjusted ?? null),
                ];
            })->toArray();
        }

        return $data;
    }

    /**
     * Compute clean key-level diff between before and after states.
     */
    protected function computeDiff(array $before, array $after): array
    {
        $diff = [];
        $ignored = ['updated_at', 'created_at'];

        foreach ($after as $key => $newVal) {
            if (in_array($key, $ignored)) continue;
            $oldVal = $before[$key] ?? null;

            if ($key === 'items') {
                $itemDiff = $this->computeItemsDiff($oldVal ?? [], $newVal ?? []);
                if (!empty($itemDiff)) {
                    $diff['items'] = $itemDiff;
                }
                continue;
            }

            if ($oldVal != $newVal) {
                $diff[$key] = [
                    'from' => $oldVal,
                    'to'   => $newVal,
                ];
            }
        }

        return $diff;
    }

    protected function computeItemsDiff(array $oldItems, array $newItems): array
    {
        return [
            'before_count' => count($oldItems),
            'after_count'  => count($newItems),
            'old_summary'  => collect($oldItems)->pluck('product_name')->filter()->values(),
            'new_summary'  => collect($newItems)->pluck('product_name')->filter()->values(),
        ];
    }
}

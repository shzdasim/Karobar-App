-- Migration: Create immutable inventory_movements ledger table
-- NOTE: Do NOT apply to production without backup and explicit approval.
CREATE TABLE IF NOT EXISTS "inventory_movements" (
    "id" INTEGER NOT NULL CONSTRAINT "PK_inventory_movements" PRIMARY KEY AUTOINCREMENT,
    "product_id" INTEGER NOT NULL,
    "batch_id" INTEGER NULL,
    "batch_number" TEXT NULL,
    "transaction_type" TEXT NOT NULL DEFAULT '',
    "transaction_id" INTEGER NULL,
    "transaction_item_id" INTEGER NULL,
    "reference_number" TEXT NOT NULL DEFAULT '',
    "quantity_in" TEXT NOT NULL DEFAULT (0),
    "quantity_out" TEXT NOT NULL DEFAULT (0),
    "balance_after" TEXT NOT NULL DEFAULT (0),
    "unit" TEXT NOT NULL DEFAULT 'unit',
    "conversion_factor" TEXT NOT NULL DEFAULT (1),
    "reason" TEXT NULL,
    "user_id" INTEGER NULL,
    "created_at" TEXT NULL
);

CREATE INDEX IF NOT EXISTS "IX_inventory_movements_product_id" ON "inventory_movements" ("product_id");
CREATE INDEX IF NOT EXISTS "IX_inventory_movements_tx" ON "inventory_movements" ("transaction_type", "transaction_id");

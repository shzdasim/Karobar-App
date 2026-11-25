# Training Lesson 4: Inventory & Store Managers

**Objectives:** Forecast & receive Purchase Orders, enter Purchase Invoices with batches/expiry, monitor near-expiry stock, run blind Stocktakes, and quarantine expired goods.

## Instructions & Examples
1. **Reorder & Receive:** Run **Purchase Order Forecast** (`/purchase-orders/forecast`), create a PO, and click **Receive** when cartons arrive to enter Batch # and Expiry Date on the Purchase Invoice.
2. **Expiry & Returns:** Check **Batch & Expiry** (`/batch-expiry`) weekly; send returnable near-expiry batches via **Purchase Returns** (`/purchase-returns/create`).
3. **Stocktake & Quarantine:** Run **Blind Stocktake** (`/stocktake-sessions/create`) by Category/Rack, finalize variances, and move expired/damaged items to **Quarantine** (`/stock-disposals/create`).

## Practice Exercises
1. Create a Purchase Invoice with `10` packs + `1` bonus pack, Batch `B-101`, Expiry `2028-12-31`.
2. Run a Cycle Count Stocktake on one category and finalize a `-1` variance; quarantine 1 expired batch.

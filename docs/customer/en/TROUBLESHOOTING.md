# Customer Troubleshooting & Mistake Recovery Guide (English)

Common issues faced by pharmacy staff and how to fix them safely without damaging stock or accounts.

---

| Problem / Error | Likely Cause | Safe Recovery Steps |
| :--- | :--- | :--- |
| **"Insufficient stock" when selling an item on the shelf** | Physical stock exists, but Purchase Invoice was not entered or was saved on a different batch. | 1. Check `/product-batches` for the item.<br>2. If distributor bill wasn't entered, create a **Purchase Invoice**.<br>3. If count is off after verification, use **Stock Adjustment** (`/stock-adjustments/create`). |
| **"Batch is expired" warning at POS** | Earliest batch is past expiry date. | Do **not** sell expired medicine. Move the batch to **Quarantine (`/stock-disposals/create`)** and select a fresh batch. Only use Supervisor Operator PIN if authorized for a valid near-expiry policy. |
| **Cash Drawer shows shortage or excess at closing** | Cash expense wasn't recorded, or a cash sale/return was marked as Credit/Bank by mistake. | 1. Check `/cash-drawer` transactions for the shift.<br>2. Record any missed shop expense in `/expenses`.<br>3. Edit the misclassified invoice payment method if within the same shift. |
| **Cannot delete a Purchase Invoice** | Stock from that purchase batch has already been sold. | You cannot delete a bill after its stock is sold. Use **Purchase Return** (`/purchase-returns/create`) or void the linked Sale Invoices first. |
| **Cannot delete a Sale Invoice** | A **Sale Return** has already been created against that invoice. | Reverse/delete the linked Sale Return first, or leave the invoice intact for audit history. |
| **Thermal receipt not printing or garbled text** | Wrong printer selected or paper width (58mm vs 80mm) mismatch. | Open **Settings → Printer Settings** (`/settings`), select the exact Windows printer name, match `80mm` or `58mm`, and click **Test Print**. |
| **WhatsApp PDF fails to send** | Local WhatsApp session logged out or phone number missing country code (`92`). | Open **Settings → WhatsApp**, scan the QR code to reconnect, and ensure customer phone numbers start with `92` (e.g., `923001234567`). |
| **LAN Terminal shows "Server Offline"** | Main Server PC is turned off, Wi-Fi IP changed, or firewall blocked the port. | Ensure the Server PC is running Karobar-App, check `/server-dashboard` on the Server, and run **Troubleshoot** in Network Settings. |

![Printer Settings](../../../Screenshots/Printer-Settings.webp)

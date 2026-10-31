// resources/js/data/guideBotScreenBlueprints.js
// Rich field-by-field glossaries, worked numerical examples, 4-pillar accounting impact matrices,
// modal context guides, and live DOM element inspection for the Cube Companion Drawer.

export const SCREEN_BLUEPRINTS = {
  "/dashboard": {
    fields: [
      {
        term: { en: "Today's Sales & Net Profit KPIs", ur: "آج کی سیلز اور خالص منافع" },
        type: "Card",
        en: "Shows today's gross revenue, returns deducted, and net profit after subtracting batch Cost of Goods Sold (COGS) and operating expenses.",
        ur: "یہ کارڈز آج کی کل فروخت، واپسی، اور قیمتِ خرید و اخراجات نکالنے کے بعد خالص منافع دکھاتے ہیں۔",
      },
      {
        term: { en: "Near-Expiry & Expired Batch Alerts", ur: "قریب ایکسپائری اور ایکسپائرڈ بیچ الرٹس" },
        type: "Badge",
        en: "Highlights batches expiring within your configured window (e.g. 90 days) so you can intimate suppliers or sell them first via FEFO.",
        ur: "یہ الرٹس ان بیچز کی نشاندہی کرتے ہیں جو جلد ایکسپائر ہونے والے ہیں تاکہ آپ سپلائر کو بروقت اطلاع دے سکیں۔",
      },
      {
        term: { en: "Low Stock / Reorder Alerts", ur: "کم اسٹاک اور ری آرڈر الرٹس" },
        type: "Badge",
        en: "Lists products whose current total units have dropped at or below their minimum Reorder Level.",
        ur: "وہ آئٹمز جن کا اسٹاک مقررہ حد (Reorder Level) سے کم ہو چکا ہے اور جن کا آرڈر دینا ضروری ہے۔",
      },
      {
        term: { en: "Receivables (Customer Udhar) vs. Payables", ur: "کسٹمر ادھار (وصول طلب) بمقابلہ سپلائر واجبات" },
        type: "Formula",
        en: "Compares total money customers owe your shop (Receivables) against total money you owe distributors (Payables).",
        ur: "گاہکوں سے وصول طلب رقم اور سپلائرز کو واجب الادا رقم کا فوری موازنہ۔",
      },
    ],
    example: {
      title: { en: "Example: Morning Shop Health Check", ur: "مثال: صبح دکان کھولتے وقت جائزہ" },
      en: "Formula: `Net Profit = (Gross Sales − Sale Returns − Discounts) − Batch COGS − Shop Expenses`.\nExample: If today's sales are Rs. 50,000, medicine batch cost (COGS) is Rs. 38,000, and electricity/tea expense is Rs. 1,500, your Net Profit shows **Rs. 10,500**.",
      ur: "فارمولا: `خالص منافع = (کل سیل − واپسی − ڈسکاؤنٹ) − قیمتِ خرید (COGS) − دکان کے اخراجات`۔\nمثال: اگر آج کی سیل 50,000 روپے، ادویات کی لاگت 38,000 روپے اور اخراجات 1,500 روپے ہیں تو خالص منافع **10,500 روپے** ہو گا۔",
    },
    impactMatrix: {
      stock: { en: "Read-only analytics (click alerts to jump to Batches or Reorder PO).", ur: "صرف معلوماتی ڈیش بورڈ (الرٹ پر کلک کر کے بیچ یا آرڈر کھولیں)۔" },
      ledger: { en: "Summarizes live Customer Receivable & Supplier Payable balances.", ur: "کسٹمر اور سپلائر کھاتوں کا مجموعی خلاصہ دکھاتا ہے۔" },
      treasury: { en: "Displays live Cash Drawer & Bank Account liquidity.", ur: "کیش ڈراور اور بینک اکاؤنٹس کا موجودہ بیلنس دکھاتا ہے۔" },
      gl: { en: "Aggregates real-time Revenue, COGS, Expense, and Asset accounts.", ur: "آمدنی، لاگت اور اخراجات کے جنرل لیجر کھاتوں کا خلاصہ۔" },
    },
    commonMistake: {
      en: "Tip: Always check the Topbar 'Cash Drawer' badge before starting morning sales to ensure your shift float is open.",
      ur: "مشورہ: صبح بلنگ شروع کرنے سے پہلے اوپر ٹاپ بار میں کیش ڈراور کا اسٹیٹس ضرور چیک کریں۔",
    },
  },

  "/suppliers": {
    fields: [
      {
        term: { en: "Expiry Return Policy (Before / After Expiry)", ur: "ایکسپائری واپسی کی پالیسی (قبل از ایکسپائری / بعد از ایکسپائری)" },
        type: "Field",
        en: "Defines how many months before expiry (e.g. `3 Months Before`) or after expiry this distributor accepts expired stock returns. The system uses this rule to trigger intimation alerts in time.",
        ur: "یہ طے کرتا ہے کہ سپلائر ایکسپائری سے کتنے ماہ پہلے (مثلاً 3 ماہ قبل) یا بعد میں مال واپس لیتا ہے۔ سسٹم اسی کے مطابق الرٹ دیتا ہے۔",
      },
      {
        term: { en: "Intimation Status (Intimated vs. Pending)", ur: "انٹیمیشن اسٹیٹس (اطلاع دی گئی / باقی ہے)" },
        type: "Badge",
        en: "In pharma distribution, suppliers require advance notice ('Intimation') before a batch expires. Marking a batch/supplier as **Intimated** records that you notified the distributor on time so your return claim stays valid.",
        ur: "ادویات کے کاروبار میں ایکسپائری سے پہلے کمپنی کو پیشگی اطلاع (Intimation) دینا لازمی ہوتا ہے۔ یہ بیج دکھاتا ہے کہ آپ نے سپلائر کو اطلاع دے دی ہے یا ابھی باقی ہے۔",
      },
      {
        term: { en: "Opening Balance (Payable)", ur: "اوپننگ بیلنس (ابتدائی واجب الادا رقم)" },
        type: "Field",
        en: "Any historical balance you already owed this supplier before using Karobar ERP. It initializes their Supplier Ledger.",
        ur: "سافٹ ویئر شروع کرنے سے پہلے کا پرانا بقایا جو آپ نے سپلائر کو ادا کرنا ہے۔",
      },
      {
        term: { en: "Current Payable / Credit Summary", ur: "موجودہ واجب الادا بیلنس (Credit Summary)" },
        type: "Column",
        en: "`Opening Balance + Credit Purchase Bills − Purchase Returns − Payments Made = Current Payable`.",
        ur: "`اوپننگ بیلنس + ادھار پرچیز بل − پرچیز واپسی − ادا شدہ رقم = موجودہ بقایا`۔",
      },
    ],
    example: {
      title: { en: "Example: How Supplier Expiry Intimation Works", ur: "مثال: سپلائر ایکسپائری انٹیمیشن کیسے کام کرتی ہے؟" },
      en: "Example: Suppose **Muller & Phipps (M&P)** has a `3 Months Before Expiry` policy. If a batch expires in **December**, Karobar flags it in **September** so you can send an **Expiry Intimation Letter** to the order booker and claim 100% replacement/credit.",
      ur: "مثال: فرض کریں سپلائر کی پالیسی `ایکسپائری سے 3 ماہ قبل` کی ہے۔ اگر دوائی دسمبر میں ایکسپائر ہو رہی ہے تو سسٹم ستمبر میں ہی الرٹ دے گا تاکہ آپ بروقت انٹیمیشن دے کر نقصان سے بچ سکیں۔",
    },
    impactMatrix: {
      stock: { en: "Links products and batches to their source distributor for return claims.", ur: "پروڈکٹس اور بیچز کو ان کے سپلائر کے ساتھ منسلک کرتا ہے۔" },
      ledger: { en: "Creates a Supplier Payable Ledger; Opening Balance posts as initial credit payable.", ur: "سپلائر کھاتہ بناتا ہے اور اوپننگ بیلنس درج کرتا ہے۔" },
      treasury: { en: "No direct cash movement until a Purchase Bill or Supplier Payment is recorded.", ur: "پروفائل بنانے سے کیش پر اثر نہیں پڑتا جب تک ادائیگی نہ کی جائے۔" },
      gl: { en: "Opening Balance credits Accounts Payable (`2000`) and debits Opening Equity.", ur: "اوپننگ بیلنس اکاؤنٹس پے ایبل (Cr) میں درج ہوتا ہے۔" },
    },
    commonMistake: {
      en: "Warning: Do not edit Opening Balance to record new payments or bills! Always use 'Purchase Invoice' for new bills and 'Supplier Ledger → Record Payment' for cash/bank payouts.",
      ur: "احتیاط: نئی ادائیگی درج کرنے کے لیے اوپننگ بیلنس تبدیل نہ کریں بلکہ سپلائر لیجر میں Record Payment استعمال کریں۔",
    },
  },

  "/products": {
    fields: [
      {
        term: { en: "Pack Size (Units per Pack)", ur: "پیک سائز (ڈبے میں گولیوں/یونٹس کی تعداد)" },
        type: "Field",
        en: "Number of loose units (tablets, capsules, sachets, ampoules) inside 1 full box/pack. Example: If 1 box has 3 strips of 10 tablets, enter `Pack Size = 30`. For a single syrup bottle, enter `1`.",
        ur: "ایک ڈبے میں موجود کھلی گولیوں یا یونٹس کی کل تعداد۔ مثلاً 3 پتوں والا ڈبہ جس میں ہر پتے میں 10 گولیاں ہوں تو پیک سائز `30` لکھیں۔ سیرپ کی بوتل کے لیے `1` لکھیں۔",
      },
      {
        term: { en: "Pack Purchase / Sale Price vs. Unit Price", ur: "پیک قیمت بمقابلہ فی یونٹ قیمت" },
        type: "Formula",
        en: "`Unit Price = Pack Price ÷ Pack Size`. When you enter Pack Price and Pack Size, Karobar automatically calculates the per-tablet/unit price for loose counter sales.",
        ur: "`فی گولی قیمت = ڈبے کی قیمت ÷ پیک سائز`۔ ڈبے کی قیمت لکھنے سے سسٹم ایک گولی کی قیمت خودکار نکال لیتا ہے۔",
      },
      {
        term: { en: "Reorder Level & Max Stock", ur: "ری آرڈر لیول (کم از کم اسٹاک کی حد)" },
        type: "Field",
        en: "When total available units fall below `Reorder Level`, the item appears in Low-Stock Alerts and Smart Purchase Order suggestions.",
        ur: "جب اسٹاک اس تعداد سے کم ہو جائے تو سسٹم کم اسٹاک کا الرٹ دیتا ہے اور پرچیز آرڈر میں شامل کرتا ہے۔",
      },
      {
        term: { en: "Rack / Shelf Location", ur: "ریک / شیلف نمبر" },
        type: "Field",
        en: "Physical cabinet or shelf code (e.g. `Rack A-3`, `Fridge-1`) shown during POS billing so counter staff can find the medicine immediately.",
        ur: "دکان میں دوائی رکھنے کی جگہ (مثلاً `Rack A-3`) تاکہ سیلز مین فوری دوائی نکال سکے۔",
      },
    ],
    example: {
      title: { en: "Example: Setting Up a Tablet Box vs. Syrup", ur: "مثال: گولیوں کے ڈبے اور سیرپ کی انٹری" },
      en: "Example: **Panadol 500mg** box has 200 tablets (`Pack Size = 200`). If Pack Sale Price is **Rs. 600**, the system automatically prices 1 loose tablet at **Rs. 3.00** (`600 ÷ 200`). Selling 10 loose tablets deducts 10 units and leaves 190 units (0 Packs + 190 Loose Units).",
      ur: "مثال: پیناڈول کے ڈبے میں 200 گولیاں ہیں (`Pack Size = 200`)۔ اگر ڈبے کی قیمت **600 روپے** ہے تو ایک گولی کی قیمت خودکار **3 روپے** ہو گی۔ 10 کھلی گولیاں بیچنے پر اسٹاک میں 190 گولیاں باقی رہ جائیں گی۔",
    },
    impactMatrix: {
      stock: { en: "Defines master SKU rules; actual quantities live inside Product Batches.", ur: "پروڈکٹ کے بنیادی اصول طے کرتا ہے؛ اصل تعداد بیچز میں ہوتی ہے۔" },
      ledger: { en: "No party ledger impact until purchased or sold.", ur: "پروڈکٹ کارڈ بنانے سے کھاتے پر کوئی اثر نہیں پڑتا۔" },
      treasury: { en: "No cash impact.", ur: "کیش پر کوئی اثر نہیں۔" },
      gl: { en: "Maps item to Inventory Asset (`1200`) and COGS (`5000`) when transacted.", ur: "خرید و فروخت کے وقت انوینٹری اور COGS کھاتوں سے جڑتا ہے۔" },
    },
    commonMistake: {
      en: "Tip: Set the correct Pack Size before recording Purchase Invoices so pack-to-unit conversions remain accurate.",
      ur: "مشورہ: پرچیز بل ڈالنے سے پہلے پیک سائز درست رکھیں تاکہ کھلی گولیوں کا حساب بالکل ٹھیک رہے۔",
    },
  },

  "/purchase-invoices": {
    fields: [
      {
        term: { en: "Posted Number vs. Supplier Bill #", ur: "سسٹم بل نمبر بمقابلہ سپلائر بل نمبر" },
        type: "Column",
        en: "`Posted Number` (e.g. `PI-00042`) is your internal sequential GRN number; `Supplier Bill #` is the invoice number printed on the distributor's paper bill.",
        ur: "`PI-...` آپ کے سسٹم کا نمبر ہے جبکہ `Supplier Bill #` کمپنی کے کاغذی بل کا نمبر ہے۔",
      },
      {
        term: { en: "Total Amount, Paid & Balance Payable", ur: "کل بل، ادا شدہ رقم اور بقایا" },
        type: "Column",
        en: "Shows total bill value, immediate cash/bank payment made on the spot, and remaining credit balance posted to Supplier Ledger.",
        ur: "بل کی کل رقم، موقع پر ادا کی گئی رقم، اور سپلائر کھاتے میں جانے والا بقایا۔",
      },
    ],
    example: {
      title: { en: "Example: Cash vs. Credit Purchase Bill", ur: "مثال: نقد اور ادھار پرچیز بل" },
      en: "Example: A Purchase Invoice of **Rs. 25,000** with **Paid Amount = Rs. 10,000 (Cash)** deducts Rs. 10,000 from your active Cash Drawer and adds **Rs. 15,000** to the Supplier's Payable Ledger.",
      ur: "مثال: **25,000 روپے** کے پرچیز بل میں اگر **10,000 روپے نقد** ادا کیے جائیں تو 10,000 کیش ڈراور سے کم ہوں گے اور **15,000 روپے** سپلائر کے ادھار کھاتے میں جمع ہو جائیں گے۔",
    },
    impactMatrix: {
      stock: { en: "Adds received packs, loose units, and bonus units into Product Batches.", ur: "نیا اسٹاک اور بونس یونٹس بیچز میں جمع کرتا ہے۔" },
      ledger: { en: "Increases Supplier Payable by `(Total Bill − Paid Amount)`.", ur: "غیر ادا شدہ رقم سپلائر کھاتے میں جمع کرتا ہے۔" },
      treasury: { en: "Deducts `Paid Amount` from Cash Drawer or selected Bank Account.", ur: "ادا شدہ رقم کیش ڈراور یا بینک اکاؤنٹ سے کم کرتا ہے۔" },
      gl: { en: "Debits Inventory (`1200`), Credits Accounts Payable (`2000`) / Cash (`1000`).", ur: "انوینٹری کو ڈیبٹ (Dr) اور سپلائر/کیش کو کریڈٹ (Cr) کرتا ہے۔" },
    },
    commonMistake: {
      en: "Tip: If you paid Rs. 0 when the goods arrived and pay the order booker next week, leave Paid Amount = 0 on the bill and record the payout later in Supplier Ledger.",
      ur: "مشورہ: اگر مال ادھار آیا ہے تو بل میں Paid Amount کو 0 رکھیں اور بعد میں ادائیگی سپلائر لیجر سے درج کریں۔",
    },
  },

  "/purchase-invoices/create": {
    fields: [
      {
        term: { en: "Batch Number & Expiry Date", ur: "بیچ نمبر اور ایکسپائری تاریخ" },
        type: "Field",
        en: "Identifies the exact manufacturing lot and expiration date. Required for FEFO counter sales and supplier expiry return claims.",
        ur: "دوائی کا بیچ نمبر اور ایکسپائری تاریخ۔ یہ FEFO بلنگ اور ایکسپائری واپسی کے لیے نہایت ضروری ہے۔",
      },
      {
        term: { en: "Pack Qty, Loose Units & Bonus Units", ur: "پیک تعداد، کھلے یونٹس اور بونس" },
        type: "Formula",
        en: "`Total Stock Added = (Pack Qty × Pack Size) + Loose Units + Bonus Units`. Bonus units are free units from the supplier—they increase your stock count without increasing the bill payable!",
        ur: "`کل اسٹاک = (پیک × پیک سائز) + کھلے یونٹس + بونس یونٹس`۔ بونس یونٹس مفت اسٹاک کے طور پر جمع ہوتے ہیں اور بل کی رقم نہیں بڑھاتے۔",
      },
      {
        term: { en: "Purchase Price vs. Sale Price (MRP) & Margin %", ur: "قیمتِ خرید بمقابلہ قیمتِ فروخت اور مارجن" },
        type: "Formula",
        en: "Enter Trade/Purchase cost and Retail MRP. The row shows your live profit margin % and updates the batch's selling price.",
        ur: "خرید اور فروخت کی قیمت درج کرنے پر سسٹم فوری منافع کا فیصد (Margin %) دکھاتا ہے۔",
      },
      {
        term: { en: "Line Discount % & Withholding Tax (WHT)", ur: "ڈسکاؤنٹ % اور ودہولڈنگ ٹیکس (WHT)" },
        type: "Field",
        en: "`Net Row Total = (Gross Cost − Trade Discount) + Tax`. Adjusts both your effective batch cost and supplier bill total.",
        ur: "کمپنی ڈسکاؤنٹ اور ٹیکس ایڈجسٹ کر کے خالص بل اور فی یونٹ لاگت نکالتا ہے۔",
      },
      {
        term: { en: "Paid Amount & Payment Method / Account", ur: "ادا شدہ رقم اور ادائیگی کا اکاؤنٹ" },
        type: "Field",
        en: "Leave `0` for full credit (`Udhar` from supplier), or enter the cash/bank amount paid right now.",
        ur: "مکمل ادھار بل کے لیے `0` چھوڑ دیں، یا موقع پر ادا کی گئی نقد/بینک رقم لکھیں۔",
      },
    ],
    example: {
      title: { en: "Example: How Bonus Units Lower Your Effective Cost", ur: "مثال: بونس یونٹس سے اوسط لاگت کیسے کم ہوتی ہے؟" },
      en: "Example: You buy **10 Packs** @ **Rs. 300/pack** (= Rs. 3,000 bill) and receive **2 Bonus Packs** free.\n• Total stock added = **12 Packs**\n• Supplier payable = **Rs. 3,000**\n• Your effective cost per pack drops from Rs. 300 to **Rs. 250** (`3,000 ÷ 12`)!",
      ur: "مثال: آپ نے **300 روپے** کے حساب سے **10 ڈبے** خریدے (کل بل 3,000 روپے) اور کمپنی نے **2 ڈبے بونس** دیے۔\n• اسٹاک میں **12 ڈبے** جمع ہوں گے\n• بل صرف **3,000 روپے** رہے گا\n• آپ کی فی ڈبہ اوسط لاگت کم ہو کر **250 روپے** رہ جائے گی!",
    },
    impactMatrix: {
      stock: { en: "Immediately increments Product Batch stock by Purchased + Bonus units.", ur: "خریدے گئے اور بونس یونٹس فوری طور پر بیچ اسٹاک میں جمع ہوتے ہیں۔" },
      ledger: { en: "Posts Bill Total and any Paid Amount to the Supplier's Ledger.", ur: "بل اور ادائیگی سپلائر کے کھاتے میں درج ہوتی ہے۔" },
      treasury: { en: "If Paid > 0 in Cash, deducts from active Cash Drawer (`cash_purchase`).", ur: "نقد ادائیگی کی صورت میں رقم کیش ڈراور سے منہا ہوتی ہے۔" },
      gl: { en: "Dr Inventory (`1200`), Cr Accounts Payable (`2000`) & Cr Cash/Bank.", ur: "انوینٹری ڈیبٹ اور اکاؤنٹس پے ایبل/کیش کریڈٹ ہوتا ہے۔" },
    },
    commonMistake: {
      en: "Pro-Tip: Press `Ctrl+S` to save immediately, and always verify the Expiry Date so FEFO picks the right batch at the POS counter.",
      ur: "مشورہ: بل محفوظ کرنے کے لیے `Ctrl+S` دبائیں اور ایکسپائری تاریخ ضرور چیک کریں۔",
    },
  },

  "/sale-invoices/pos": {
    fields: [
      {
        term: { en: "Barcode Scanner & Medicine Search (F2)", ur: "بارکوڈ اسکینر اور دوائی سرچ (F2)" },
        type: "Field",
        en: "Scan any product barcode or press `F2` to search by brand name or generic formula/salt. The system automatically selects the earliest-expiring batch (**FEFO**).",
        ur: "بارکوڈ اسکین کریں یا `F2` دبا کر نام یا فارمولے سے سرچ کریں۔ سسٹم خودکار طور پر قریب ایکسپائری والا بیچ منتخب کرتا ہے۔",
      },
      {
        term: { en: "Pack Qty vs. Loose Unit Qty", ur: "مکمل ڈبہ (Pack) بمقابلہ کھلی گولیاں (Loose Units)" },
        type: "Field",
        en: "Enter full boxes in `Pack` or individual tablets/strips in `Units`. Selling loose units automatically breaks a pack and deducts exact unit stock.",
        ur: "پورا ڈبہ بیچنے کے لیے Pack اور کھلی گولیاں بیچنے کے لیے Units میں تعداد لکھیں۔",
      },
      {
        term: { en: "Hold Bill (F8) & Held Carts", ur: "بل ہولڈ کرنا (F8) اور واپس بلانا" },
        type: "Button",
        en: "Temporarily parks the current customer's cart so you can bill the next customer in queue, then restore the held cart with 1 click.",
        ur: "موجودہ گاہک کا بل عارضی طور پر روک کر اگلے گاہک کو فارغ کریں اور بعد میں بل واپس کھول لیں۔",
      },
      {
        term: { en: "Cash Received, Change Due & Payment Account", ur: "وصول شدہ نقد رقم، بقایا واپسی اور اکاؤنٹ" },
        type: "Formula",
        en: "`Change Due = Cash Tendered − Net Bill Total`. For walk-in customers, full payment is collected; for registered customers, any unpaid amount goes to their Udhar Ledger.",
        ur: "گاہک سے لی گئی رقم لکھنے پر سسٹم واپس کرنے والے بقایا پیسے (Change) خودکار دکھاتا ہے۔",
      },
    ],
    example: {
      title: { en: "Example: Selling Loose Tablets with FEFO", ur: "مثال: کھلی گولیاں اور FEFO بیچ سلیکشن" },
      en: "Example: You have Batch A (expires in 2 months) and Batch B (expires in 11 months). When you scan the item at POS, Karobar automatically picks **Batch A** so it sells before expiring!",
      ur: "مثال: اگر آپ کے پاس ایک بیچ 2 ماہ بعد اور دوسرا 11 ماہ بعد ایکسپائر ہو رہا ہے تو POS خودکار طور پر **2 ماہ والا بیچ** پہلے سیل کرے گا تاکہ دوائی ایکسپائر نہ ہو۔",
    },
    impactMatrix: {
      stock: { en: "Deducts exact Packs/Units from the selected Product Batch immediately.", ur: "منتخب بیچ سے فروخت شدہ یونٹس فوری کم ہو جاتے ہیں۔" },
      ledger: { en: "Posts credit/partial balance to Customer Receivable Ledger if a named customer is selected.", ur: "رجسٹرڈ گاہک کے کھاتے میں بقایا ادھار درج کرتا ہے۔" },
      treasury: { en: "Logs `cash_sale` inflow into the active Cash Drawer (or deposits to Bank/Wallet).", ur: "نقد وصولی فوری طور پر کیش ڈراور میں جمع ہو جاتی ہے۔" },
      gl: { en: "Dr Cash/AR, Cr Sales Revenue (`4000`); Dr COGS (`5000`), Cr Inventory (`1200`).", ur: "سیلز ریونیو کریڈٹ اور COGS و کیش ڈیبٹ ہوتے ہیں۔" },
    },
    commonMistake: {
      en: "Tip: Make sure your Cash Drawer shift is OPEN before billing cash customers so every POS receipt reconciles in your evening Z-Report.",
      ur: "مشورہ: نقد بلنگ سے پہلے کیش ڈراور شفٹ ضرور کھول لیں تاکہ شام کی Z-Report بالکل درست نکلے۔",
    },
  },

  "/sale-invoices/create/retail": {
    fields: [
      {
        term: { en: "Customer Selection & Credit Limit Alert", ur: "گاہک کا انتخاب اور ادھار کی حد (Credit Limit)" },
        type: "Field",
        en: "Select `Walk-In Customer` for cash counter sales, or choose a registered customer to view their current `Udhar` balance, default discount %, and credit limit.",
        ur: "نقد بل کے لیے Walk-In منتخب کریں یا ادھار کھاتے والے گاہک کا انتخاب کریں۔",
      },
      {
        term: { en: "Batch Selection & Live Stock Badge", ur: "بیچ کا انتخاب اور موجودہ اسٹاک" },
        type: "Column",
        en: "Shows each batch's expiry date, available units, and retail price. Defaults to the earliest expiring batch (FEFO).",
        ur: "ہر بیچ کی ایکسپائری تاریخ اور موجودہ اسٹاک دکھاتا ہے۔",
      },
      {
        term: { en: "Received Amount vs. Net Receivable (Udhar)", ur: "وصول شدہ رقم بمقابلہ بقایا ادھار" },
        type: "Formula",
        en: "`Posted to Customer Udhar = Net Bill Total − Received Amount`. If a bill is Rs. 2,000 and the customer pays Rs. 500 cash, Rs. 500 enters Cash Drawer and Rs. 1,500 enters Customer Ledger.",
        ur: "`بقایا ادھار = کل بل − وصول شدہ رقم`۔ جو رقم کم وصول ہو گی وہ خودکار گاہک کے کھاتے میں چلی جائے گی۔",
      },
    ],
    example: {
      title: { en: "Example: Partial Cash + Udhar Retail Bill", ur: "مثال: آدھی نقد اور آدھی ادھار بلنگ" },
      en: "Example: Bill Total = **Rs. 1,800** for customer *Ali Khan*. He pays **Rs. 800 Cash** now.\n• **Rs. 800** is added to your active Cash Drawer (`cash_sale`).\n• **Rs. 1,000** is added to Ali Khan's Customer Ledger (`Udhar`).",
      ur: "مثال: علی خان کا کل بل **1,800 روپے** بنا جس میں سے اس نے **800 روپے نقد** دیے۔\n• **800 روپے** کیش ڈراور میں جمع ہوں گے۔\n• **1,000 روپے** علی خان کے ادھار کھاتے میں درج ہو جائیں گے۔",
    },
    impactMatrix: {
      stock: { en: "Deducts sold units from the selected batch.", ur: "منتخب بیچ سے اسٹاک کم کرتا ہے۔" },
      ledger: { en: "Adds unpaid balance to Customer Receivable Ledger.", ur: "باقی رقم گاہک کے کھاتے میں جمع کرتا ہے۔" },
      treasury: { en: "Adds Received Cash to active Cash Drawer or Bank Account.", ur: "وصول شدہ نقد رقم کیش ڈراور میں جمع کرتا ہے۔" },
      gl: { en: "Posts Sales Revenue, COGS, Inventory reduction, and Cash/AR.", ur: "سیلز، اسٹاک اور کیش/ادھار کی ڈبل انٹری پوسٹ کرتا ہے۔" },
    },
    commonMistake: {
      en: "Warning: Walk-In Customer bills cannot be saved with unpaid credit (`Udhar`)—select or create a named Customer if the bill is not paid in full.",
      ur: "احتیاط: Walk-In گاہک کے نام پر ادھار محفوظ نہیں ہو سکتا؛ ادھار کے لیے گاہک کا نام منتخب کرنا لازمی ہے۔",
    },
  },

  "/cash-drawer": {
    fields: [
      {
        term: { en: "Opening Float (Initial Drawer Cash)", ur: "اوپننگ فلوٹ (ابتدائی نقد رقم)" },
        type: "Field",
        en: "The physical coins and notes in the counter drawer when you start your shift (for giving change to customers). You can carry over the last shift's actual closing cash with 1 click.",
        ur: "شفٹ شروع کرتے وقت گلے میں موجود ابتدائی نقدی (بقایا دینے کے لیے)۔ آپ پچھلی شفٹ کا کلوزنگ کیش ایک کلک سے منتقل کر سکتے ہیں۔",
      },
      {
        term: { en: "Cash Inflows Breakdown (+)", ur: "کیش ان فلو (گلے میں آنے والی رقم +)" },
        type: "Card",
        en: "Breaks down all cash entering the drawer into 4 clear buckets: **Cash Sales**, **Customer Udhar Receipts**, **Supplier Return Refunds**, and **Manual Cash In / Bank Withdrawals**. Click any bucket to filter the ledger!",
        ur: "گلے میں آنے والی تمام نقدی کی 4 اقسام: نقد سیلز، گاہکوں سے ادھار وصولی، سپلائر واپسی ریفنڈ، اور مینوئل کیش ان۔ کسی بھی قسم پر کلک کر کے نیچے لسٹ فلٹر کریں۔",
      },
      {
        term: { en: "Cash Outflows Breakdown (−)", ur: "کیش آؤٹ فلو (گلے سے نکلنے والی رقم −)" },
        type: "Card",
        en: "Breaks down all cash leaving the drawer into 6 buckets: **Cash Purchases (GRN)**, **Customer Cash Sale Returns**, **Supplier Payments**, **Shop Expenses**, **Staff Salary Advances**, and **Manual Cash Out / Safe Drops**.",
        ur: "گلے سے نکلنے والی نقدی کی 6 اقسام: نقد خریداری، گاہک کو نقد واپسی، سپلائر کو ادائیگی، دکان کے اخراجات، اسٹاف ایڈوانس، اور مینوئل کیش آؤٹ۔",
      },
      {
        term: { en: "Expected Drawer Cash Formula", ur: "متوقع کیش بیلنس (Expected Balance) کا فارمولا" },
        type: "Formula",
        en: "`Expected Drawer Cash = Opening Float + Total Cash Inflows − Total Cash Outflows`. This is the exact physical cash that should be inside your drawer right now.",
        ur: "`متوقع کیش = اوپننگ فلوٹ + کل کیش ان − کل کیش آؤٹ`۔ اس وقت گلے میں اتنی نقد رقم موجود ہونی چاہیے۔",
      },
      {
        term: { en: "Document & Party Reference Badges", ur: "بل نمبر اور پارٹی کا نام (ریفرنس بیج)" },
        type: "Column",
        en: "Shows the exact invoice or voucher number (`SI-00045`, `EXP-...`, `PMT-...`, `ADV-...`) and the Customer, Supplier, or Employee name. Click any blue badge to open the original record.",
        ur: "ہر لین دین کے سامنے اصل بل نمبر اور گاہک/سپلائر/اسٹاف کا نام دکھاتا ہے۔ نیلے بیج پر کلک کر کے اصل ریکارڈ کھولیں۔",
      },
      {
        term: { en: "PKR Note & Coin Denomination Calculator", ur: "پاکستانی نوٹوں اور سکوں کا کیلکولیٹر" },
        type: "Button",
        en: "Available inside the **Open Drawer** and **Close Drawer** modals. Enter the quantity of Rs. 5000, 1000, 500, 100, 50, 20, 10 notes and coins to auto-sum your physical cash without mental math errors.",
        ur: "شفٹ کھولتے یا بند کرتے وقت 5000، 1000، 500 وغیرہ کے نوٹوں کی تعداد لکھیں، سسٹم خودکار کل رقم گن لے گا۔",
      },
    ],
    example: {
      title: { en: "Example: Full Shift Reconciliation & Z-Report", ur: "مثال: شفٹ کا حساب ملانا اور Z-Report" },
      en: "Example:\n• **Opening Float**: Rs. 5,000\n• **Cash Sales**: +Rs. 18,000 | **Customer Udhar Received**: +Rs. 2,000 (`Total In = +Rs. 20,000`)\n• **Shop Expenses**: −Rs. 1,200 | **Cash Sale Refund**: −Rs. 800 (`Total Out = −Rs. 2,000`)\n• **Expected Drawer Cash**: `5,000 + 20,000 − 2,000 = Rs. 23,000`\nIf you count **Rs. 22,900** in physical notes at closing, the system highlights a **Shortage of −Rs. 100** on the Shift Z-Report.",
      ur: "مثال:\n• **اوپننگ فلوٹ**: 5,000 روپے\n• **نقد سیل**: +18,000 | **ادھار وصولی**: +2,000 (`کل آمد = +20,000 روپے`)\n• **دکان کا خرچہ**: −1,200 | **سیل واپسی**: −800 (`کل اخراج = −2,000 روپے`)\n• **متوقع کیش**: `5,000 + 20,000 − 2,000 = 23,000 روپے`\nاگر گنتی میں **22,900 روپے** نکلیں تو سسٹم **100 روپے کمی (Shortage)** دکھائے گا۔",
    },
    impactMatrix: {
      stock: { en: "No direct stock change (linked invoices/returns handle stock).", ur: "براہِ راست اسٹاک تبدیل نہیں ہوتا۔" },
      ledger: { en: "Displays customer/supplier cash payments recorded from ledgers.", ur: "کھاتوں سے ہونے والی نقد وصولیاں اور ادائیگیاں دکھاتا ہے۔" },
      treasury: { en: "Automatically syncs with the default 'Cash in Hand (Drawer)' account in Financial Accounts (`/accounts`).", ur: "فنانشل اکاؤنٹس میں موجود 'Cash in Hand (Drawer)' کے ساتھ خودکار مطابقت رکھتا ہے۔" },
      gl: { en: "Manual Cash In/Out updates Cash in Hand (`1000`) and Treasury logs.", ur: "مینوئل کیش ان/آؤٹ سے کیش اکاؤنٹ اپڈیٹ ہوتا ہے۔" },
    },
    commonMistake: {
      en: "Important: To move cash from the Drawer to a Bank Account (e.g. evening bank deposit), use Financial Accounts (`/accounts` → Transfer) or record Cash Out with clear notes.",
      ur: "اہم بات: گلے سے بینک میں پیسے جمع کروانے کے لیے `/accounts` میں ٹرانسفر آپشن استعمال کریں۔",
    },
  },

  "/product-batches": {
    fields: [
      {
        term: { en: "Expiry Status Badge (Valid / Expiring Soon / Expired)", ur: "ایکسپائری اسٹیٹس بیج (محفوظ / قریب ایکسپائری / ایکسپائرڈ)" },
        type: "Badge",
        en: "Color-coded indicator: **Green** = healthy shelf life, **Amber** = expiring within alert window (sell first or intimate supplier), **Red** = expired (quarantine/dispose immediately).",
        ur: "سبز = محفوظ، پیلا = جلد ایکسپائر ہونے والا، سرخ = ایکسپائر ہو چکا۔",
      },
      {
        term: { en: "Storage Location (Shop Floor / Godown / Cold Storage)", ur: "اسٹوریج لوکیشن (دکان / گودام / فریج)" },
        type: "Column",
        en: "Shows where the batch is stored. Use **Transfer Location** to move stock between Godown and Shop Floor without changing total stock.",
        ur: "بیچ کہاں رکھا ہے (دکان، گودام یا فریج)۔ لوکیشن ٹرانسفر سے مال ایک جگہ سے دوسری جگہ منتقل کریں۔",
      },
    ],
    example: {
      title: { en: "Example: FEFO & Expiry Management", ur: "مثال: بیچ اور ایکسپائری کنٹرول" },
      en: "Filter by **Expiring in 90 Days** once a week, export the list, and submit Expiry Intimations to your suppliers before their return window closes.",
      ur: "ہر ہفتے 90 دن میں ایکسپائر ہونے والے بیچز فلٹر کریں اور سپلائر کو بروقت اطلاع دیں۔",
    },
    impactMatrix: {
      stock: { en: "Tracks exact unit count, cost, and MRP per lot.", ur: "ہر بیچ کی الگ تعداد، لاگت اور قیمتِ فروخت محفوظ رکھتا ہے۔" },
      ledger: { en: "No party ledger change on location transfers.", ur: "لوکیشن بدلنے سے کسی کھاتے پر اثر نہیں پڑتا۔" },
      treasury: { en: "No cash impact.", ur: "کیش پر کوئی اثر نہیں۔" },
      gl: { en: "Inventory valuation is calculated from active batch quantities × unit cost.", ur: "انوینٹری کی کل مالیت بیچز کی لاگت سے نکلتی ہے۔" },
    },
    commonMistake: {
      en: "Never delete a batch that has historical sales—use Stock Adjustment or Stock Disposal to zero out damaged/expired units.",
      ur: "جس بیچ کی سیل ہو چکی ہو اسے ڈیلیٹ نہ کریں بلکہ اسٹاک ایڈجسٹمنٹ یا ڈسپوزل استعمال کریں۔",
    },
  },

  "/customer-ledger": {
    fields: [
      {
        term: { en: "Debit (Bill / Udhar Added) vs. Credit (Payment / Return)", ur: "ڈیبٹ (نیا ادھار بل) بمقابلہ کریڈٹ (وصولی / واپسی)" },
        type: "Column",
        en: "In a Customer Ledger, **Debit (+)** increases what the customer owes you (Credit Sale Bills), while **Credit (−)** reduces their balance (Cash/Bank Payments received or Sale Returns).",
        ur: "گاہک کے کھاتے میں **ڈیبٹ (+)** سے ادھار بڑھتا ہے (نیا بل) اور **کریڈٹ (−)** سے ادھار کم ہوتا ہے (رقم کی وصولی یا مال واپسی)۔",
      },
      {
        term: { en: "+ Record Payment Button", ur: "رقم وصولی کا بٹن (+ Record Payment)" },
        type: "Button",
        en: "Records cash, bank transfer, or mobile wallet recovery from the customer, reduces their receivable balance, and prints a Payment Receipt.",
        ur: "گاہک سے نقد یا بینک وصولی درج کرتا ہے، بقایا کم کرتا ہے اور رسید پرنٹ کرتا ہے۔",
      },
    ],
    example: {
      title: { en: "Example: Recovering Customer Udhar in Cash", ur: "مثال: گاہک سے نقد ادھار وصولی" },
      en: "Example: Customer owes **Rs. 5,000**. They pay **Rs. 3,000 Cash**.\n• Customer Ledger balance drops to **Rs. 2,000**.\n• Active Cash Drawer increases by **+Rs. 3,000** (`customer_payment`).",
      ur: "مثال: گاہک کے ذمے **5,000 روپے** تھے، اس نے **3,000 روپے نقد** ادا کیے۔\n• گاہک کا بقایا کم ہو کر **2,000 روپے** رہ جائے گا۔\n• کیش ڈراور میں **+3,000 روپے** جمع ہو جائیں گے۔",
    },
    impactMatrix: {
      stock: { en: "No stock change on payment recovery.", ur: "رقم وصولی سے اسٹاک پر اثر نہیں پڑتا۔" },
      ledger: { en: "Reduces Customer Receivable balance immediately.", ur: "گاہک کا واجب الوصول بیلنس کم کرتا ہے۔" },
      treasury: { en: "Adds cash to Cash Drawer (`customer_payment`) or selected Bank/Wallet.", ur: "وصول شدہ رقم کیش ڈراور یا بینک اکاؤنٹ میں جمع ہوتی ہے۔" },
      gl: { en: "Dr Cash/Bank (`1000`/`1100`), Cr Accounts Receivable (`1300`).", ur: "کیش/بینک ڈیبٹ اور اکاؤنٹس ریسیوایبل کریڈٹ ہوتا ہے۔" },
    },
    commonMistake: {
      en: "Always verify whether the customer paid in Physical Cash (goes to Cash Drawer) or EasyPaisa/JazzCash/Bank (select the matching Bank/Wallet account).",
      ur: "رقم وصول کرتے وقت درست اکاؤنٹ (نقد ڈراور یا بینک/ایزی پیسہ) منتخب کریں۔",
    },
  },

  "/supplier-ledger": {
    fields: [
      {
        term: { en: "Credit (Purchase Bill) vs. Debit (Payment / Return)", ur: "کریڈٹ (پرچیز بل) بمقابلہ ڈیبٹ (ادائیگی / واپسی)" },
        type: "Column",
        en: "In a Supplier Ledger, **Credit (+)** increases what you owe the distributor (Purchase Invoices), while **Debit (−)** reduces your payable (Payments made or Purchase Returns).",
        ur: "سپلائر کھاتے میں **کریڈٹ (+)** سے واجب الادا رقم بڑھتی ہے اور **ڈیبٹ (−)** (ادائیگی یا پرچیز واپسی) سے بقایا کم ہوتا ہے۔",
      },
      {
        term: { en: "Record Supplier Payment", ur: "سپلائر کو ادائیگی درج کرنا" },
        type: "Button",
        en: "Records cash, cheque, or bank transfer paid to the supplier and allocates it against open purchase invoices.",
        ur: "سپلائر کو نقد، بینک یا چیک کے ذریعے کی گئی ادائیگی درج کرتا ہے۔",
      },
    ],
    example: {
      title: { en: "Example: Paying an Order Booker from Cash Drawer", ur: "مثال: کیش ڈراور سے سپلائر کو ادائیگی" },
      en: "Example: You owe a distributor **Rs. 40,000** and pay **Rs. 15,000 Cash** from the counter drawer.\n• Supplier Payable drops to **Rs. 25,000**.\n• Active Cash Drawer decreases by **−Rs. 15,000** (`supplier_payment`).",
      ur: "مثال: سپلائر کے **40,000 روپے** واجب الادا تھے، آپ نے گلے سے **15,000 روپے نقد** ادا کیے۔\n• سپلائر کا بقایا **25,000 روپے** رہ جائے گا۔\n• کیش ڈراور سے **15,000 روپے** کم ہو جائیں گے۔",
    },
    impactMatrix: {
      stock: { en: "No stock change on payment.", ur: "ادائیگی سے اسٹاک تبدیل نہیں ہوتا۔" },
      ledger: { en: "Reduces Supplier Payable balance.", ur: "سپلائر کا واجب الادا بیلنس کم کرتا ہے۔" },
      treasury: { en: "Deducts from Cash Drawer (`supplier_payment`) or Bank Account.", ur: "کیش ڈراور یا بینک اکاؤنٹ سے رقم منہا کرتا ہے۔" },
      gl: { en: "Dr Accounts Payable (`2000`), Cr Cash/Bank (`1000`/`1100`).", ur: "اکاؤنٹس پے ایبل ڈیبٹ اور کیش/بینک کریڈٹ ہوتا ہے۔" },
    },
    commonMistake: {
      en: "If paying by Cheque or Bank Transfer, select your Bank Account instead of Cash so your physical Cash Drawer expected balance stays accurate.",
      ur: "اگر بینک یا چیک سے ادائیگی کی ہے تو Cash کی بجائے Bank اکاؤنٹ منتخب کریں تاکہ گلے کا کیش خراب نہ ہو۔",
    },
  },

  "/accounts": {
    fields: [
      {
        term: { en: "Cash in Hand (Drawer) — Default Cash Account", ur: "کیش ان ہینڈ (ڈراور) — بنیادی کیش اکاؤنٹ" },
        type: "Card",
        en: "Automatically synchronized with your active Cash Drawer shift (`Safe Opening Reserve + Open Shift Expected Cash`).",
        ur: "یہ اکاؤنٹ آپ کے فعال کیش ڈراور کے بیلنس کے ساتھ خودکار طور پر منسلک رہتا ہے۔",
      },
      {
        term: { en: "Inter-Account Transfer (Drawer ↔ Bank / Safe)", ur: "اکاؤنٹ ٹرانسفر (ڈراور سے بینک یا سیف میں منتقلی)" },
        type: "Button",
        en: "Moves money between two treasury accounts (e.g. depositing Rs. 50,000 from Cash Drawer into HBL Bank). Automatically logs the matching `cash_out` / `cash_in` in your open Cash Drawer shift!",
        ur: "ایک اکاؤنٹ سے دوسرے اکاؤنٹ میں رقم منتقل کرتا ہے (مثلاً گلے سے بینک میں پیسے جمع کروانا)۔",
      },
    ],
    example: {
      title: { en: "Example: Evening Bank Deposit from Drawer", ur: "مثال: گلے سے بینک میں کیش جمع کروانا" },
      en: "Example: Your drawer has **Rs. 35,000** and you deposit **Rs. 25,000** into **Meezan Bank**.\nUse **Transfer** (`From: Cash in Hand` → `To: Meezan Bank`, `Rs. 25,000`). Your Drawer Expected Cash becomes **Rs. 10,000** and Meezan Bank increases by **+Rs. 25,000**.",
      ur: "مثال: گلے میں **35,000 روپے** ہیں اور آپ **25,000 روپے** میزان بینک میں جمع کرواتے ہیں۔\n**Transfer** استعمال کریں۔ گلے میں متوقع کیش **10,000 روپے** رہ جائے گا اور بینک بیلنس **25,000 روپے** بڑھ جائے گا۔",
    },
    impactMatrix: {
      stock: { en: "No stock impact.", ur: "اسٹاک پر کوئی اثر نہیں۔" },
      ledger: { en: "No customer/supplier ledger change on internal transfers.", ur: "اندرونی ٹرانسفر سے گاہک یا سپلائر کھاتے متاثر نہیں ہوتے۔" },
      treasury: { en: "Updates source & destination Account balances + Cash Drawer log.", ur: "دونوں اکاؤنٹس اور کیش ڈراور کا بیلنس اپڈیٹ کرتا ہے۔" },
      gl: { en: "Posts balanced asset transfer between Cash (`1000`) and Bank (`1100`).", ur: "کیش اور بینک کے درمیان متوازن جنرل لیجر انٹری کرتا ہے۔" },
    },
    commonMistake: {
      en: "Never record a bank deposit as an 'Expense'—a bank deposit is an asset transfer (`Transfer`), not a shop cost!",
      ur: "بینک میں جمع کروائی گئی رقم کو کبھی بھی 'Expense' (خرچہ) میں نہ لکھیں بلکہ 'Transfer' استعمال کریں۔",
    },
  },

  "/expenses": {
    fields: [
      {
        term: { en: "Expense Category & Payee", ur: "خرچے کی قسم (Category) اور وصول کنندہ" },
        type: "Field",
        en: "Classifies the operating cost (e.g. Electricity, Shop Rent, Tea/Refreshments, Packaging) for your Profit & Loss statement.",
        ur: "نفع و نقصان کی رپورٹ کے لیے خرچے کی مد (مثلاً بجلی بل، کرایہ، چائے پانی)۔",
      },
      {
        term: { en: "Payment Account (Cash Drawer vs. Bank)", ur: "ادائیگی کا اکاؤنٹ (کیش ڈراور یا بینک)" },
        type: "Field",
        en: "If paid in Cash while a shift is open, it automatically deducts from your active **Cash Drawer** (`Operating Shop Expense`).",
        ur: "نقد ادائیگی کی صورت میں رقم خودکار طور پر فعال کیش ڈراور سے منہا ہو جاتی ہے۔",
      },
    ],
    example: {
      title: { en: "Example: Recording Daily Shop Petty Cash Expense", ur: "مثال: دکان کا روزمرہ خرچہ درج کرنا" },
      en: "Example: Paying **Rs. 450** cash for shop refreshments.\n• Deducts **−Rs. 450** from active Cash Drawer.\n• Adds **Rs. 450** to Operating Expenses on your Profit & Loss report.",
      ur: "مثال: چائے پانی کے لیے گلے سے **450 روپے** ادا کیے۔\n• کیش ڈراور سے **450 روپے** کم ہو جائیں گے۔\n• نفع و نقصان رپورٹ میں **450 روپے** خرچہ درج ہو جائے گا۔",
    },
    impactMatrix: {
      stock: { en: "No inventory impact.", ur: "اسٹاک پر کوئی اثر نہیں۔" },
      ledger: { en: "No customer/supplier ledger impact.", ur: "گاہک یا سپلائر کھاتے پر اثر نہیں۔" },
      treasury: { en: "Deducts from active Cash Drawer (`expense`) or selected Bank Account.", ur: "کیش ڈراور یا بینک اکاؤنٹ سے رقم کم کرتا ہے۔" },
      gl: { en: "Dr Operating Expense (`6xxx`), Cr Cash/Bank (`1000`/`1100`).", ur: "خرچہ اکاؤنٹ ڈیبٹ اور کیش/بینک کریڈٹ ہوتا ہے۔" },
    },
    commonMistake: {
      en: "Do not record medicine purchases or supplier payments under Expenses—those belong in Purchase Invoices and Supplier Ledger.",
      ur: "ادویات کی خریداری یا سپلائر کی ادائیگی کو اخراجات (Expenses) میں درج نہ کریں۔",
    },
  },

  "/accounting": {
    fields: [
      {
        term: { en: "Chart of Accounts (COA)", ur: "چارٹ آف اکاؤنٹس (کھاتوں کی فہرست)" },
        type: "Field",
        en: "Organized into 5 accounting groups: **1xxx Assets** (Cash, Bank, Inventory, Receivables), **2xxx Liabilities** (Supplier Payables), **3xxx Equity** (Capital), **4xxx Revenue** (Sales), and **5xxx/6xxx Expenses** (COGS & Shop Expenses).",
        ur: "کاروبار کے 5 بنیادی کھاتے: اثاثے (Assets)، واجبات (Liabilities)، سرمایہ (Equity)، آمدنی (Revenue) اور اخراجات (Expenses)۔",
      },
      {
        term: { en: "Trial Balance (Dr = Cr Check)", ur: "ٹرائل بیلنس (ڈیبٹ اور کریڈٹ کی برابری)" },
        type: "Formula",
        en: "`Total Debits (Dr) = Total Credits (Cr)`. Verifies that every single invoice, return, expense, and payment in the ERP is mathematically balanced.",
        ur: "تصدیق کرتا ہے کہ سسٹم کے تمام ڈیبٹ اور کریڈٹ سوفیصد برابر ہیں۔",
      },
      {
        term: { en: "Profit & Loss vs. Balance Sheet", ur: "نفع و نقصان بمقابلہ بیلنس شیٹ" },
        type: "Card",
        en: "**Profit & Loss** shows `Revenue − COGS − Expenses = Net Profit` for a date range. **Balance Sheet** shows `Assets = Liabilities + Equity` as of a specific date.",
        ur: "**نفع و نقصان** مخصوص مدت کی کمائی دکھاتا ہے جبکہ **بیلنس شیٹ** کاروبار کے کل اثاثے اور واجبات دکھاتی ہے۔",
      },
    ],
    example: {
      title: { en: "Example: How Auto-Posting Works Behind the Scenes", ur: "مثال: خودکار ڈبل انٹری کیسے کام کرتی ہے؟" },
      en: "You don't need to write manual journal entries for daily sales or purchases! Every POS bill automatically posts:\n1. `Dr Cash / Receivable` & `Cr Sales Revenue`\n2. `Dr Cost of Goods Sold (COGS)` & `Cr Inventory Asset`",
      ur: "آپ کو روزمرہ بلنگ کے لیے خود جنرل انٹری کرنے کی ضرورت نہیں—ہر سیل اور پرچیز بل خودکار طور پر ڈبل انٹری لیجر میں پوسٹ ہوتا ہے۔",
    },
    impactMatrix: {
      stock: { en: "Reflects live Inventory Asset (`1200`) valuation.", ur: "انوینٹری اثاثے کی کل مالیت دکھاتا ہے۔" },
      ledger: { en: "Reconciles with Customer AR (`1300`) and Supplier AP (`2000`).", ur: "کسٹمر اور سپلائر کنٹرول اکاؤنٹس سے مطابقت رکھتا ہے۔" },
      treasury: { en: "Reconciles with Cash (`1000`) and Bank (`1100`) accounts.", ur: "کیش اور بینک کھاتوں سے مطابقت رکھتا ہے۔" },
      gl: { en: "Master Double-Entry General Ledger & Financial Statements.", ur: "مرکزی جنرل لیجر اور مالیاتی رپورٹس۔" },
    },
    commonMistake: {
      en: "Use Manual Journal Entries only for accountant adjustments (e.g. depreciation, owner capital injection, or tax provisions).",
      ur: "مینوئل جنرل انٹری صرف اکاؤنٹنگ ایڈجسٹمنٹ یا سرمایہ شامل کرنے کے لیے استعمال کریں۔",
    },
  },

  "/hr-payroll": {
    fields: [
      {
        term: { en: "Employee Attendance & ZKTeco Biometric Sync", ur: "اسٹاف حاضری اور بائیومیٹرک مشین سنک" },
        type: "Field",
        en: "Tracks daily Present, Absent, Late, and Leave status—either marked manually or pulled directly from your ZKTeco fingerprint device over LAN.",
        ur: "اسٹاف کی روزانہ حاضری، چھٹی اور بائیومیٹرک مشین سے خودکار حاضری کا ریکارڈ۔",
      },
      {
        term: { en: "Staff Salary Advances vs. Monthly Payroll", ur: "اسٹاف ایڈوانس بمقابلہ ماہانہ تنخواہ (Payroll)" },
        type: "Formula",
        en: "`Net Payable Salary = Basic Salary + Sales Commissions + Allowances − Attendance Deductions − Advance Recovery`. Issuing a cash advance mid-month automatically deducts from your open Cash Drawer (`Staff Salary Advance`)!",
        ur: "`خالص تنخواہ = بنیادی تنخواہ + کمیشن − غیر حاضری کٹوتی − ایڈوانس کی واپسی`۔ مہینے کے درمیان نقد ایڈوانس دینے سے رقم کیش ڈراور سے کم ہو جاتی ہے۔",
      },
    ],
    example: {
      title: { en: "Example: Mid-Month Advance & Month-End Salary", ur: "مثال: ایڈوانس کٹوتی اور ماہانہ تنخواہ" },
      en: "Example: Salesman *Bilal* has **Rs. 30,000** salary, earns **Rs. 2,500** commission, and took a **Rs. 5,000** cash advance on the 15th.\nAt month-end payroll, the system auto-deducts the Rs. 5,000 advance so net payout is **Rs. 27,500**.",
      ur: "مثال: بلال کی تنخواہ **30,000 روپے** اور کمیشن **2,500 روپے** ہے، جبکہ اس نے 15 تاریخ کو **5,000 روپے** ایڈوانس لیا۔\nمہینے کے آخر میں پے رول بناتے وقت 5,000 ایڈوانس کٹ کر بقایا تنخواہ **27,500 روپے** ادا ہو گی۔",
    },
    impactMatrix: {
      stock: { en: "No stock impact.", ur: "اسٹاک پر کوئی اثر نہیں۔" },
      ledger: { en: "Maintains individual Employee Advance & Salary Ledgers.", ur: "ہر ملازم کے ایڈوانس اور تنخواہ کا کھاتہ رکھتا ہے۔" },
      treasury: { en: "Deducts cash advances & salary payouts from Cash Drawer or Bank.", ur: "نقد ایڈوانس اور تنخواہ کیش ڈراور یا بینک سے منہا کرتا ہے۔" },
      gl: { en: "Posts Staff Advance Asset (`1400`) and Salary Expense (`6100`).", ur: "اسٹاف ایڈوانس اور تنخواہ کے اخراجات جنرل لیجر میں پوسٹ کرتا ہے۔" },
    },
    commonMistake: {
      en: "Always record mid-month staff loans under the 'Advances & Loans' tab so payroll automatically recovers them at month-end!",
      ur: "اسٹاف کو ادھار یا ایڈوانس دیتے وقت 'Advances' ٹیب میں درج کریں تاکہ تنخواہ سے خودکار کٹوتی ہو سکے۔",
    },
  },

  "/purchase-orders": {
    fields: [
      {
        term: { en: "Load Low-Stock / Smart Suggestions", ur: "لو اسٹاک اور ڈیمانڈ آئٹمز خودکار لوڈ کریں" },
        type: "Action",
        en: "Automatically populates the order table with products below their Reorder Alert Level plus pending items from the Customer Shortage Book.",
        ur: "کم اسٹاک والی اشیاء اور گاہکوں کی ڈیمانڈ بُک والے آئٹمز ایک کلک سے آرڈر میں شامل کرتا ہے۔",
      },
      {
        term: { en: "Convert to Purchase Invoice (GRN)", ur: "پرچیز انوائس (بل) میں تبدیل کریں" },
        type: "Action",
        en: "When the supplier delivers the ordered goods, click Convert to open a pre-filled Purchase Invoice where you only need to enter Batch # and Expiry Date.",
        ur: "جب سپلائر مال پہنچا دے تو آرڈر کو ایک کلک سے پرچیز انوائس میں تبدیل کریں اور صرف بیچ اور ایکسپائری درج کریں۔",
      },
    ],
    example: {
      title: { en: "Example: Purchase Order vs. Purchase Invoice", ur: "مثال: پرچیز آرڈر اور پرچیز بل میں فرق" },
      en: "A **Purchase Order (PO)** is just a demand list sent to the distributor (via PDF/WhatsApp) — it **does not** change your stock or ledger.\nOnce the delivery arrives, convert it to a **Purchase Invoice** to add the stock!",
      ur: "**پرچیز آرڈر (PO)** صرف سپلائر کو بھیجی جانے والی ڈیمانڈ لسٹ ہے—اس سے اسٹاک یا کھاتہ تبدیل نہیں ہوتا۔ مال آنے پر اسے **Purchase Invoice** میں تبدیل کرنے سے اسٹاک جمع ہوتا ہے۔",
    },
    impactMatrix: {
      stock: { en: "No stock change until converted into a Purchase Invoice.", ur: "پرچیز انوائس بننے تک اسٹاک تبدیل نہیں ہوتا۔" },
      ledger: { en: "No payable posted until goods are received.", ur: "مال وصول ہونے تک سپلائر کھاتے پر اثر نہیں پڑتا۔" },
      treasury: { en: "No cash or bank movement.", ur: "کیش یا بینک پر کوئی اثر نہیں۔" },
      gl: { en: "Non-posting procurement document.", ur: "جنرل لیجر میں کوئی انٹری نہیں ہوتی۔" },
    },
    commonMistake: {
      en: "If your stock didn't increase after saving a Purchase Order, remember to click 'Convert to Purchase Invoice' when the delivery arrives!",
      ur: "صرف پرچیز آرڈر بنانے سے اسٹاک نہیں بڑھتا—مال آنے پر اسے Purchase Invoice میں تبدیل کرنا ضروری ہے۔",
    },
  },

  "/sale-returns": {
    fields: [
      {
        term: { en: "Load Original Sale Invoice", ur: "اصل سیل بل سے آئٹمز لوڈ کریں" },
        type: "Field",
        en: "Select the customer's original Sale Invoice to automatically pull the exact batch sold, unit price, and discount given on that bill.",
        ur: "گاہک کا اصل سیل بل منتخب کرنے سے فروخت شدہ بیچ، قیمت اور ڈسکاؤنٹ خودکار لوڈ ہو جاتے ہیں۔",
      },
      {
        term: { en: "Refund Method (Cash vs. Ledger Credit)", ur: "رقم کی واپسی کا طریقہ (نقد یا کھاتہ ایڈجسٹمنٹ)" },
        type: "Field",
        en: "Choose whether you handed physical cash back from the Cash Drawer or credited the customer's Udhar Ledger balance.",
        ur: "طے کریں کہ گاہک کو گلے سے نقد رقم واپس دی گئی ہے یا اس کے ادھار کھاتے سے رقم کم کی گئی ہے۔",
      },
    ],
    example: {
      title: { en: "Example: Returning 1 Strip from a Sale Bill", ur: "مثال: فروخت شدہ بل میں سے 1 پتا واپس لینا" },
      en: "Example: Customer returns **10 tablets (Rs. 150)** from Invoice `SI-00120` for cash.\n• Batch stock increases by **+10 units**\n• Cash Drawer expected cash decreases by **−Rs. 150** (`Sale Return Refund`)\n• Sales revenue & COGS reverse automatically.",
      ur: "مثال: گاہک نے بل `SI-00120` میں سے **10 گولیاں (150 روپے)** واپس کیں:\n• اسٹاک میں **10 یونٹس** واپس جمع ہو جائیں گے\n• کیش ڈراور سے **150 روپے** کم ہو جائیں گے۔",
    },
    impactMatrix: {
      stock: { en: "Restores returned units back into the original Product Batch (+).", ur: "واپس آنے والے یونٹس دوبارہ اسی بیچ کے اسٹاک میں جمع کرتا ہے۔" },
      ledger: { en: "Credits Customer Ledger if adjusted against Udhar.", ur: "ادھار گاہک کے کھاتے سے بقایا کم کرتا ہے۔" },
      treasury: { en: "Deducts Cash Refund from open Cash Drawer (`sale_return`).", ur: "نقد واپسی کی صورت میں کیش ڈراور سے رقم منہا کرتا ہے۔" },
      gl: { en: "Dr Sales Returns & Cr Cash/AR; Dr Inventory & Cr COGS.", ur: "سیلز ریٹرن اور انوینٹری کی ڈبل انٹری پوسٹ کرتا ہے۔" },
    },
    commonMistake: {
      en: "Always link the Sale Return to the original invoice when possible so the exact batch and discount rate are restored.",
      ur: "ہمیشہ اصل سیل بل منتخب کر کے ریٹرن ڈالیں تاکہ درست بیچ اور ڈسکاؤنٹ لاگو ہو۔",
    },
  },

  "/purchase-returns": {
    fields: [
      {
        term: { en: "Batch Selection & Return Qty", ur: "بیچ کا انتخاب اور واپسی کی مقدار" },
        type: "Field",
        en: "Select the exact supplier batch being returned (e.g. near-expiry or damaged stock) so units are deducted from the right batch.",
        ur: "سپلائر کو واپس بھیجا جانے والا مخصوص بیچ اور مقدار منتخب کریں۔",
      },
      {
        term: { en: "Debit Note / Supplier Ledger Adjustment", ur: "ڈیبٹ نوٹ اور سپلائر کھاتہ ایڈجسٹمنٹ" },
        type: "Formula",
        en: "Automatically reduces your Supplier Payable balance (`Payable − Return Total`) or logs a cash refund received from the distributor.",
        ur: "سپلائر کے واجب الادا کھاتے سے واپسی کی رقم خودکار کم کر دیتا ہے۔",
      },
    ],
    example: {
      title: { en: "Example: Returning Near-Expiry Stock to Distributor", ur: "مثال: قریب ایکسپائری مال ڈسٹری بیوٹر کو واپس بھیجنا" },
      en: "Example: You return **5 packs** of a near-expiry batch worth **Rs. 4,000** to *Premier Distributors*.\n• Batch stock decreases by **−5 packs**\n• Your payable to Premier Distributors drops by **Rs. 4,000**\n• Print the **Debit Note PDF** and hand it to their deliveryman.",
      ur: "مثال: آپ نے **4,000 روپے** کے 5 پیک سپلائر کو واپس بھیجے:\n• اسٹاک سے 5 پیک کم ہو جائیں گے\n• سپلائر کے کھاتے سے 4,000 روپے کم ہو جائیں گے۔",
    },
    impactMatrix: {
      stock: { en: "Deducts returned units from the selected Product Batch (−).", ur: "منتخب بیچ کے اسٹاک سے واپس کردہ یونٹس کم کرتا ہے۔" },
      ledger: { en: "Debits Supplier Ledger, reducing your net payable balance.", ur: "سپلائر کو واجب الادا رقم (Payable) میں کمی کرتا ہے۔" },
      treasury: { en: "Records Cash Inflow if the supplier refunds cash immediately.", ur: "نقد واپسی ملنے پر کیش ڈراور میں اضافہ کرتا ہے۔" },
      gl: { en: "Dr Accounts Payable (`2000`), Cr Inventory Asset (`1200`).", ur: "سپلائر پے ایبل کو ڈیبٹ اور انوینٹری کو کریڈٹ کرتا ہے۔" },
    },
    commonMistake: {
      en: "Check the Supplier's 'Expiry Return Policy' and 'Intimation Status' on the Suppliers screen before their return window closes!",
      ur: "سپلائر کی ایکسپائری واپسی کی مدت ختم ہونے سے پہلے انٹیمیشن اور پرچیز ریٹرن مکمل کریں۔",
    },
  },

  "/stock-adjustments": {
    fields: [
      {
        term: { en: "Adjustment Type (Add + vs. Deduct −)", ur: "ایڈجسٹمنٹ کی قسم (اضافہ + یا کمی −)" },
        type: "Field",
        en: "Choose **Add (+)** for found stock or initial opening stock, or **Deduct (−)** for shelf shrinkage, internal clinic use, or unrecorded breakage.",
        ur: "ملنے والے یا ابتدائی اسٹاک کے لیے **Add (+)** اور کم نکلنے والے مال کے لیے **Deduct (−)** منتخب کریں۔",
      },
      {
        term: { en: "Reason / Audit Note", ur: "ایڈجسٹمنٹ کی وجہ (Audit Note)" },
        type: "Field",
        en: "Mandatory explanation recorded permanently in the adjustment register so managers can audit why stock was changed manually.",
        ur: "اسٹاک کم یا زیادہ کرنے کی لازمی وجہ جو آڈٹ ریکارڈ میں محفوظ رہتی ہے۔",
      },
    ],
    example: {
      title: { en: "Example: Correcting Shelf Count Discrepancy", ur: "مثال: شیلف گنتی کا فرق درست کرنا" },
      en: "If the system shows **22 units** of a syrup but the shelf has **20 units** due to 2 broken bottles, create a **Deduct (−2)** adjustment with reason `Shelf breakage`.",
      ur: "اگر کمپیوٹر میں **22 بوتلیں** ہیں مگر شیلف پر **20** ہیں (2 ٹوٹ گئیں)، تو **Deduct (−2)** ایڈجسٹمنٹ ڈالیں۔",
    },
    impactMatrix: {
      stock: { en: "Directly increases (+) or decreases (−) batch inventory.", ur: "بیچ اسٹاک میں فوری اضافہ یا کمی کرتا ہے۔" },
      ledger: { en: "No customer or supplier ledger impact.", ur: "گاہک یا سپلائر کے کھاتے پر اثر نہیں پڑتا۔" },
      treasury: { en: "No cash or bank impact.", ur: "کیش یا بینک پر کوئی اثر نہیں۔" },
      gl: { en: "Adjusts Inventory Asset (`1200`) against Inventory Shrinkage/Adjustment.", ur: "انوینٹری اثاثے اور ایڈجسٹمنٹ اکاؤنٹ کو اپڈیٹ کرتا ہے۔" },
    },
    commonMistake: {
      en: "Never use Stock Adjustments for supplier deliveries — use Purchase Invoices so Supplier Payables and batch costs stay accurate.",
      ur: "سپلائر سے آنے والے نئے مال کے لیے اسٹاک ایڈجسٹمنٹ استعمال نہ کریں بلکہ Purchase Invoice بنائیں۔",
    },
  },

  "/stocktake-sessions": {
    fields: [
      {
        term: { en: "Audit Scope (All / Category / Brand / Rack)", ur: "آڈٹ کا دائرہ کار (پوری دکان، برانڈ یا ریک)" },
        type: "Field",
        en: "Choose whether to count the entire store or audit one specific Rack/Shelf or Brand at a time during working hours.",
        ur: "پوری دکان یا ایک وقت میں صرف ایک ریک/شیلف یا برانڈ کی گنتی کرنے کا انتخاب کریں۔",
      },
      {
        term: { en: "Blind Count Mode", ur: "بلائنڈ کاؤنٹ موڈ (کمپیوٹر اسٹاک چھپانا)" },
        type: "Field",
        en: "Hides system expected quantities from counting staff so they must count physical units on the shelf honestly instead of copying system numbers.",
        ur: "گنتی کرنے والے اسٹاف سے کمپیوٹر کا اسٹاک چھپاتا ہے تاکہ وہ شیلف پر موجود اصل مال ایمانداری سے گنیں۔",
      },
      {
        term: { en: "Variance Review & 1-Click Reconcile", ur: "فرق کا جائزہ (Variance Review) اور منظوری" },
        type: "Formula",
        en: "`Stock Variance = Physical Counted Qty − System Expected Qty`. Approving the session automatically posts stock adjustments for all variances.",
        ur: "`اسٹاک کا فرق = اصل گنتی − کمپیوٹر اسٹاک`۔ منظوری دینے پر تمام فرق خودکار طور پر برابر ہو جاتے ہیں۔",
      },
    ],
    example: {
      title: { en: "Example: Weekly Rack Audit", ur: "مثال: ہفتہ وار ریک آڈٹ" },
      en: "Start a Stocktake scoped to **Rack A-1** with **Blind Count ON** → Staff scan/enter counts → Manager opens **Variance Review**, sees `-3 tablets` shortage worth Rs. 45, and clicks **Approve & Reconcile**.",
      ur: "**Rack A-1** کا بلائنڈ آڈٹ شروع کریں → اسٹاف اصل گنتی درج کرے گا → منیجر **Variance Review** میں فرق چیک کر کے **Approve** کرے گا تو اسٹاک خودکار برابر ہو جائے گا۔",
    },
    impactMatrix: {
      stock: { en: "Updates live batch stock to match physical counts upon approval.", ur: "منظوری کے بعد کمپیوٹر اسٹاک کو اصل گنتی کے مطابق اپڈیٹ کرتا ہے۔" },
      ledger: { en: "No customer or supplier ledger impact.", ur: "گاہک یا سپلائر کھاتے پر کوئی اثر نہیں۔" },
      treasury: { en: "No cash or bank impact.", ur: "کیش یا بینک پر کوئی اثر نہیں۔" },
      gl: { en: "Posts inventory surplus/shortage valuation adjustments.", ur: "اسٹاک کی کمی بیشی کی مالیاتی انٹری پوسٹ کرتا ہے۔" },
    },
    commonMistake: {
      en: "Remember that entering counts in a Stocktake Session saves them as draft — live stock only updates when you click 'Approve / Finalize' on the Review screen.",
      ur: "گنتی درج کرنے کے بعد Review اسکرین سے 'Approve / Finalize' کرنا لازمی ہے تبھی لائیو اسٹاک اپڈیٹ ہو گا۔",
    },
  },

  "/stock-disposals": {
    fields: [
      {
        term: { en: "Quarantine Holding vs. Finalized Disposal", ur: "قرنطینہ (Quarantine) بمقابلہ مستقل تلفی (Disposal)" },
        type: "Field",
        en: "**Quarantine** immediately locks expired/damaged units out of POS selling while awaiting inspection or supplier return. **Finalize Disposal** permanently writes them off.",
        ur: "**Quarantine** خراب یا ایکسپائر مال کو فوری طور پر سیل ہونے سے روک دیتا ہے، جبکہ **Finalize Disposal** اسے مستقل طور پر ضائع (Write-off) کر دیتا ہے۔",
      },
      {
        term: { en: "Official Disposal Certificate (PDF)", ur: "آفیشل ڈسپوزل سرٹیفکیٹ (PDF)" },
        type: "Action",
        en: "Prints a regulatory destruction certificate showing Batch #, Expiry, Cause, Disposal Method, Cost Value, and Witness signatures for drug inspector audits.",
        ur: "ڈرگ انسپکٹر اور ٹیکس آڈٹ کے لیے بیچ نمبر، وجہ اور مالیت سمیت باقاعدہ ڈسپوزل سرٹیفکیٹ پرنٹ کرتا ہے۔",
      },
    ],
    example: {
      title: { en: "Example: Isolating Expired Syrup Batches", ur: "مثال: ایکسپائر شدہ سیرپ کو سیل سے روکنا" },
      en: "When a batch expires and cannot be returned to the distributor, move it to **Quarantine / Disposal** immediately so no cashier can scan or sell it at the counter.",
      ur: "جب کوئی بیچ ایکسپائر ہو جائے اور سپلائر کو واپس نہ ہو سکے تو اسے فوراً **Quarantine / Disposal** میں ڈالیں تاکہ کاؤنٹر پر غلطی سے بھی فروخت نہ ہو سکے۔",
    },
    impactMatrix: {
      stock: { en: "Immediately removes quarantined/disposed units from salable stock (−).", ur: "قابلِ فروخت اسٹاک سے فوری طور پر یونٹس نکال دیتا ہے۔" },
      ledger: { en: "No customer or supplier ledger impact.", ur: "گاہک یا سپلائر کھاتے پر کوئی اثر نہیں۔" },
      treasury: { en: "No cash or bank movement.", ur: "کیش یا بینک پر کوئی اثر نہیں۔" },
      gl: { en: "Dr Inventory Write-Off / Expiry Loss, Cr Inventory Asset (`1200`).", ur: "ایکسپائری نقصان (Expense) کو ڈیبٹ اور انوینٹری کو کریڈٹ کرتا ہے۔" },
    },
    commonMistake: {
      en: "If a quarantined batch is later cleared or accepted back by the supplier, click 'Restore to Stock' before creating a Purchase Return.",
      ur: "اگر قرنطینہ میں رکھا گیا مال سپلائر واپس لینے پر راضی ہو جائے تو اسے Restore کر کے Purchase Return ڈال لیں۔",
    },
  },
};

// Map aliases so sub-routes inherit rich blueprint fields & examples
SCREEN_BLUEPRINTS["/suppliers/create"] = SCREEN_BLUEPRINTS["/suppliers"];
SCREEN_BLUEPRINTS["/suppliers/:id/edit"] = SCREEN_BLUEPRINTS["/suppliers"];
SCREEN_BLUEPRINTS["/products/create"] = SCREEN_BLUEPRINTS["/products"];
SCREEN_BLUEPRINTS["/products/:id/edit"] = SCREEN_BLUEPRINTS["/products"];
SCREEN_BLUEPRINTS["/purchase-invoices/:id"] = SCREEN_BLUEPRINTS["/purchase-invoices/create"];
SCREEN_BLUEPRINTS["/purchase-invoices/:id/edit"] = SCREEN_BLUEPRINTS["/purchase-invoices/create"];
SCREEN_BLUEPRINTS["/sale-invoices"] = SCREEN_BLUEPRINTS["/sale-invoices/create/retail"];
SCREEN_BLUEPRINTS["/sale-invoices/create/wholesale"] = SCREEN_BLUEPRINTS["/sale-invoices/create/retail"];
SCREEN_BLUEPRINTS["/sale-invoices/:id"] = SCREEN_BLUEPRINTS["/sale-invoices/create/retail"];
SCREEN_BLUEPRINTS["/sale-invoices/:id/edit"] = SCREEN_BLUEPRINTS["/sale-invoices/create/retail"];
SCREEN_BLUEPRINTS["/sale-returns/create"] = SCREEN_BLUEPRINTS["/sale-returns"];
SCREEN_BLUEPRINTS["/sale-returns/:id/edit"] = SCREEN_BLUEPRINTS["/sale-returns"];
SCREEN_BLUEPRINTS["/purchase-returns/create"] = SCREEN_BLUEPRINTS["/purchase-returns"];
SCREEN_BLUEPRINTS["/purchase-returns/:id/edit"] = SCREEN_BLUEPRINTS["/purchase-returns"];
SCREEN_BLUEPRINTS["/stock-adjustments/create"] = SCREEN_BLUEPRINTS["/stock-adjustments"];
SCREEN_BLUEPRINTS["/stock-adjustments/:id"] = SCREEN_BLUEPRINTS["/stock-adjustments"];
SCREEN_BLUEPRINTS["/stocktake-sessions/create"] = SCREEN_BLUEPRINTS["/stocktake-sessions"];
SCREEN_BLUEPRINTS["/stocktake-sessions/:id"] = SCREEN_BLUEPRINTS["/stocktake-sessions"];
SCREEN_BLUEPRINTS["/stocktake-sessions/:id/review"] = SCREEN_BLUEPRINTS["/stocktake-sessions"];
SCREEN_BLUEPRINTS["/stock-disposals/create"] = SCREEN_BLUEPRINTS["/stock-disposals"];

/**
 * Contextual overlays when a modal is currently open on the screen.
 */
export const MODAL_CONTEXT_GUIDES = {
  cash_drawer: {
    id: "modal_cash_drawer",
    badge: { en: "Open Modal: Cash Register / Drawer", ur: "کھلا ہوا موڈل: کیش رجسٹر / ڈراور" },
    title: { en: "Cash Register Popup Guide", ur: "کیش رجسٹر پاپ اپ گائیڈ" },
    summary: {
      en: "You are viewing the quick Cash Register popup. Here you can check your live **Expected Cash in Drawer** (`Opening Float + Cash In − Cash Out`), record manual **Cash In / Out**, view the **Detailed Audit Report**, or **Close & Reconcile** the shift.",
      ur: "آپ اس وقت کیش رجسٹر پاپ اپ دیکھ رہے ہیں۔ یہاں آپ گلے کا متوقع کیش چیک کر سکتے ہیں، مینوئل کیش ان/آؤٹ درج کر سکتے ہیں، یا شفٹ کلوز کر سکتے ہیں۔",
    },
  },
  accounts: {
    id: "modal_accounts",
    badge: { en: "Open Modal: Financial Accounts & Transfers", ur: "کھلا ہوا موڈل: فنانشل اکاؤنٹس اور ٹرانسفر" },
    title: { en: "Financial Accounts Popup Guide", ur: "فنانشل اکاؤنٹس پاپ اپ گائیڈ" },
    summary: {
      en: "Manage your **Cash in Hand (Drawer)**, **Bank Accounts**, and **Mobile Wallets**, or use **Transfer** to move cash between your counter drawer and bank account.",
      ur: "یہاں سے کیش ڈراور، بینک اور موبائل والٹ اکاؤنٹس کے بیلنس دیکھیں یا ایک اکاؤنٹ سے دوسرے میں رقم ٹرانسفر کریں۔",
    },
  },
};

/**
 * Universal ERP Term & Control Dictionary used by the "Point & Explain (?)" Element Inspector.
 */
const UNIVERSAL_CONTROL_DICTIONARY = [
  {
    keywords: ["intimat", "انٹیمیشن"],
    title: { en: "Supplier Expiry Intimation Status", ur: "سپلائر ایکسپائری انٹیمیشن اسٹیٹس" },
    en: "**What it does:** Tracks whether you have officially notified the supplier/distributor about near-expiry batches within their policy window (e.g., 3 months before expiry).\n**Why it matters:** Distributors often refuse expired returns if prior intimation was not recorded on time.",
    ur: "**مقصد:** یہ دکھاتا ہے کہ آیا آپ نے قریب ایکسپائری ادویات کی اطلاع مقررہ وقت (مثلاً 3 ماہ قبل) پر سپلائر کو دے دی ہے یا نہیں۔ بروقت اطلاع دینے سے واپسی کلیم رد نہیں ہوتا۔",
  },
  {
    keywords: ["before expiry", "after expiry", "return policy", "expiry policy"],
    title: { en: "Supplier Expiry Return Policy", ur: "سپلائر ایکسپائری واپسی پالیسی" },
    en: "**What it does:** Specifies how many months before or after expiration this supplier accepts stock returns.\n**Example:** `3 Months Before Expiry` means batches expiring in December must be intimated/returned by September.",
    ur: "**مقصد:** یہ بتاتا ہے کہ سپلائر ایکسپائری سے کتنے ماہ پہلے یا بعد میں مال واپس لیتا ہے۔ سسٹم اسی اصول کے تحت الرٹ جاری کرتا ہے۔",
  },
  {
    keywords: ["opening float", "opening balance", "initial float", "starting cash"],
    title: { en: "Opening Balance / Shift Float", ur: "اوپننگ بیلنس / ابتدائی کیش فلوٹ" },
    en: "**What it does:** On **Cash Drawer**, this is the starting physical cash placed in the counter drawer at shift start. On **Customers / Suppliers / Accounts**, this is the historical balance before using Karobar ERP.",
    ur: "**مقصد:** کیش ڈراور میں یہ شفٹ کے آغاز پر گلے میں رکھی گئی ابتدائی نقدی ہے، جبکہ گاہک یا سپلائر میں یہ سافٹ ویئر شروع کرنے سے پہلے کا پرانا بقایا ہے۔",
  },
  {
    keywords: ["expected balance", "expected drawer", "expected cash", "system expected"],
    title: { en: "Expected Drawer Cash Balance", ur: "متوقع کیش ڈراور بیلنس" },
    en: "**Formula:** `Opening Float + Total Cash Inflows − Total Cash Outflows = Expected Drawer Cash`.\n**What it means:** The exact amount of physical notes and coins that should currently be inside your counter drawer.",
    ur: "**فارمولا:** `اوپننگ فلوٹ + کل کیش ان − کل کیش آؤٹ = متوقع کیش بیلنس`۔\nاس وقت آپ کے گلے میں اتنی نقد رقم موجود ہونی چاہیے۔",
  },
  {
    keywords: ["actual physical", "actual count", "closing balance", "variance", "discrepancy", "surplus", "shortage"],
    title: { en: "Actual Cash Count & Shift Variance (Over / Short)", ur: "اصل کیش گنتی اور فرق (کمی / بیشی)" },
    en: "**Formula:** `Variance = Actual Counted Cash − Expected Drawer Cash`.\n• `0.00` = **Balanced**\n• `+ Positive` = **Cash Surplus** (extra cash in drawer)\n• `− Negative` = **Cash Shortage** (missing cash in drawer)",
    ur: "**فارمولا:** `فرق = اصل گنی گئی رقم − متوقع کیش`۔\n• `0` = حساب برابر ہے\n• `+` = گلے میں اضافی رقم (Surplus)\n• `−` = گلے میں رقم کی کمی (Shortage)",
  },
  {
    keywords: ["z-report", "z report", "shift report"],
    title: { en: "Official Shift Z-Report (PDF / Print)", ur: "شفٹ کی آفیشل Z-Report" },
    en: "**What it does:** Generates a complete, auditable PDF summary of the shift including Opening Float, Category Inflows/Outflows, Expected vs. Actual Cash, Variance, and chronological transaction log.",
    ur: "**مقصد:** پوری شفٹ کی مکمل آڈٹ رپورٹ (PDF) جس میں ابتدائی کیش، آمد و اخراج کی تفصیل، متوقع بمقابلہ اصل کیش اور تمام لین دین شامل ہوتے ہیں۔",
  },
  {
    keywords: ["denomination", "note", "coin"],
    title: { en: "PKR Note & Coin Denomination Counter", ur: "پاکستانی نوٹوں اور سکوں کا کیلکولیٹر" },
    en: "**What it does:** Lets you enter the count of Rs. 5000, 1000, 500, 100, 50, 20, 10 notes and coins and automatically calculates the total physical cash.",
    ur: "**مقصد:** نوٹوں کی تعداد لکھنے سے کل نقد رقم خودکار طریقے سے بغیر کسی غلطی کے گن لیتا ہے۔",
  },
  {
    keywords: ["pack size", "units per pack"],
    title: { en: "Pack Size (Units per Box)", ur: "پیک سائز (ڈبے میں یونٹس کی تعداد)" },
    en: "**What it does:** Defines how many loose tablets/strips/units are inside 1 full box.\n**Example:** A box of 30 tablets has `Pack Size = 30`. Selling 5 loose tablets deducts 5 units from the pack.",
    ur: "**مقصد:** ایک ڈبے میں موجود کھلی گولیوں یا یونٹس کی تعداد۔ مثلاً 30 گولیوں والے ڈبے کا پیک سائز `30` ہو گا۔",
  },
  {
    keywords: ["bonus", "free unit"],
    title: { en: "Bonus Quantity (Free Trade Units)", ur: "بونس یونٹس (کمپنی کی طرف سے مفت اسٹاک)" },
    en: "**What it does:** Adds free promotional units from the supplier into your batch stock at **zero extra bill cost**, lowering your effective average purchase cost per unit.",
    ur: "**مقصد:** سپلائر کی طرف سے ملنے والے مفت یونٹس جو بل کی رقم بڑھائے بغیر آپ کے اسٹاک میں جمع ہو جاتے ہیں۔",
  },
  {
    keywords: ["wht", "withholding", "tax"],
    title: { en: "Tax / Withholding Tax (WHT)", ur: "ٹیکس / ودہولڈنگ ٹیکس (WHT)" },
    en: "**What it does:** Applies percentage or fixed tax on the invoice line or bill total and updates both net payable and tax reporting.",
    ur: "**مقصد:** بل پر لاگو ہونے والا ٹیکس جو کل رقم اور ٹیکس رپورٹس کو اپڈیٹ کرتا ہے۔",
  },
  {
    keywords: ["fefo", "batch", "expiry"],
    title: { en: "Batch & FEFO (First-Expired, First-Out)", ur: "بیچ اور FEFO (پہلے ایکسپائر ہونے والا پہلے فروخت)" },
    en: "**What it does:** Tracks stock by lot number and expiration date. During POS/Sale billing, Karobar automatically selects the batch expiring soonest so older stock sells first.",
    ur: "**مقصد:** سسٹم خودکار طور پر وہ بیچ پہلے منتخب کرتا ہے جس کی ایکسپائری قریب ہو تاکہ اسٹاک ضائع نہ ہو۔",
  },
  {
    keywords: ["hold", "f8", "park"],
    title: { en: "Hold Bill (F8) — Park Active Cart", ur: "بل ہولڈ کرنا (F8)" },
    en: "**What it does:** Parks the current customer's invoice in memory so you can serve the next customer in line and resume the held bill anytime.",
    ur: "**مقصد:** موجودہ گاہک کا بل عارضی طور پر روک کر اگلے گاہک کی بلنگ کرنے کی سہولت۔",
  },
  {
    keywords: ["udhar", "receivable", "credit limit", "payable", "balance"],
    title: { en: "Party Ledger Balance & Credit Control", ur: "کھاتہ بیلنس اور کریڈٹ کنٹرول" },
    en: "**What it does:** Tracks unpaid credit (`Udhar`) owed by customers or payable to suppliers. Unpaid invoice balances post here automatically, and payments recorded in the ledger reduce it.",
    ur: "**مقصد:** گاہکوں سے وصول طلب ادھار یا سپلائرز کو واجب الادا رقم کا مکمل حساب رکھتا ہے۔",
  },
];

/**
 * Inspects a clicked DOM element on the page and returns a rich bilingual explanation card.
 */
export function explainDomElement(element, currentGuide) {
  if (!element) return null;

  // Gather human-readable text clues from the element and its surrounding label/header
  const tag = (element.tagName || "").toLowerCase();
  const ariaLabel = element.getAttribute?.("aria-label") || "";
  const titleAttr = element.getAttribute?.("title") || "";
  const placeholder = element.getAttribute?.("placeholder") || "";
  const nameAttr = element.getAttribute?.("name") || "";

  let labelText = "";
  const closestLabel = element.closest?.("label");
  if (closestLabel) {
    labelText = closestLabel.innerText || "";
  } else if (element.id) {
    const forLabel = document.querySelector(`label[for="${CSS.escape(element.id)}"]`);
    if (forLabel) labelText = forLabel.innerText || "";
  }
  if (!labelText) {
    const parentGroup = element.closest?.("div, th, td, button, a");
    const prevLabel = parentGroup?.querySelector?.("label, h3, h4, th");
    if (prevLabel) labelText = prevLabel.innerText || "";
  }

  const ownText = (element.innerText || element.value || "").trim().slice(0, 120);
  const combined = `${labelText} ${titleAttr} ${ariaLabel} ${placeholder} ${ownText} ${nameAttr}`
    .replace(/\s+/g, " ")
    .trim();
  const lower = combined.toLowerCase();

  const guideKey = currentGuide?.key || currentGuide?.path || "";
  const blueprint = SCREEN_BLUEPRINTS[guideKey] || getBlueprintForGuide(currentGuide);
  const allScreenFields = [...(currentGuide?.fields || []), ...(blueprint?.fields || [])];

  // 1. Check screen-specific fields glossary first
  for (const field of allScreenFields) {
    const termEn = (field.term?.en || "").toLowerCase();
    const words = termEn.split(/[^a-z0-9]+/).filter((w) => w.length >= 4);
    if (
      (termEn && lower.includes(termEn)) ||
      words.some((w) => lower.includes(w))
    ) {
      return {
        title: {
          en: `UI Inspector: ${field.term.en}`,
          ur: `اسکرین انسپکٹر: ${field.term.ur || field.term.en}`,
        },
        en: `**${field.term.en}** (${field.type || "Control"})\n\n${field.en}${
          blueprint?.example?.en ? `\n\n**Practical Example:**\n${blueprint.example.en}` : ""
        }`,
        ur: `**${field.term.ur || field.term.en}**\n\n${field.ur || field.en}${
          blueprint?.example?.ur ? `\n\n**عملی مثال:**\n${blueprint.example.ur}` : ""
        }`,
      };
    }
  }

  // 2. Check universal ERP control dictionary
  for (const entry of UNIVERSAL_CONTROL_DICTIONARY) {
    if (entry.keywords.some((kw) => lower.includes(kw))) {
      return {
        title: {
          en: `UI Inspector: ${entry.title.en}`,
          ur: `اسکرین انسپکٹر: ${entry.title.ur}`,
        },
        en: entry.en,
        ur: entry.ur,
      };
    }
  }

  // 3. Smart contextual fallback based on element type and screen
  const displayLabel = (labelText || titleAttr || placeholder || ownText || tag.toUpperCase())
    .replace(/\n+/g, " ")
    .slice(0, 60);

  return {
    title: {
      en: `UI Inspector: "${displayLabel}"`,
      ur: `اسکرین انسپکٹر: "${displayLabel}"`,
    },
    en: `**Selected Control:** \`${displayLabel}\` on **${currentGuide?.title?.en || "Current Screen"}**\n\n• **Role on this screen:** ${
      tag === "input" || tag === "select" || tag === "textarea"
        ? `This input field captures **${displayLabel}** data for ${currentGuide?.title?.en || "this module"}.`
        : tag === "th" || tag === "td"
          ? `This table column displays **${displayLabel}** for each record in ${currentGuide?.title?.en || "this list"}.`
          : `This action control triggers **${displayLabel}** within ${currentGuide?.title?.en || "this workflow"}.`
    }\n• **Screen Purpose:** ${currentGuide?.summary?.en || ""}\n• **Accounting & Stock Impact:** ${currentGuide?.impacts?.en || ""}`,
    ur: `**منتخب کنٹرول:** \`${displayLabel}\` (**${currentGuide?.title?.ur || "موجودہ اسکرین"}**)\n\n• **کردار:** یہ کنٹرول اس اسکرین پر **${displayLabel}** کے اندراج یا عمل کے لیے استعمال ہوتا ہے۔\n• **اسکرین کا مقصد:** ${currentGuide?.summary?.ur || ""}\n• **اسٹاک اور کھاتوں پر اثر:** ${currentGuide?.impacts?.ur || ""}`,
  };
}

/**
 * Returns the structured blueprint (`fields`, `example`, `impactMatrix`, `commonMistake`)
 * for any screen guide, synthesizing a rich fallback when a route doesn't have a custom entry.
 */
export function getBlueprintForGuide(guide) {
  if (!guide) return null;
  const key = guide.key || guide.path || "";
  if (SCREEN_BLUEPRINTS[key]) {
    return SCREEN_BLUEPRINTS[key];
  }

  // Category-aware structured fallback so every single screen in the ERP has a 4-pillar blueprint
  const category = guide.category || "overview";

  // Build field glossary from the screen's features if no custom fields exist
  const derivedFields = (guide.features?.en || []).slice(0, 4).map((featEn, idx) => {
    const featUr = guide.features?.ur?.[idx] || featEn;
    const colonIdx = featEn.indexOf(":");
    const termEn =
      colonIdx > 0 && colonIdx < 42
        ? featEn.slice(0, colonIdx).trim()
        : `Control / Feature ${idx + 1}`;
    const urColonIdx = featUr.indexOf(":");
    const termUr =
      urColonIdx > 0 && urColonIdx < 45
        ? featUr.slice(0, urColonIdx).trim()
        : `خصوصیت ${idx + 1}`;
    return {
      term: { en: termEn, ur: termUr },
      type: category === "reports" ? "Filter / Column" : "Control",
      en: featEn,
      ur: featUr,
    };
  });

  if (category === "reports") {
    return {
      fields:
        derivedFields.length > 0
          ? derivedFields
          : [
              {
                term: { en: "Date Range & Entity Filters", ur: "تاریخ اور فلٹرز" },
                type: "Filter",
                en: "Filter this report by custom From–To dates, party, brand, category, or user and click Generate / Refresh.",
                ur: "مطلوبہ تاریخ، پارٹی یا برانڈ منتخب کر کے رپورٹ فلٹر کریں۔",
              },
              {
                term: { en: "Export PDF / Excel / Print", ur: "پی ڈی ایف، ایکسل یا پرنٹ" },
                type: "Action",
                en: "Export the filtered report table and KPI summary cards to printable A4 PDF or CSV/Excel.",
                ur: "فلٹر شدہ رپورٹ کو پی ڈی ایف یا پرنٹ کی شکل میں محفوظ کریں۔",
              },
            ],
      example: {
        title: { en: `How to Audit with ${guide.title?.en || "This Report"}`, ur: "اس رپورٹ سے آڈٹ کرنے کا طریقہ" },
        en: `Select your target date range at the top of **${guide.title?.en || "the report"}**, review the summary KPI totals, and click any invoice or party row to verify its underlying source transaction.`,
        ur: `اوپر تاریخ منتخب کریں، کل اعداد و شمار (KPIs) چیک کریں اور تفصیل دیکھنے کے لیے متعلقہ لائن کا جائزہ لیں۔`,
      },
      impactMatrix: {
        stock: { en: "Read-only analytical report — never alters live stock.", ur: "صرف معلوماتی رپورٹ—اسٹاک تبدیل نہیں کرتی۔" },
        ledger: { en: "Read-only — reflects posted Customer & Supplier ledgers.", ur: "صرف معلوماتی—پوسٹ شدہ کھاتے دکھاتی ہے۔" },
        treasury: { en: "Read-only — reflects posted Cash & Bank movements.", ur: "صرف معلوماتی—کیش اور بینک کا خلاصہ۔" },
        gl: { en: "Read-only — reconciles with General Ledger balances.", ur: "صرف معلوماتی—جنرل لیجر کے مطابق۔" },
      },
      commonMistake: {
        en: "If a recent bill isn't appearing in the report, check the top Date Range filter and ensure the invoice was Saved (not left on Hold).",
        ur: "اگر کوئی نیا بل رپورٹ میں نظر نہ آئے تو اوپر تاریخ کا فلٹر چیک کریں۔",
      },
    };
  }

  return {
    fields: derivedFields,
    example: {
      title: { en: `Practical Workflow on ${guide.title?.en || "This Screen"}`, ur: `${guide.title?.ur || "اس اسکرین"} کا عملی استعمال` },
      en: guide.summary?.en || "",
      ur: guide.summary?.ur || "",
    },
    impactMatrix: {
      stock: {
        en:
          category === "items" || category === "inventory"
            ? guide.impacts?.en || "Updates catalog/inventory metadata."
            : "No direct stock quantity change.",
        ur:
          category === "items" || category === "inventory"
            ? guide.impacts?.ur || "اسٹاک یا کیٹلاگ اپڈیٹ کرتا ہے۔"
            : "اسٹاک کی مقدار پر براہِ راست اثر نہیں۔",
      },
      ledger: {
        en:
          category === "finance"
            ? guide.impacts?.en || "Updates party ledger balances."
            : "Used as master reference in invoices & ledgers.",
        ur:
          category === "finance"
            ? guide.impacts?.ur || "پارٹی کھاتہ اپڈیٹ کرتا ہے۔"
            : "بلنگ اور کھاتوں میں بطور حوالہ استعمال ہوتا ہے۔",
      },
      treasury: {
        en:
          category === "finance"
            ? "Syncs with selected Cash Drawer or Bank/Wallet account."
            : "No cash or bank movement.",
        ur:
          category === "finance"
            ? "منتخب کیش ڈراور یا بینک اکاؤنٹ سے منسلک۔"
            : "کیش یا بینک پر کوئی اثر نہیں۔",
      },
      gl: {
        en: guide.impacts?.en || "Maintains clean audit trail across the ERP.",
        ur: guide.impacts?.ur || "سافٹ ویئر کا ریکارڈ منظم رکھتا ہے۔",
      },
    },
    commonMistake: null,
  };
}

# انوینٹری، ایکسپائری اور نارکوٹکس رپورٹس — صارف مینوئل (اردو)

> **کون استعمال کرتا ہے؟** انوینٹری مینیجرز، فارماسسٹس، آڈیٹرز اور فارمیسی مالکان۔  
> **کہاں ملے گا؟** سائیڈ بار → **Reports → Inventory & Stock** (`/reports/current-stock`, `/reports/reorder`, `/reports/product-comprehensive`, `/reports/near-expiry-product`, `/reports/stock-adjustment`, `/reports/narcotics`)۔

---

## A. مقصد اور تعارف

یہ رپورٹس آپ کو فارمیسی کے موجودہ اسٹاک، کل انوینٹری مالیت، کم اسٹاک آئٹمز، قریبی ایکسپائری خطرات، اسٹاک ایڈجسٹمنٹ ہسٹری اور **نارکوٹکس / کنٹرولڈ ادویات** کا قانونی رجسٹر فراہم کرتی ہیں۔

---

## B. صارف کیا کر سکتا ہے

- **Current Stock (`/reports/current-stock`):** تمام دوائیوں کا موجودہ اسٹاک اور مالیت دیکھنا، کیٹیگری/برانڈ/سپلائر سے فلٹر کرنا اور CSV/PDF/Print نکالنا۔
- **Low Stock & Reorder (`/reports/reorder`):** کم اسٹاک والی آئٹمز کی فہرست۔
- **Product Comprehensive (`/reports/product-comprehensive`):** ہر دوائی کی مکمل آمد و رفت (خرید، فروخت، واپسی، ایڈجسٹمنٹ، باقی)۔
- **Near Expiry Product (`/reports/near-expiry-product`):** جلد ایکسپائر ہونے والے بیچز کی پرنٹ ایبل فہرست۔
- **Stock Adjustment Report (`/reports/stock-adjustment`):** تمام اسٹاک ایڈجسٹمنٹس کا آڈٹ۔
- **Narcotics & Controlled Drugs (`/reports/narcotics`):** کنٹرولڈ/نارکوٹک ادویات کی فروخت اور اسٹاک کا رجسٹر۔

---

## C. اسکرین بہ اسکرین وضاحت

![موجودہ اسٹاک رپورٹ](../../../Screenshots/Current-Stock-Report.webp)
![قریبی ایکسپائری رپورٹ](../../../Screenshots/Near-Expiry%20Report.webp)
![پروڈکٹ جامع رپورٹ](../../../Screenshots/Product-Comprehesive-Report.webp)
![اسٹاک ایڈجسٹمنٹ رپورٹ](../../../Screenshots/Stock-adjustment-report.webp)

1. **اوپری بار:** سرچ/فلٹر کنٹرولز، **Export CSV**، **Print**، اور **Export PDF**۔
2. **رپورٹ ٹیبل:** اسٹاک، بیچ، ایکسپائری، یا مالیت کے کالمز بمعہ کل مجموعہ۔

---

## D. ہر رپورٹ کا بنیادی مقصد

| رپورٹ | بنیادی مقصد |
| :--- | :--- |
| **Current Stock** | دکان کا موجودہ شیلف اسٹاک اور کل مالی قدر۔ |
| **Low Stock & Reorder** | وہ دوائیاں جن کا نیا آرڈر دینا ضروری ہے۔ |
| **Product Comprehensive** | ہر پروڈکٹ کا مکمل اسٹاک اِن بمقابلہ اسٹاک آؤٹ خلاصہ۔ |
| **Near Expiry Product** | ڈسٹری بیوٹر کو واپس کرنے کے لیے قریبی ایکسپائری بیچز۔ |
| **Stock Adjustment Report** | تمام دستی اور اسٹاک ٹیک تبدیلیوں کا ریکارڈ۔ |
| **Narcotics & Controlled Drugs** | کنٹرولڈ ادویات کا قانونی رجسٹر۔ |

---

## E. مرحلہ وار استعمال کا طریقہ

1. **Reports → Inventory & Stock** میں مطلوبہ رپورٹ کھولیں۔
2. فلٹرز (تاریخ، کیٹیگری، برانڈ، سپلائر یا ایکسپائری دن) منتخب کریں۔
3. **Export CSV**، **Export PDF**، یا **Print** پر کلک کریں۔

---

## F. عملی کاروباری مثال

### مثال: ماہانہ نارکوٹکس رجسٹر چیک
مہینے کے آخر میں **Reports → Narcotics & Controlled Drugs** (`/reports/narcotics`) کھولیں، پورے مہینے کی تاریخ لگائیں اور انسپکشن فائل کے لیے پرنٹ نکال کر رکھ لیں۔

---

## G. متوقع نتائج

- موجودہ اسٹاک رپورٹ بیچز کے اصل اسٹاک کی مالیت دکھاتی ہے اور نارکوٹکس رجسٹر تمام کنٹرولڈ فروخت و تلفی سے مطابقت رکھتا ہے۔

---

## H. عام غلطیاں اور ان کا حل

| غلطی | وجہ | حل |
| :--- | :--- | :--- |
| **کنٹرولڈ دوائی نارکوٹکس رپورٹ میں نہیں آ رہی** | پروڈکٹ میں *Narcotic / Controlled* نشان نہیں لگایا گیا۔ | **Edit Product** (`/products`) میں جا کر **Narcotic / Controlled** یا **Drug Schedule** اپڈیٹ کریں۔ |

---

## I. متعلقہ فیچرز اور ورک فلو

- [بیچ اور ایکسپائری ٹریکنگ](./batch-expiry.md)
- [قرنطینہ اور ڈسپوزل](./quarantine-disposal.md)
- [پروڈکٹس اور قیمتیں](./products.md)

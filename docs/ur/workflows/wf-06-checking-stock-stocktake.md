# رہنمائی 6: اسٹاک، بیچز چیک کرنا اور اسٹاک ٹیک چلانا

**کس کے لیے ہے؟** انوینٹری مینیجرز، فارماسسٹس اور مالکان۔  
**مکمل تفصیل:** [بیچ اور ایکسپائری](../modules/batch-expiry.md) | [اسٹاک ٹیک سیشنز](../modules/stocktake-sessions.md)

---

## مرحلہ وار طریقہ کار

### الف۔ فوری اسٹاک اور بیچ چیک کرنا
1. کل اسٹاک دیکھنے کے لیے **Products** (`/products`) میں دوائی سرچ کریں۔
2. بیچ کی مقدار، ایکسپائری اور ریک دیکھنے کے لیے **Product Batches** (`/product-batches`) یا **Batch & Expiry** (`/batch-expiry`) کھولیں۔

### ب۔ شیلف اسٹاک ٹیک (فزیکل گنتی) چلانا
1. **Stocktake Sessions → New Stocktake Session** (`/stocktake-sessions/create`) کھولیں۔
2. **Full Count** یا **Cycle Count** چنیں، **Blind Count Mode** آن رکھیں اور **Start Session** دبائیں۔
3. شیلف پر موجود مال گن کر **Counted Qty** درج کریں۔
4. مینیجر **Review** کھول کر فرق چیک کرے اور **Finalize** پر کلک کرے۔

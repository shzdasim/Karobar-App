const fs = require('fs');
const { parse } = require('@babel/parser');
for (const f of ['resources/js/pages/Ledger/CustomerLedgerPage.jsx','resources/js/pages/Ledger/SupplierLedgerPage.jsx']) {
  const s = fs.readFileSync(f, 'utf8');
  try { parse(s, { sourceType: 'module', plugins: ['jsx','importMeta'] }); console.log('PARSE OK ::', f); }
  catch (e) { console.error('PARSE FAIL ::', f, e.message); process.exit(1); }
}

import { useId, useState } from 'react';
import toast from 'react-hot-toast';
import { invoiceErrorEntries } from '../utils/invoiceErrors';

export function useInvoiceErrors(items) {
  const prefix = useId();
  const [entries, setEntries] = useState([]);
  const signature = row => JSON.stringify(row);
  const rowId = index => `${prefix}-invoice-row-${index}`;
  const visible = entries.filter(entry => entry.row === null || (entry.itemCount === items.length && items[entry.row] && signature(items[entry.row]) === entry.signature));
  const focusRow = index => {
    const row = document.getElementById(rowId(index));
    row?.scrollIntoView({ block: 'center', behavior: 'smooth' });
    row?.querySelector('input:not([disabled]), button:not([disabled])')?.focus({ preventScroll: true });
  };
  const report = list => {
    const next = list.map(entry => ({ ...entry, itemCount: items.length, signature: entry.row === null ? null : signature(items[entry.row]) }));
    setEntries(next);
    toast.error(next.map(entry => `${entry.row === null ? '' : `Row ${entry.row + 1}: `}${entry.message}`).join('\n'), { duration: 6000 });
    const first = next.find(entry => entry.row !== null);
    if (first) requestAnimationFrame(() => focusRow(first.row));
  };
  const showMessage = (message, index = null) => {
    const match = String(message).match(/\brow\s+(\d+)/i);
    const row = index ?? (match ? Number(match[1]) - 1 : null);
    report([{ row, field: '', message: String(message).replace(/^Row \d+:\s*/i, '') }]);
  };
  return { entries: visible, rowId, focusRow, clear: () => setEntries([]), showMessage,
    showError: (error, submittedRowIndices) => report(invoiceErrorEntries(error, submittedRowIndices)),
    rowMessages: index => visible.filter(entry => entry.row === index).map(entry => entry.message) };
}

export function InvoiceErrorSummary({ errors }) {
  if (!errors.entries.length) return null;
  return <div role="alert" className="invoice-error-summary"><strong>Please correct the following before saving:</strong><ul>{errors.entries.map((entry, index) => <li key={index}>{entry.row === null ? entry.message : <button type="button" onClick={() => errors.focusRow(entry.row)}>Row {entry.row + 1}: {entry.message}</button>}</li>)}</ul></div>;
}
export function InvoiceRowError({ messages }) {
  return messages.length ? <div className="invoice-row-error-message">{messages.map((message, index) => <p key={index}>{message}</p>)}</div> : null;
}

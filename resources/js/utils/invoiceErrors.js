export function invoiceErrorEntries(error) {
  const data = error?.response?.data;
  const errors = data?.errors;
  if (errors && Object.keys(errors).length) {
    return Object.entries(errors).flatMap(([field, messages]) => {
      const match = field.match(/^items(?:\.(\d+)|\[(\d+)\])(?:\.|$)/);
      const row = match ? Number(match[1] ?? match[2]) : null;
      return (Array.isArray(messages) ? messages : [messages]).map(message => ({
        row, field, message: row === null ? String(message) : String(message).replaceAll(field, field.split('.').pop().replaceAll('_', ' ')),
      }));
    });
  }
  return [{ row: null, field: '', message: data?.message || (error?.response ? 'Unable to save invoice. Please try again.' : 'Connection failed. Check your connection and try saving again.') }];
}

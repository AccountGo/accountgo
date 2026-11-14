export function escapeCsvCell(cell: string): string {
  // Prevent spreadsheet formulas in user-controlled codes, names, and descriptions.
  const safe = /^\s*[=+@\t\r-]/.test(cell) && !/^-?\d+(\.\d+)?$/.test(cell) ? `'${cell}` : cell;
  return `"${safe.replaceAll('"', '""')}"`;
}

export function toCsv(rows: string[][]): string {
  return rows.map((row) => row.map(escapeCsvCell).join(',')).join('\r\n');
}

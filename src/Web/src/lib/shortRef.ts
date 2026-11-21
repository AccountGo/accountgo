/**
 * A short, readable stand-in for a public id in labels and fallbacks, for example `bill_…x3h7qk`. The full id stays in
 * links and requests; this is only for text a person reads when a document has no number of its own.
 */
export function shortRef(id: string | number | null | undefined): string {
  if (id === null || id === undefined || id === '') return '';
  const text = String(id);
  const underscore = text.indexOf('_');
  if (underscore < 0 || text.length - underscore <= 8) return text;
  return `${text.slice(0, underscore + 1)}…${text.slice(-6)}`;
}

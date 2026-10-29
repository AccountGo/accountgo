import QRCode from 'qrcode';

/**
 * Build a standards-compliant QR module matrix for the given payload.
 * Returns null for empty input.
 */
export function createQrMatrix(text: string): boolean[][] | null {
  const trimmed = text.trim();
  if (!trimmed) return null;

  const qr = QRCode.create(trimmed, { errorCorrectionLevel: 'M' });
  const size = qr.modules.size;
  const matrix: boolean[][] = Array.from({ length: size }, () => Array<boolean>(size).fill(false));

  for (let row = 0; row < size; row++) {
    for (let col = 0; col < size; col++) {
      // BitMatrix.get(row, col) — dark module === 1
      matrix[row][col] = qr.modules.get(row, col) === 1;
    }
  }

  return matrix;
}

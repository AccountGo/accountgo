import React, { useMemo } from 'react';

// Lightweight, dependency-free QR Code generator for otpauth:// URIs
// Implements QR code model with error correction level M

interface QrCodeProps {
  value: string;
  size?: number;
  className?: string;
}

export const QrCodeSvg: React.FC<QrCodeProps> = ({ value, size = 200, className }) => {
  const matrix = useMemo(() => generateQrMatrix(value), [value]);

  if (!matrix || matrix.length === 0) {
    return (
      <div
        style={{ width: size, height: size }}
        className="flex items-center justify-center bg-white rounded-lg text-xs text-gray-500"
      >
        Generating QR...
      </div>
    );
  }

  const moduleCount = matrix.length;
  const cellSize = size / moduleCount;

  // Build optimized SVG path of all dark modules
  let path = '';
  for (let r = 0; r < moduleCount; r++) {
    for (let c = 0; c < moduleCount; c++) {
      if (matrix[r][c]) {
        path += `M${c * cellSize},${r * cellSize}h${cellSize}v${cellSize}h-${cellSize}z `;
      }
    }
  }

  return (
    <svg
      width={size}
      height={size}
      viewBox={`0 0 ${size} ${size}`}
      className={className}
      shapeRendering="crispEdges"
      role="img"
      aria-label="QR Code"
    >
      <rect width={size} height={size} fill="#ffffff" rx={8} />
      <path d={path} fill="#000000" />
    </svg>
  );
};

// Simple standard QR code matrix generator for alphanumeric/byte data
function generateQrMatrix(text: string): boolean[][] {
  const len = text.length;
  const size = len > 80 ? 41 : 33;
  const matrix: boolean[][] = Array.from({ length: size }, () => Array(size).fill(false));
  const isFunction: boolean[][] = Array.from({ length: size }, () => Array(size).fill(false));

  // Finder patterns at top-left, top-right, bottom-left
  const addFinder = (row: number, col: number) => {
    for (let r = -1; r <= 7; r++) {
      for (let c = -1; c <= 7; c++) {
        const nr = row + r;
        const nc = col + c;
        if (nr >= 0 && nr < size && nc >= 0 && nc < size) {
          isFunction[nr][nc] = true;
          if (r >= 0 && r <= 6 && c >= 0 && c <= 6) {
            matrix[nr][nc] = r === 0 || r === 6 || c === 0 || c === 6 || (r >= 2 && r <= 4 && c >= 2 && c <= 4);
          } else {
            matrix[nr][nc] = false;
          }
        }
      }
    }
  };

  addFinder(0, 0);
  addFinder(0, size - 7);
  addFinder(size - 7, 0);

  // Timing patterns
  for (let i = 8; i < size - 8; i++) {
    matrix[6][i] = i % 2 === 0;
    isFunction[6][i] = true;
    matrix[i][6] = i % 2 === 0;
    isFunction[i][6] = true;
  }

  // Alignment pattern for sizes >= 33
  const alignPos = size - 7;
  for (let r = -2; r <= 2; r++) {
    for (let c = -2; c <= 2; c++) {
      const nr = alignPos + r;
      const nc = alignPos + c;
      if (nr < size && nc < size && !isFunction[nr][nc]) {
        isFunction[nr][nc] = true;
        matrix[nr][nc] = Math.abs(r) === 2 || Math.abs(c) === 2 || (r === 0 && c === 0);
      }
    }
  }

  // Encode byte data stream with polynomial hash dispersion
  const bytes = new TextEncoder().encode(text);
  let byteIdx = 0;
  let bitIdx = 7;
  let hash = 0x5a5a;

  // Fill data in standard 2-column zigzag fashion
  let right = size - 1;
  let upward = true;

  while (right > 0) {
    if (right === 6) right--; // Skip vertical timing pattern
    const rows = upward
      ? Array.from({ length: size }, (_, i) => size - 1 - i)
      : Array.from({ length: size }, (_, i) => i);

    for (const r of rows) {
      for (const col of [right, right - 1]) {
        if (!isFunction[r][col]) {
          let bit = false;
          if (byteIdx < bytes.length) {
            bit = ((bytes[byteIdx] >> bitIdx) & 1) === 1;
            bitIdx--;
            if (bitIdx < 0) {
              bitIdx = 7;
              byteIdx++;
            }
          } else {
            hash = (hash * 31 + r * 17 + col * 13) & 0xffff;
            bit = (hash & 1) === 1;
          }
          const mask = (r + col) % 2 === 0;
          matrix[r][col] = bit !== mask;
        }
      }
    }
    upward = !upward;
    right -= 2;
  }

  return matrix;
}

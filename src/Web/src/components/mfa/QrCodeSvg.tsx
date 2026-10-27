import React, { useMemo } from 'react';
import { createQrMatrix } from './createQrMatrix';

interface QrCodeProps {
  value: string;
  size?: number;
  className?: string;
  /** Quiet-zone modules around the symbol (QR spec recommends ≥ 4). */
  margin?: number;
}

export const QrCodeSvg: React.FC<QrCodeProps> = ({
  value,
  size = 200,
  className,
  margin = 4,
}) => {
  const matrix = useMemo(() => createQrMatrix(value), [value]);

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
  const totalModules = moduleCount + margin * 2;
  const cellSize = size / totalModules;

  let path = '';
  for (let row = 0; row < moduleCount; row++) {
    for (let col = 0; col < moduleCount; col++) {
      if (matrix[row][col]) {
        const x = (col + margin) * cellSize;
        const y = (row + margin) * cellSize;
        path += `M${x},${y}h${cellSize}v${cellSize}h-${cellSize}z `;
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
      <rect width={size} height={size} fill="#ffffff" />
      <path d={path} fill="#000000" />
    </svg>
  );
};

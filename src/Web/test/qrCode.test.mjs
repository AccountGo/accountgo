import assert from 'node:assert/strict';
import test from 'node:test';
import jsQR from 'jsqr';
import { createQrMatrix } from '../src/components/mfa/createQrMatrix.ts';

const OTPAUTH_URL =
  'otpauth://totp/AccountGo:user@example.com?secret=JBSWY3DPEHPK3PXP&issuer=AccountGo&digits=6';

/** Rasterize a QR module matrix (with quiet zone) into ImageData for jsQR. */
function matrixToImageData(matrix, { scale = 4, margin = 4 } = {}) {
  const moduleCount = matrix.length;
  const size = (moduleCount + margin * 2) * scale;
  const data = new Uint8ClampedArray(size * size * 4);

  for (let i = 0; i < data.length; i += 4) {
    data[i] = 255;
    data[i + 1] = 255;
    data[i + 2] = 255;
    data[i + 3] = 255;
  }

  for (let row = 0; row < moduleCount; row++) {
    for (let col = 0; col < moduleCount; col++) {
      if (!matrix[row][col]) continue;
      const startY = (row + margin) * scale;
      const startX = (col + margin) * scale;
      for (let dy = 0; dy < scale; dy++) {
        for (let dx = 0; dx < scale; dx++) {
          const offset = ((startY + dy) * size + (startX + dx)) * 4;
          data[offset] = 0;
          data[offset + 1] = 0;
          data[offset + 2] = 0;
        }
      }
    }
  }

  return { data, width: size, height: size };
}

test('createQrMatrix encodes an otpauth URI that authenticator apps can decode', () => {
  const matrix = createQrMatrix(OTPAUTH_URL);
  assert.ok(matrix.length >= 21, 'QR version size must be at least 21 modules');
  assert.equal(matrix.length % 4, 1, 'QR sizes are 21 + 4n');

  const image = matrixToImageData(matrix);
  const decoded = jsQR(image.data, image.width, image.height);
  assert.ok(decoded, 'QR matrix must be scannable');
  assert.equal(decoded.data, OTPAUTH_URL);
});

test('createQrMatrix rejects empty values', () => {
  assert.equal(createQrMatrix(''), null);
  assert.equal(createQrMatrix('   '), null);
});

export function constantTimeEqual(a: string, b: string): boolean {
  const enc = new TextEncoder();
  const aBuf = enc.encode(a);
  const bBuf = enc.encode(b);
  if (aBuf.byteLength !== bBuf.byteLength) {
    return false;
  }
  if (typeof (crypto as any)?.subtle?.timingSafeEqual === "function") {
    return (crypto as any).subtle.timingSafeEqual(aBuf, bBuf);
  }
  let diff = 0;
  for (let i = 0; i < aBuf.byteLength; i++) {
    diff |= aBuf[i] ^ bBuf[i];
  }
  return diff === 0;
}

export function parseSyncSqlHeaders(headers: Headers): string[] {
  const countHeader = headers.get("x-d1-sync-sql-count");
  if (countHeader) {
    const count = parseInt(countHeader, 10);
    const statements: string[] = [];
    for (let i = 0; i < count; i++) {
      const chunkB64 = headers.get(`x-d1-sync-sql-${i}`);
      if (chunkB64) {
        const bytes = Uint8Array.from(atob(chunkB64), (c) => c.charCodeAt(0));
        const chunkStmts: string[] = JSON.parse(new TextDecoder().decode(bytes));
        statements.push(...chunkStmts);
      }
    }
    return statements;
  }

  const singleB64 = headers.get("x-d1-sync-sql");
  if (singleB64) {
    const bytes = Uint8Array.from(atob(singleB64), (c) => c.charCodeAt(0));
    return JSON.parse(new TextDecoder().decode(bytes));
  }

  return [];
}

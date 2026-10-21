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
  if (countHeader !== null) {
    const count = parseInt(countHeader, 10);
    if (isNaN(count) || count < 0) {
      throw new Error(`Invalid x-d1-sync-sql-count header: "${countHeader}"`);
    }

    const statements: string[] = [];
    for (let i = 0; i < count; i++) {
      const headerName = `x-d1-sync-sql-${i}`;
      const chunkB64 = headers.get(headerName);
      if (chunkB64 === null) {
        throw new Error(`Missing expected chunk header: "${headerName}" (count=${count})`);
      }
      try {
        const bytes = Uint8Array.from(atob(chunkB64), (c) => c.charCodeAt(0));
        const chunkStmts = JSON.parse(new TextDecoder().decode(bytes));
        if (!Array.isArray(chunkStmts)) {
          throw new Error(`Chunk header "${headerName}" did not decode to a JSON array`);
        }
        statements.push(...chunkStmts);
      } catch (err: any) {
        throw new Error(`Failed to decode/parse chunk header "${headerName}": ${err?.message ?? err}`);
      }
    }
    return statements;
  }

  const singleB64 = headers.get("x-d1-sync-sql");
  if (singleB64 !== null) {
    try {
      const bytes = Uint8Array.from(atob(singleB64), (c) => c.charCodeAt(0));
      const statements = JSON.parse(new TextDecoder().decode(bytes));
      if (!Array.isArray(statements)) {
        throw new Error(`x-d1-sync-sql header did not decode to a JSON array`);
      }
      return statements;
    } catch (err: any) {
      throw new Error(`Failed to decode/parse x-d1-sync-sql header: ${err?.message ?? err}`);
    }
  }

  return [];
}

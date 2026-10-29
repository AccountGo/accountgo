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

export async function parseSyncSqlHeaders(headers: Headers): Promise<string[]> {
  const encoding = headers.get("x-d1-sync-encoding");
  if (encoding !== null && encoding !== "gzip") {
    throw new Error("Unsupported x-d1-sync-encoding header");
  }
  const countHeader = headers.get("x-d1-sync-sql-count");
  const chunks: string[] = [];
  if (countHeader !== null) {
    const count = Number(countHeader);
    if (!/^[1-9]\d*$/.test(countHeader) || !Number.isSafeInteger(count)) {
      throw new Error(`Invalid x-d1-sync-sql-count header`);
    }

    for (let i = 0; i < count; i++) {
      const headerName = `x-d1-sync-sql-${i}`;
      const chunkB64 = headers.get(headerName);
      if (chunkB64 === null) {
        throw new Error(`Missing expected chunk header: "${headerName}"`);
      }
      chunks.push(chunkB64);
    }
  } else {
    const singleB64 = headers.get("x-d1-sync-sql");
    if (singleB64 !== null) chunks.push(singleB64);
  }
  if (chunks.length === 0) return [];
  if (encoding === "gzip") {
    try {
      const bytes = Uint8Array.from(atob(chunks.join("")), c => c.charCodeAt(0));
      const stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream("gzip"));
      return validateStatements(JSON.parse(await new Response(stream).text()));
    } catch {
      throw new Error("Failed to decode/parse gzip sync headers");
    }
  }

  // Old containers emit independently encoded JSON arrays per chunk, without an encoding header.
  return chunks.flatMap((chunk, index) => {
    const label = countHeader !== null ? `chunk header "x-d1-sync-sql-${index}"` : "x-d1-sync-sql header";
    try {
      const bytes = Uint8Array.from(atob(chunk), c => c.charCodeAt(0));
      return validateStatements(JSON.parse(new TextDecoder().decode(bytes)));
    } catch (err) {
      const errorType = err instanceof SyntaxError ? "SyntaxError" : "Error";
      throw new Error(`Failed to decode/parse ${label}: ${errorType}`);
    }
  });
}

function validateStatements(value: unknown): string[] {
  if (!Array.isArray(value) || !value.every(statement => typeof statement === "string")) {
    throw new Error("Sync payload must be an array of SQL strings");
  }
  return value;
}

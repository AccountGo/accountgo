// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';

test('index.css binds Tailwind v4 dark: variant to [data-scheme="dark"] instead of OS prefers-color-scheme (E6)', () => {
  const cssPath = path.resolve(import.meta.dirname, '../src/index.css');
  const css = fs.readFileSync(cssPath, 'utf8');

  const importIdx = css.indexOf('@import "tailwindcss";');
  const variantRule = '@custom-variant dark (&:where([data-scheme="dark"], [data-scheme="dark"] *));';
  const variantIdx = css.indexOf(variantRule);

  assert.ok(importIdx >= 0, 'Expected @import "tailwindcss"; in src/index.css');
  assert.ok(variantIdx > importIdx, 'Expected @custom-variant dark right after @import "tailwindcss";');
});

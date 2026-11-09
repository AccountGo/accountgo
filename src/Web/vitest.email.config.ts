import { defineConfig } from 'vitest/config';

export default defineConfig({
  oxc: { jsx: { runtime: 'automatic' } },
  test: {
    environment: 'jsdom',
    include: ['test-ui/**/*.test.tsx'],
    testTimeout: 10000,
  },
});

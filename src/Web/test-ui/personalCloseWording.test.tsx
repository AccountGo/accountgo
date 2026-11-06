import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { YearEndCloseCard } from '../src/components/YearEndCloseCard';
vi.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ authFetch: async () => ({ ok: true, json: async () => [] }) }) }));
afterEach(cleanup);
it('uses household wording for year-end close', async () => {
  render(<YearEndCloseCard canClose personal />);
  expect(await screen.findByText('Close completed fiscal years.')).toBeTruthy();
  expect(document.body.textContent).not.toMatch(/revenue|profit/i);
});

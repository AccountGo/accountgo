import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { BusinessDetailsCard } from '../src/components/BusinessDetailsCard';
vi.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ user: { firstName: 'Pat', lastName: 'Owner', email: 'pat@example.test' }, authFetch: async () => ({ ok: true, json: async () => ({ legalName: 'Pat Owner', businessEmail: 'billing@example.test', businessPhone: null, address: { countryCode: 'US' } }) }) }) }));
afterEach(cleanup);
it('personal profile displays owner details and country without business contacts', async () => {
  render(<BusinessDetailsCard canEdit personal />);
  expect(await screen.findByLabelText('Country')).toHaveProperty('value', 'US');
  expect(screen.getByText('pat@example.test')).toBeTruthy();
  expect(screen.queryByLabelText('Business Email')).toBeNull();
  expect(screen.queryByLabelText('Business Phone')).toBeNull();
  expect(screen.queryByLabelText('Address Line 1')).toBeNull();
});

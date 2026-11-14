import assert from 'node:assert/strict';
import { after, test } from 'node:test';
import { createServer } from 'vite';

const server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false }, appType: 'custom' });
after(() => server.close());

const { buildTaxReportCsv, canShowTaxNav } = await server.ssrLoadModule('/src/lib/taxReport.ts');

test('canShowTaxNav enforces personal, no-tax, regime, positive-rate, and invoicing-only rules', () => {
  // Personal books -> false
  assert.equal(
    canShowTaxNav({ isPersonalBooks: true, activeModules: ['sales', 'purchases'], taxRegime: 'VatGst', usesTax: true }),
    false,
  );

  // No tax -> false
  assert.equal(
    canShowTaxNav({ isPersonalBooks: false, activeModules: ['sales', 'purchases', 'financials'], taxRegime: 'None', hasPositiveTaxRate: false }),
    false,
  );
  assert.equal(
    canShowTaxNav({ isPersonalBooks: false, activeModules: ['sales', 'purchases', 'financials'], usesTax: false }),
    false,
  );

  // Regime VatGst -> true
  assert.equal(
    canShowTaxNav({ isPersonalBooks: false, activeModules: ['sales', 'financials'], taxRegime: 'VatGst' }),
    true,
  );

  // Regime None with a 12% tax -> true
  assert.equal(
    canShowTaxNav({ isPersonalBooks: false, activeModules: ['sales', 'financials'], taxRegime: 'None', hasPositiveTaxRate: true }),
    true,
  );

  // Invoicing-only workspace (sales + purchases, no financials/reports) -> true
  assert.equal(
    canShowTaxNav({ isPersonalBooks: false, activeModules: ['sales', 'purchases'], usesTax: true }),
    true,
  );

  // No relevant module -> false
  assert.equal(
    canShowTaxNav({ isPersonalBooks: false, activeModules: ['reports'], usesTax: true }),
    false,
  );
});

test('buildTaxReportCsv includes metadata, formula guard, and Net refundable label', () => {
  const report = {
    from: '2026-08-01',
    to: '2026-10-31',
    basis: 'Accrual',
    currency: 'PHP',
    isWholePeriod: true,
    periodLabel: 'Aug\u2013Oct 2026',
    output: {
      rows: [
        { taxId: 1, taxCode: 'VAT12', taxName: 'VAT 12%', rate: 12, taxable: 1000, tax: 120, documentCount: 1 },
      ],
      taxable: 1000,
      tax: 120,
    },
    input: {
      rows: [
        { taxId: 1, taxCode: 'VAT12', taxName: 'VAT 12%', rate: 12, taxable: 2000, tax: 240, documentCount: 1 },
      ],
      taxable: 2000,
      tax: 240,
    },
    netTax: -120,
    documents: [
      {
        side: 'output',
        kind: 'invoice',
        id: 1,
        no: 'INV-1',
        date: '2026-08-15',
        party: '=CMD()',
        taxId: 1,
        taxable: 1000,
        tax: 120,
        posted: true,
      },
    ],
    documentsTruncated: false,
    warnings: { unposted: [] },
    ledger: null,
    lock: null,
  };

  const csv = buildTaxReportCsv(report, 'Acme Corp', '2026-11-14T15:00:00.000Z');
  assert.match(csv, /"Workspace","Acme Corp"/);
  assert.match(csv, /"Period","Aug\u2013Oct 2026"/);
  assert.match(csv, /"Basis","Accrual"/);
  assert.match(csv, /"Currency","PHP"/);
  assert.match(csv, /"Generated at","2026-11-14T15:00:00\.000Z"/);
  assert.match(csv, /"Net refundable","120\.00"/);
  assert.match(csv, /"'=CMD\(\)"/);
});

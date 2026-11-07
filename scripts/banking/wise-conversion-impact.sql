-- Read-only worksheet for Wise conversion rows that were imported before direction
-- eligibility was applied. Do not DELETE, UPDATE, or INSERT from this file.
--
-- Statement lines do not store the Wise direction token, so this query cannot
-- decide which rows are conversions. POST
-- /api/Banking/Accounts/{bankId}/StatementImports/WiseConversionImpact
-- with the original file. That report is read-only and returns the candidate
-- external ids with workspace, bank, import, match, and posting status.
-- Same-source external ID matches do not prove file provenance. Verify the
-- original import and source before taking any action.
-- Paste those external ids below, then use an authorized correction for each
-- state. Matched and reconciled rows need their own reversal or undo.
-- Payoneer and Stripe are not part of this report.

SELECT
    l.CompanyId,
    l.BankId,
    b.Name AS BankName,
    l.BankStatementImportId AS ImportId,
    i.FileName AS ImportFileName,
    l.Id AS StatementLineId,
    l.ExternalId,
    l.Amount,
    CASE
        WHEN l.ExcludedAt IS NOT NULL THEN 'excluded'
        WHEN l.MatchedGeneralLedgerLineId IS NOT NULL THEN 'matched'
        ELSE 'unmatched'
    END AS MatchStatus,
    CASE
        WHEN g.BankReconciliationId IS NOT NULL THEN 'reconciled'
        WHEN l.MatchedGeneralLedgerLineId IS NOT NULL THEN 'posted'
        WHEN l.DraftBillId IS NOT NULL THEN 'draft-bill'
        ELSE 'none'
    END AS PostingStatus,
    l.MatchedGeneralLedgerLineId,
    g.BankReconciliationId
FROM BankStatementLine l
JOIN Bank b ON b.Id = l.BankId AND b.CompanyId = l.CompanyId
JOIN BankStatementImport i ON i.Id = l.BankStatementImportId AND i.CompanyId = l.CompanyId
LEFT JOIN GeneralLedgerLine g ON g.Id = l.MatchedGeneralLedgerLineId AND g.CompanyId = l.CompanyId
WHERE l.ExternalId IN (
    -- Replace with external ids from the impact report. Leave none to return no rows.
    'ACT-INT-001'
);

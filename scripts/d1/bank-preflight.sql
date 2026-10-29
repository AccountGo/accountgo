-- Read-only preflight for #413 / D1 0032_bank_accounts.
-- Run against production before deploying 0032. Deploy is blocked until every
-- SELECT below returns zero rows. Fix remaining violations with a reviewed
-- one-off migration (D1 cannot loop to allocate dedicated cash accounts).
--
-- Expected on hosted test data: zero rows in every result set.

-- 1. Banks with no posting account
SELECT 'null_account' AS violation, b."Id" AS bank_id, b."CompanyId", b."Name", b."AccountId"
FROM "Bank" b
WHERE b."AccountId" IS NULL;

-- 2. Banks on a summary (non-postable) account
SELECT 'summary_account' AS violation, b."Id" AS bank_id, b."CompanyId", b."Name", b."AccountId", a."AccountCode"
FROM "Bank" b
JOIN "Account" a ON a."Id" = b."AccountId" AND a."CompanyId" = b."CompanyId"
WHERE EXISTS (
  SELECT 1 FROM "Account" child
  WHERE child."ParentAccountId" = a."Id" AND child."CompanyId" = a."CompanyId"
);

-- 3. Banks on a non-cash account (neither the account nor its parent is cash)
SELECT 'non_cash_account' AS violation, b."Id" AS bank_id, b."CompanyId", b."Name", b."AccountId", a."AccountCode"
FROM "Bank" b
JOIN "Account" a ON a."Id" = b."AccountId" AND a."CompanyId" = b."CompanyId"
WHERE COALESCE(a."IsCash", 0) = 0
  AND NOT EXISTS (
    SELECT 1 FROM "Account" parent
    WHERE parent."Id" = a."ParentAccountId"
      AND parent."CompanyId" = a."CompanyId"
      AND parent."IsCash" = 1
  );

-- 4. Two or more banks sharing one posting account
SELECT 'shared_account' AS violation, b."CompanyId", b."AccountId", COUNT(*) AS bank_count,
       GROUP_CONCAT(b."Id") AS bank_ids
FROM "Bank" b
WHERE b."AccountId" IS NOT NULL
GROUP BY b."CompanyId", b."AccountId"
HAVING COUNT(*) > 1;

-- 5. NULL or blank bank names (0032 fills these from the account name or "Bank {Id}")
SELECT 'blank_name' AS violation, b."Id" AS bank_id, b."CompanyId", b."Name", b."AccountId"
FROM "Bank" b
WHERE b."Name" IS NULL OR TRIM(b."Name") = '';

-- 6. Duplicate bank names within a company (case-insensitive)
SELECT 'duplicate_name' AS violation, b."CompanyId", lower(b."Name") AS name_key, COUNT(*) AS bank_count,
       GROUP_CONCAT(b."Id") AS bank_ids
FROM "Bank" b
WHERE b."Name" IS NOT NULL AND TRIM(b."Name") != ''
GROUP BY b."CompanyId", lower(b."Name")
HAVING COUNT(*) > 1;

-- 7. More than one default for receipts / payments per company
SELECT 'multiple_default_receipts' AS violation, b."CompanyId", COUNT(*) AS bank_count,
       GROUP_CONCAT(b."Id") AS bank_ids
FROM "Bank" b
WHERE b."IsDefault" = 1 OR b."IsDefaultForReceipts" = 1
GROUP BY b."CompanyId"
HAVING COUNT(*) > 1;

SELECT 'multiple_default_payments' AS violation, b."CompanyId", COUNT(*) AS bank_count,
       GROUP_CONCAT(b."Id") AS bank_ids
FROM "Bank" b
WHERE b."IsDefaultForPayments" = 1
GROUP BY b."CompanyId"
HAVING COUNT(*) > 1;

# Document capture implementation decisions

Status: implementation record for the approved document-capture design. Parser selection is approved conditionally by the architecture owner; implementation must stop and request a new decision if the pinned parser cannot meet the bounds below.

## Scope and invariants

- The capture service owns capture state only. It has no dependency on purchasing, sales, banking, payment, or ledger command services. A separate evidence-link operation follows a successful existing save.
- Stored capture identifiers use registered `cap_` public IDs. Internal capture, party, and target integer IDs never cross API or UI boundaries.
- `FakeDocumentFieldExtractor` is development/test only. The production adapter remains unconfigured until a later owner instruction names a provider. New extraction is disabled by default; evidence reads remain independently authorized.
- Accepted bytes and fields remain until workspace deletion. Abandoned unaccepted bytes expire after seven days; attempt reservations expire after 48 hours. Rejection deletes bytes only for bank and assistant captures.
- PDFs are retained byte-for-byte and served as authenticated attachments. Structural validation is not sanitization or malware certification.

## Bounded contracts

The API and UI share explicit, versioned kind schemas. Proposed limits for implementation and tests:

| Limit | Value |
|---|---:|
| Upload request and original PDF | 8 MiB |
| Stored normalized image | 1.5 MiB |
| Image dimensions | 8,000 px long edge; 24 megapixels |
| Normalized image long edge | 2,048 px |
| PDF pages | 1–10 |
| Financial lines | 100 |
| Assistant fields | 64 |
| Text field value | 2,000 characters |
| Line description | 1,000 characters |
| Raw text before masking/storage | 8,000 characters |
| Decimal representation | invariant string, at most 18 significant digits and 4 fractional digits |
| Object/list nesting | 4 levels |
| Extractor deadline | 20 seconds |
| Image decode concurrency | 4 slots, 500 ms admission wait |

PDF structure parsing is limited to 32 MiB aggregate decoded bytes, 8 MiB per decoded stream, 50,000 visited indirect objects, 1,000 page-tree visits, and depth 64. The PDF parse gate uses four slots and a 500 ms admission wait. Deadline checks must run in bounded input/filter/structure traversal code; no detached `Task.Run` timeout may leave CPU work running.

The accept request body is capped at 256 KiB, link requests at 4 KiB, and assistant run-stream bodies at 16 KiB before proxy buffering. The existing 8 MiB + 64 KiB upload route cap applies only to capture uploads. MVC and worker checks both enforce the limits, including unknown-length and encoded-path requests.

## Persistence and serialization

- SQLite admission uses an immediate write transaction (`BEGIN IMMEDIATE`/provider equivalent) with a bounded busy wait; the transaction rechecks company liveness, idempotency, lease, and rolling attempt counts before inserting a durable reservation.
- PostgreSQL admission uses a transaction-scoped advisory lock keyed by company and a distinct namespace for purge coordination. It serializes the count-and-reserve decision across API instances.
- SQL Server admission uses `sp_getapplock` with an exclusive transaction-owned resource keyed by company and a bounded timeout. This implementation receives provider-specific lock tests where the current test infrastructure supports SQL Server.
- Provider calls run only after the reservation transaction commits. Completion uses capture generation and company-liveness conditions so stale or deleted work cannot overwrite newer state.

## Review and access boundaries

- Optional existing-writer context is a nullable `CaptureReviewContext` containing a `cap_` capture ID, a canonical source-total snapshot, the current form-total snapshot, and a user acknowledgement bound to those values. The writer independently loads accepted canonical capture fields, recalculates authoritative totals, and rejects stale or absent acknowledgement when required. It never trusts a client `totalsMatch` flag.
- Resource-specific evidence reads use the existing authorization service: unlinked captures require the relevant module's capture-review read capability; linked evidence requires target read permission and target resource access on every metadata and byte path. Bank evidence additionally requires the existing sensitive banking permission. Assistant evidence is creator-only.
- Company liveness is checked after tenant resolution and again in state-changing transactions. Cleanup commits `DeletedAt` first and is retried by a bounded purge worker.

## PDF parser decision and policy

Approved candidate: `UglyToad.PdfPig` 0.1.16, exact stable pin, Apache-2.0. The official repository and release history show active maintenance, including stack-depth and malformed-input fixes. No prerelease or floating version is permitted.

The parser is used for structure only: catalog, active page tree, and encryption metadata. It must never decode page content streams, fonts, or images. Reject `/Encrypt`. Enforce decoded-byte, indirect-object, page-tree, nesting-depth, and cooperative deadline caps in custom parser/filter hooks. Cycles, malformed structures, unsupported filters, exceptions, or any cap hit fail closed as malformed/unsupported input. Never use detached timeout tasks.

Required adversarial fixtures cover Flate bombs in object and xref streams, cyclic/deep page trees, dishonest `/Count`, incremental page-tree replacement, unrelated `/Type /Page` text in a stream, encryption, 11 pages, truncation, and bounded peak time/memory. Verify the package and all transitive licenses before adding it. If its 0.1.16 filter-provider hook cannot enforce the aggregate bound, stop and ask the architecture owner rather than bypassing the constraint.

Evidence: the repository describes PdfPig as Apache-2.0 licensed and publishes release history at `https://github.com/UglyToad/PdfPig`. The 0.1.16 release notes identify the public filter-provider hook and stack-depth/malformed-input fixes. These are evidence of the extension point and maintenance, not proof of our application-level ceilings; implementation tests must establish those ceilings.

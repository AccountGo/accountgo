# Document capture implementation decisions

Status: implementation record for the approved document-capture design. PDF parser selection is pending the architecture owner's decision.

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

## PDF parser decision request

Candidate for review: `UglyToad.PdfPig` (Apache-2.0). The official repository documents the license and the release history shows active maintenance, including a stable 0.1.16 release with stack-depth and malformed-input fixes. The current NuGet profile also shows a newer prerelease, so the implementation would pin a stable release and avoid floating versions.

Open safety question: public release notes and issues indicate decompression bounds have received fixes, but the current public API evidence does not establish a strict aggregate decoded-byte limit for every supported filter. The implementation must not rely on file size, page count, or `MaxStackDepth` alone as a decompression bound. Before adding this dependency, the owner must approve PdfPig only with a proven bounded filter/decode path and tests, or direct selection of another parser. No PDF dependency has been added.

Evidence: the repository describes PdfPig as Apache-2.0 licensed and publishes its releases at `https://github.com/UglyToad/PdfPig`; release notes for 0.1.15/0.1.16 mention malformed-input and recursion/decompression fixes. License and release history are not evidence of a hard application-level resource ceiling.

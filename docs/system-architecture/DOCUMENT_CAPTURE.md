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

PDF parsing is limited to 8 MiB input, 32 MiB aggregate decoded bytes, 8 MiB per decoded stream, `MaxStackDepth` 64, and a cooperative deadline enforced inside custom filter decoders. The PDF parse gate uses four slots and a 500 ms admission wait. PdfPig 0.1.16 has no per-object or page-tree visit hook; do not claim a per-object count cap. Input and decoded-byte caps bound the available structure data, and adversarial tests must demonstrate that pathological structures terminate within fixed time and memory ceilings. Never use detached `Task.Run` timeouts.

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

The parser is used for structure only: catalog, active page tree, and encryption metadata. It must never decode page content streams, fonts, or images. Reject `/Encrypt`; accept only 1–10 pages after open. Enforce decoded-byte, nesting-depth, and cooperative deadline limits in custom filter hooks. The lack of per-object/page-tree hooks is recorded as a bounded-behavior limitation: the 8 MiB input and 32 MiB aggregate decode caps bound structure data, and adversarial tests must establish termination within fixed time and memory budgets. Cycles, malformed structures, unsupported filters, exceptions, or any cap hit fail closed as malformed/unsupported input.

Required adversarial fixtures cover Flate bombs in object and xref streams, cyclic/deep page trees, dishonest `/Count`, huge `/Kids`, thousands of tiny objects, incremental page-tree replacement, unrelated `/Type /Page` text in a stream, encryption, 11 pages, truncation, and bounded peak time/memory on an 8 MiB worst-case input. Run hard-timeout/memory tests in a subprocess where needed so a hang fails only the test process. If any case cannot be bounded in-process, stop and ask the architecture owner; the fallback is an out-of-process validator with OS-level limits, not a hand-written second parser.

Evidence: the repository describes PdfPig as Apache-2.0 licensed and publishes release history at `https://github.com/UglyToad/PdfPig`; NuGet metadata for 0.1.16 declares Apache-2.0 and no dependencies for .NET 8/9 target groups. The 0.1.16 source exposes `IFilterProvider`/`IFilter.Decode` and `ParsingOptions.MaxStackDepth`. These prove the extension points, not our application-level ceilings; implementation tests must establish those ceilings. There is no per-object hook, so we rely on bounded input/decode data and adversarial time/memory termination tests, and will not claim a separate object-count cap.

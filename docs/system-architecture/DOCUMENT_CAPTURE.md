# Document capture implementation decisions

Status: implementation record for the approved document-capture design. Parser selection is approved conditionally by the architecture owner; implementation must stop and request a new decision if the pinned parser cannot meet the bounds below.

## Scope and invariants

- The capture service owns capture state only. It has no dependency on purchasing, sales, banking, payment, or ledger command services. A separate evidence-link operation follows a successful existing save.
- Stored capture identifiers use registered `cap_` public IDs. Internal capture, party, and target integer IDs never cross API or UI boundaries.
- `FakeDocumentFieldExtractor` is development/test only. The production adapter remains unconfigured until a later owner instruction names a provider. New extraction is disabled by default; evidence reads remain independently authorized.
- Accepted bytes and fields remain until workspace deletion. Abandoned unaccepted bytes expire after seven days; attempt reservations expire after 48 hours. Rejection deletes bytes only for bank and assistant captures.
- PDFs are retained byte-for-byte and served as authenticated attachments. Structural validation is not sanitization or malware certification.

## Bounded contracts

The API and UI share explicit, versioned kind schemas. Enforced limits:

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
| Image decode concurrency | 4 slots by default, configurable from 1–4; 500 ms admission wait |
| PDF parse concurrency | 1 slot by default, configurable from 1–4; 500 ms admission wait |

Image normalization uses the public API's pinned `SkiaSharp` and Linux native asset version. A test compares those pins to the Branding project when the hosted worktree is present. The image path reads codec format, dimensions, and frame count before pixel decode; it accepts JPEG/PNG only, caps the long edge at 8,000 px and total pixels at 24 MP, then emits normalized PNG up to 2,048 px. If PNG output exceeds 1.5 MiB, it composites transparency onto white and tries JPEG qualities 85, 70, 55, and 40 in order. A local four-way 24 MP JPEG run measured 419,790,848 bytes baseline working set and 752,697,344 bytes peak (332,906,496 bytes growth).

NuGet audit for the API project found no vulnerable or deprecated SkiaSharp 2.88.9 packages. The same audit reported existing high advisories for AutoMapper 12.0.1 and marked `AutoMapper.Extensions.Microsoft.DependencyInjection` 12.0.1 as legacy; these are unrelated to the image-normalization dependency. The API Docker image built for Linux ARM64, and a published smoke executable invoking `DocumentCaptureImageNormalizer` inside that image returned `image/png 4x4 95`, confirming native codec load and decode.

PDF parsing is limited to 8 MiB input, 32 MiB aggregate decoded bytes, 8 MiB per decoded stream, `MaxStackDepth` 64, and a cooperative deadline enforced inside custom filter decoders. The independent PDF parse gate defaults to one slot and has a 500 ms admission wait; it is configurable separately from the four-slot image decode gate. PdfPig 0.1.16 has no per-object or page-tree visit hook; do not claim a per-object count cap. Input and decoded-byte caps bound the available structure data, and adversarial tests establish termination within fixed time and memory ceilings. Never use detached `Task.Run` timeouts.

### Measured resource envelope

`wrangler.jsonc` selects Cloudflare Container instance types `standard-1` for one pool and `lite` for the other. Cloudflare documents these as 4 GiB and 256 MiB respectively ([Limits and Instance Types](https://developers.cloudflare.com/containers/platform/limits/)). The Dockerfile also targets small-container operation. The following are development-host measurements, not production-container measurements:

| Operation | Concurrency | Input | Allocated | Peak working-set growth | Wall time |
|---|---:|---:|---:|---:|---:|
| JPEG decode/normalize at 24 MP | 4 | 4 images | not recorded | 332,906,496 bytes aggregate | not recorded |
| PdfPig: 175,000 small indirect objects | 1 | 7,764,270 bytes | 143,770,072 bytes | 92,962,816 bytes | 702 ms |
| PdfPig: compressed object stream and xref stream | 1 | 8,388,608 bytes | 137,374,688 bytes | 104,857,600 bytes | 1,144 ms |

PDF adversarial tests run in a subprocess and fail above 220,000,000 allocated bytes, 160,000,000 bytes of sampled peak working-set growth, or the 3-second parser deadline. These ceilings leave at least 50% margin over the measured worst case. The deployment budget comparison is:

| Pool instance | Budget | Four 24 MP image decodes | One PDF parse slot | Verdict |
|---|---:|---:|---:|---|
| `lite` | 268,435,456 bytes | 332,906,496 bytes growth exceeds the whole budget | 104,857,600 bytes measured growth | Image concurrency is unsafe; capture must remain unavailable on this instance type |
| `standard-1` | 4,294,967,296 bytes | 332,906,496 bytes growth | 104,857,600 bytes measured growth | Measured operation growth fits; validate under the deployed container before enabling |

The image slot count remains four by default per the approved design, and the PDF count defaults to one. Since the runtime flag is shared and does not select a container pool, enabling capture while traffic can reach `lite` is a launch blocker. Operations must route enabled capture traffic only to capacity that passes the production memory test, or configure a lower image slot count and repeat that test. Keep the runtime flag off until this gate is cleared.

The API exposes capability discovery at `GET /api/document-captures/capabilities`, multipart upload at `POST /api/document-captures`, metadata at `GET /{cap_id}`, stored media at `GET /{cap_id}/image`, review actions at `POST /{cap_id}/accept` and `/reject`, and association at `POST /{cap_id}/links`. Upload accepts `force=true` to bypass only content-hash deduplication. The accept request body is capped at 256 KiB, link requests at 4 KiB, and assistant run-stream bodies at 16 KiB before proxy buffering. The existing 8 MiB + 64 KiB upload route cap applies only to capture uploads. MVC and worker checks both enforce the limits, including unknown-length and encoded-path requests.

## Persistence and serialization

- SQLite admission uses an immediate write transaction (`BEGIN IMMEDIATE`/provider equivalent) with a bounded busy wait; the transaction rechecks company liveness, idempotency, lease, and rolling attempt counts before inserting a durable reservation.
- New extraction reservations are limited to 20 per rolling hour and 60 per UTC day per company. Terminal idempotent replays and active-lease status checks do not create reservations.
- Every state-changing path locks the company row before any capture row. PostgreSQL uses `SELECT ... FOR UPDATE` on the live company row; purge locks the deleted company row while bypassing the live-company filter. SQL Server uses `UPDLOCK, ROWLOCK, HOLDLOCK` on that row with a bounded lock timeout. SQLite uses an immediate write transaction with a bounded busy wait. This ordering serializes workspace deletion against admission, retry, completion, accept, reject, linking, and purge.
- PostgreSQL advisory locking is reserved for a singleton purge-worker batch across API instances. Per-company purge steps still acquire the company row lock; they do not use a separate advisory namespace.
- Provider calls run only after the reservation transaction commits. Completion uses capture generation and company-liveness conditions so stale or deleted work cannot overwrite newer state.

## Review and access boundaries

- Optional existing-writer context is a nullable `CaptureReviewContext` containing a `cap_` capture ID, a canonical source-total snapshot, the current form-total snapshot, and a user acknowledgement bound to those values. The writer independently loads accepted canonical capture fields, recalculates authoritative totals, and rejects stale or absent acknowledgement when required. It never trusts a client `totalsMatch` flag.
- Resource-specific evidence reads use the existing authorization service: unlinked captures require the relevant module's capture-review read capability; linked evidence requires target read permission and target resource access on every metadata and byte path. Bank evidence additionally requires the existing sensitive banking permission. Assistant evidence is creator-only.
- Company liveness is checked after tenant resolution and again in state-changing transactions. Cleanup commits `DeletedAt` first and is retried by a bounded purge worker.

## PDF parser decision and policy

Approved candidate: `UglyToad.PdfPig` 0.1.16, exact stable pin, Apache-2.0. The official repository and release history show active maintenance, including stack-depth and malformed-input fixes. No prerelease or floating version is permitted.

The parser is used for structure only: catalog, active page tree, and encryption metadata. It must never decode page content streams, fonts, or images. Reject `/Encrypt`; require a terminal `%%EOF`; accept only 1–10 pages after open. Enforce decoded-byte, nesting-depth, and cooperative deadline limits in custom filter hooks. The lack of per-object hooks is recorded as a bounded-behavior limitation: the 8 MiB input and 32 MiB aggregate decode caps bound structure data, and adversarial tests establish termination within fixed time and memory budgets. Cycles, malformed structures, unsupported filters, exceptions, or any cap hit fail closed as malformed/unsupported input.

The conservative active-content policy rejects catalog `/OpenAction`, `/AA`, `/AcroForm`, `/Collection`, `/Perms`, or `/AF`; `/Names` is rejected only when it contains `/JavaScript` or `/EmbeddedFiles`, so ordinary named destinations remain supported. Active page-tree inspection is bounded to 64 levels, 5,000 nodes, and 500 children per node. On each of the at most ten active pages, reject `/AA`, more than 500 annotations, annotation subtypes `/Screen`, `/RichMedia`, `/FileAttachment`, `/Movie`, `/Sound`, `/3D`, or `/Widget`, and action types `/JavaScript`, `/Launch`, `/SubmitForm`, `/ImportData`, `/Rendition`, `/GoToR`, `/GoToE`, `/Sound`, or `/Movie`. Plain `/Link` annotations with URI or internal `GoTo` actions and ordinary text/highlight/stamp annotations remain supported. Active-content inspection uses PdfPig's resolved object dictionaries and fails closed on malformed or unresolved references. This is a conservative content policy, not malware certification; PDFs remain authenticated attachments served with `private, no-store` and `nosniff`.

Required adversarial fixtures cover Flate bombs in object and xref streams, cyclic/deep page trees, dishonest `/Count`, huge `/Kids`, thousands of tiny objects, incremental page-tree replacement, unrelated `/Type /Page` text in a stream, encryption, 11 pages, truncation, and bounded peak time/memory on an 8 MiB worst-case input. Run hard-timeout/memory tests in a subprocess where needed so a hang fails only the test process. If any case cannot be bounded in-process, stop and ask the architecture owner; the fallback is an out-of-process validator with OS-level limits, not a hand-written second parser.

Evidence: the repository describes PdfPig as Apache-2.0 licensed and publishes release history at `https://github.com/UglyToad/PdfPig`; NuGet metadata for 0.1.16 declares Apache-2.0 and no dependencies for .NET 8/9 target groups. The 0.1.16 source exposes `IFilterProvider`/`IFilter.Decode` and `ParsingOptions.MaxStackDepth`. These prove the extension points, not our application-level ceilings; implementation tests must establish those ceilings. There is no per-object hook, so we rely on bounded input/decode data and adversarial time/memory termination tests, and will not claim a separate object-count cap.

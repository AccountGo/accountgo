# Document capture implementation decisions

Status: implementation record for the approved document-capture design. Parser selection is approved conditionally by the architecture owner; implementation must stop and request a new decision if the pinned parser cannot meet the bounds below.

## Scope and invariants

- The capture service owns capture state only. It has no dependency on purchasing, sales, banking, payment, or ledger command services. A separate evidence-link operation follows a successful existing save.
- Stored capture identifiers use registered `cap_` public IDs. Internal capture, party, and target integer IDs never cross API or UI boundaries.
- `LocalOcrDocumentFieldExtractor` is the opt-in self-hosted reader. `FakeDocumentFieldExtractor` is for development and tests. New extraction is disabled by default and evidence reads remain independently authorized.
- Accepted bytes and fields remain until workspace deletion. Abandoned unaccepted bytes expire after seven days; attempt reservations expire after 48 hours. Rejection deletes bytes only for bank and assistant captures.
- The runtime flag controls upload, retry, and accept. It does not block reject, linking an already accepted capture, or authorized evidence reads. Provider availability is reported separately and gates upload/extraction, not manual acceptance.
- The feature grant gates new upload and retry. Accepting an existing capture uses the existing kind-specific write permission and module access, plus the runtime flag; it does not require the new-capture feature grant.
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
| Minimum extraction lease | 30 seconds (extractor deadline plus finalization margin) |
| Image decode concurrency | 4 slots by default, configurable from 1–4; 500 ms admission wait |
| PDF parse concurrency | 1 slot by default, configurable from 1–4; 500 ms admission wait |

Image normalization uses the public API's pinned `SkiaSharp` and Linux native asset version. A test compares those pins to the Branding project when the hosted worktree is present. The image path reads codec format, dimensions, and frame count before pixel decode; it accepts JPEG/PNG only and caps the long edge at 8,000 px and total pixels at 24 MP. JPEG decode uses the largest codec scale whose long edge remains at least 2,048 px; images are resized to 2,048 px before EXIF orientation is applied. PNG must decode at full resolution and is resized before orientation. Output is normalized PNG up to 2,048 px. If PNG output exceeds 1.5 MiB, it composites transparency onto white and tries JPEG qualities 85, 70, 55, and 40 in order. Local four-way 24 MP measurements: orientation-6 JPEG baseline 818,036,736 bytes, peak 910,393,344 bytes, growth 92,356,608 bytes; PNG baseline 330,924,032 bytes, peak 745,095,168 bytes, growth 414,171,136 bytes.

NuGet audit for the API project found no vulnerable or deprecated SkiaSharp 2.88.9 packages. The same audit reported existing high advisories for AutoMapper 12.0.1 and marked `AutoMapper.Extensions.Microsoft.DependencyInjection` 12.0.1 as legacy; these are unrelated to the image-normalization dependency. The API Docker image built for Linux ARM64, and a published smoke executable invoking `DocumentCaptureImageNormalizer` inside that image returned `image/png 4x4 95`, confirming native codec load and decode.

PDF parsing is limited to 8 MiB input, 32 MiB aggregate decoded bytes, 8 MiB per decoded stream, `MaxStackDepth` 64, and a cooperative deadline enforced inside custom filter decoders. The independent PDF parse gate defaults to one slot and has a 500 ms admission wait; it is configurable separately from the four-slot image decode gate. PdfPig 0.1.16 has no per-object or page-tree visit hook; do not claim a per-object count cap. Input and decoded-byte caps bound the available structure data, and adversarial tests establish termination within fixed time and memory ceilings. Never use detached `Task.Run` timeouts.

When capture is enabled, a background worker parses a tiny embedded PDF once after the host signals startup. It uses the configured PDF gate and a separate 15-second cooperative deadline for the embedded fixture. The worker does not delay readiness. A failed warm-up logs a warning without parser exceptions or document content. Uploads keep their three-second cooperative deadline. An expired parse returns `503 DOCUMENT_BUSY` with `Retry-After: 1`. PDF gate contention remains `429 DOCUMENT_DECODE_BUSY`, and malformed files and decoded-byte limit violations keep their input error codes. PdfPig can spend time in startup or parser code that does not call the deadline hooks, so a timeout response can arrive after three seconds.

### Local document reading

Set `DocumentCapture:Enabled=true` and `DocumentCapture:Extractor=local-ocr` to
enable reading on a self-hosted instance with the worker installed. By default
the extractor setting is empty. The API image bundles a ReadyToRun worker, its PDFium
libraries, and the pinned PP-OCRv5 mobile detection and English recognition
models. For a local build, publish `src/Api.CaptureWorker/Api.CaptureWorker.csproj`
and set `DocumentCapture:Extractor:WorkerPath` to the absolute path of the
published `Api.CaptureWorker.dll`. Startup fails if the worker or a required
model file is missing. The worker verifies all three model asset hashes before
processing each request.

PDF validation still uses the structure-only policy described above. The child
reads the text layer with PDFium, groups it by glyph positions, and falls back
to OCR when a page has fewer than 20 non-space characters. For a document
with more than three such pages, it OCRs the first two and the last, and returns
`OCR_PARTIAL_PAGES` for the skipped pages. Page rendering uses 200 dpi and caps
the long edge at 2,048 pixels. The recognition model supports English. A
PDF's existing text layer can include other languages.

The API starts one worker process per extraction. The length-prefixed request
and the result travel through redirected standard streams. The worker receives
no application secrets through its environment. Logs omit document bytes,
extracted text, and child stderr. The parent gives the worker 20 seconds,
samples resident memory every 100 ms, and reads at most 256 KiB of output.
It kills and reaps the process tree on cancellation or a limit breach.
`DocumentCapture:Extractor:MaxWorkerMemoryMb` defaults to 1,024 MiB.
`DocumentCapture:Extractor:MaxConcurrency` defaults to 1 and accepts 1–4.
The instance queues up to four waiters per worker slot for up to 15 seconds.
A full queue or an expired wait returns `429 DOCUMENT_DECODE_BUSY` before a
capture or quota attempt is written. The worker deadline returns
`503 DOCUMENT_BUSY`, and a worker failure returns `503 DOCUMENT_PROVIDER_UNAVAILABLE`.
Both responses include `Retry-After: 1`. An unreadable document returns
`422 DOCUMENT_UNREADABLE`; excess memory or output returns `422 DOCUMENT_TOO_COMPLEX`.
Each extraction logs its outcome, exit code, queue and run times, peak sampled
RSS, page count, OCR page count, and line count.
`DocumentCapture:Extractor:IntraOpThreads` defaults
to 2. The worker uses one inter-op thread. The CPU memory arena is enabled by
default and can be disabled with `DocumentCapture:Extractor:EnableCpuMemoryArena=false`.

Hosted activation requires staging results on standard-1 for worker peak RSS,
p95 time per page, and 10 concurrent uploads against the one-slot gate, plus
the existing storage, backup and rollout checks. Local OCR remains unsuitable
for lite capacity.

### Production topology (hosted Cloudflare Containers)

In hosted production, ordinary traffic runs on the V1 `lite` container while
capture uploads run on the V2 `paid` pool at `standard-1`:

- Set `DOCUMENT_CAPTURE_EXTRACTION_POOL=paid` on the Cloudflare Worker to route
  only `POST /api/document-captures` (uploads and forced re-uploads) to the V2
  `paid` pool object (`v2-paid-<versionId>`, `standard-1`), regardless of
  whether `TIERED_CAPACITY_ENABLED` is on. All other capture endpoints
  (`capabilities`, metadata, evidence bytes, `accept`, `reject`, `links`) stay
  on their existing route so normal browsing does not keep a `standard-1`
  container awake.
- While `DOCUMENT_CAPTURE_EXTRACTION_POOL=paid` is set, the Worker enforces
  `DocumentCapture__ExtractionHost=false` on the V1 `lite` container (and any
  V2 `free` pool container) and `DocumentCapture__ExtractionHost=true` on the
  V2 `paid` pool container, overriding any ops-provided value.
- On an instance with `DocumentCapture:ExtractionHost=false`, `POST /api/document-captures`
  returns `503 DOCUMENT_CAPTURE_UNCONFIGURED` with `Retry-After: 1` before any
  reservation, quota attempt, media preparation, or worker start, and
  `DocumentCapturePdfWarmup` does not run. `GET /api/document-captures/capabilities`
  still reports `enabled` and `providerAvailable` from the normal flags, and
  accept, reject, link, and evidence reads work normally.
- `DocumentCapture:ExtractionHost=false` is also valid when `DocumentCapture:Enabled=false`
  so ops can flip `DocumentCapture__Enabled=false` as a kill switch without
  clearing `DOCUMENT_CAPTURE_EXTRACTION_POOL`.
- On `GET /api/document-captures/capabilities?warmup=1` with the pool var set
  (opt-in, sent only by `useDocumentCapture` when a scan panel opens), the
  Worker fires a non-blocking `GET /health` warm-up to the V2 `paid` pool via
  `ctx.waitUntil`, throttled to at most once per isolate per 60 seconds, so
  ordinary sidebar/page availability checks never wake the `standard-1` container.

### Measured resource envelope

Local OCR development-host measurements used an Apple M1 running macOS 26.6.2
and an ARM64 Release worker published with ReadyToRun. The worker ran with two
intra-op threads. No builds or test suites were running during the benchmark.
Normal macOS background activity continued. Each of the 65 synthetic single-page
English images ran once with the arena on and once with it off, alternating the
order. Each timed run started a fresh process and included model hash verification
and loading. Peak RSS is the largest child maximum resident set in each group.

| CPU memory arena | Pages | Median seconds per page | p95 seconds per page | Peak RSS |
|---|---:|---:|---:|---:|
| On (default) | 65 | 0.698 | 0.870 | 252,805,120 bytes |
| Off | 65 | 0.697 | 0.870 | 254,230,528 bytes |

The English image evaluation matched vendor, date, and total on all 65 fixtures.
The PDF text-layer evaluation matched all three fields on all 18 fixtures.
These development-host results inform the staging measurements above.
The worker publishes pinned PDFium 159.0.8152 through
[`bblanchon.PDFium.Linux`](https://www.nuget.org/packages/bblanchon.PDFium.Linux/159.0.8152)
for Linux x64 and ARM64 and
[`bblanchon.PDFium.macOS`](https://www.nuget.org/packages/bblanchon.PDFium.macOS/159.0.8152)
for local macOS development.

`wrangler.jsonc` selects Cloudflare Container instance types `standard-1` for one pool and `lite` for the other. Cloudflare documents these as 4 GiB and 256 MiB respectively ([Limits and Instance Types](https://developers.cloudflare.com/containers/platform/limits/)). The Dockerfile also targets small-container operation. The following are development-host measurements, not production-container measurements:

| Operation | Concurrency | Input | Allocated | Peak working-set growth | Wall time |
|---|---:|---:|---:|---:|---:|
| Orientation-6 JPEG decode/normalize at 24 MP | 4 | 4 images | not recorded | 92,356,608 bytes aggregate | 292 ms |
| PNG decode/normalize at 24 MP | 4 | 4 images | not recorded | 414,171,136 bytes aggregate | 1 s |
| PdfPig: 175,000 small indirect objects | 1 | 7,764,270 bytes | 143,770,072 bytes | 92,962,816 bytes | 702 ms |
| PdfPig: compressed object stream and xref stream | 1 | 8,388,608 bytes | 137,374,688 bytes | 104,857,600 bytes | 1,144 ms |

PDF adversarial tests run in a subprocess and fail above 220,000,000 allocated bytes, 160,000,000 bytes of sampled peak working-set growth, or the 3-second parser deadline. These ceilings leave at least 50% margin over the measured worst case. The deployment budget comparison is:

| Pool instance | Budget | Four 24 MP image decodes | One PDF parse slot | Verdict |
|---|---:|---:|---:|---|
| `lite` | 268,435,456 bytes | JPEG 92,356,608 bytes; PNG 414,171,136 bytes exceeds the whole budget | 104,857,600 bytes measured growth | PNG concurrency is unsafe; capture must remain unavailable on this instance type |
| `standard-1` | 4,294,967,296 bytes | JPEG 92,356,608 bytes; PNG 414,171,136 bytes | 104,857,600 bytes measured growth | Measured operation growth fits; validate under the deployed container before enabling |

On the local Linux ARM64 runtime, the probe parsed a synthetic 789-byte one-page invoice in fresh containers with `--cpus=0.0625 --memory=256m --memory-swap=256m` while backend tests were running on the host. Both completed cold parses exceeded three seconds (14,576 and 13,097 ms) and returned retryable busy. Using the probe's `--warm` option, which runs the production embedded fixture first, the same invoice parsed successfully in 695 and 1,199 ms. The fixture itself returned retryable busy after 16,492 and 30,205 ms, so its cooperative budget was also exceeded, although subsequent parsing succeeded. Docker stopped during the third pair when local disk space ran out. The three-second deadline held for both measured warm parses and failed for both measured cold parses. A consistently successful warm-up remains unverified at this capacity.

The image slot count remains four by default per the approved design, and the PDF count defaults to one. Since the runtime flag is shared and does not select a container pool, enabling capture while traffic can reach `lite` is a launch blocker. Operations must route enabled capture traffic only to capacity that passes the production memory test, or configure a lower image slot count and repeat that test. Keep the runtime flag off until this gate is cleared.

The API exposes capability discovery at `GET /api/document-captures/capabilities`, multipart upload at `POST /api/document-captures`, metadata at `GET /{cap_id}`, stored media at `GET /{cap_id}/image`, review actions at `POST /{cap_id}/accept` and `/reject`, and association at `POST /{cap_id}/links`. Upload accepts `force=true` to bypass only content-hash deduplication. The accept request body is capped at 256 KiB, link requests at 4 KiB, and assistant run-stream bodies at 16 KiB before proxy buffering. The existing 8 MiB + 64 KiB upload route cap applies only to capture uploads. MVC and worker checks both enforce the limits, including unknown-length and encoded-path requests.

## Persistence and serialization

- SQLite admission uses an immediate write transaction (`BEGIN IMMEDIATE`/provider equivalent) with a bounded busy wait; the transaction rechecks company liveness, idempotency, lease, and rolling attempt counts before inserting a durable reservation.
- New extraction reservations are limited to 20 per rolling hour and 60 per UTC calendar day per company. An omitted `Idempotency-Key` has no replay guarantee; source-hash deduplication still applies. Terminal idempotent replays and active-lease status checks do not create reservations.
- Every state-changing path locks the company row before any capture row. PostgreSQL uses `SELECT ... FOR UPDATE` on the live company row; purge locks the deleted company row while bypassing the live-company filter. SQL Server uses `UPDLOCK, ROWLOCK, HOLDLOCK` on that row with a bounded lock timeout. SQLite uses an immediate write transaction with a bounded busy wait. This ordering serializes workspace deletion against admission, retry, completion, accept, reject, linking, and purge.
- PostgreSQL advisory locking is reserved for a singleton purge-worker batch across API instances. Per-company purge steps still acquire the company row lock; they do not use a separate advisory namespace.
- Provider calls run only after the reservation transaction commits. Completion uses capture generation and company-liveness conditions so stale or deleted work cannot overwrite newer state.

## Review and access boundaries

- Optional existing-writer context is a nullable `CaptureReviewContext` containing a `cap_` capture ID, a canonical source-total snapshot, the current form-total snapshot, and a user acknowledgement bound to those values. The writer independently loads accepted canonical capture fields, recalculates authoritative totals, and rejects stale or absent acknowledgement when required. It never trusts a client `totalsMatch` flag.
- Resource-specific evidence reads use the existing authorization service: unlinked captures require the relevant module's capture-review read capability; linked evidence requires target read permission and target resource access on every metadata and byte path. Bank evidence additionally requires the existing sensitive banking permission. Assistant evidence is creator-only.
- Bank captures have no link target in this version. The bank form uses the panel to fill bank details only; accepted bank captures are retained under the retention rules but are not attached to a bank account. This keeps sensitive bank images off any additional read path. A later change may add a bank target guarded by the sensitive banking permission.
- Company liveness is checked after tenant resolution and again in state-changing transactions. Cleanup commits `DeletedAt` first and is retried by a bounded purge worker.
- Sensitive account identifiers in financial free text are replaced completely with `••••` before storage. Masked fields are `notes` and each financial line's `description`, plus assistant field `label`/`value` and bounded extractor raw text. Structured values such as invoice numbers, amounts, quantities, dates, and routing/account fields are not rewritten by the financial free-text masker.
- Account-like digit runs are masked except when the complete matched token parses as `yyyy-MM-dd` with a year from 1990 through 2100. Compact dates are masked; masking remains a heuristic, not complete PII detection.
- Company attempts are reserved transactionally. Admission-lock contention returns `503 DOCUMENT_BUSY` with `Retry-After: 1`; it is not reported as a rate limit. Rate-limit responses remain `429 DOCUMENT_RATE_LIMITED`.
- `Idempotency-Key` may be omitted. An explicitly empty or over-80-character value returns `400 DOCUMENT_IDEMPOTENCY_KEY_INVALID`; an omitted key relies on source-hash deduplication and has no key replay guarantee.
- Runtime-disabled accept returns `503 DOCUMENT_CAPTURE_DISABLED`; upload while disabled or without a configured provider returns `503 DOCUMENT_CAPTURE_UNCONFIGURED`.
- The retention worker waits 60 seconds after process start by default before its first purge pass. `DocumentCapture:RetentionStartDelaySeconds` configures a delay from 0 through 3,600 seconds.

## PDF parser decision and policy

Approved candidate: `UglyToad.PdfPig` 0.1.16, exact stable pin, Apache-2.0. The official repository and release history show active maintenance, including stack-depth and malformed-input fixes. No prerelease or floating version is permitted.

The parser is used for structure only: catalog, active page tree, and encryption metadata. It must never decode page content streams, fonts, or images. Reject `/Encrypt`; require a terminal `%%EOF`; accept only 1–10 pages after open. Bounded Flate and LZW filters support TIFF predictor 2 and PNG predictors 10–15; PNG row filter bytes are validated and honored, and predictor output shares the decoded-byte limits. Invalid predictor parameters fail as unsupported input. Passive `/OpenAction` destinations and GoTo-only action chains are allowed. Static AcroForms and widget annotations are allowed only without XFA, catalog `/NeedsRendering true`, field `/AA` entries `/K`, `/F`, `/V`, or `/C`, or unsupported `/A`/`/AA` actions. Action `/Next` chains are inspected iteratively with cycle, depth, and document-wide action-count bounds. Enforce decoded-byte, nesting-depth, and cooperative deadline limits in custom filter hooks. The lack of per-object hooks is recorded as a bounded-behavior limitation: the 8 MiB input and 32 MiB aggregate decode caps bound structure data, and adversarial tests establish termination within fixed time and memory budgets. Cycles, malformed structures, unsupported filters, and structural or byte limit violations fail closed as malformed or unsupported input. A cooperative deadline expiry asks the client to retry.

The active-content policy rejects catalog `/AA`, `/Collection`, `/Perms`, or `/AF`; `/Names` is rejected only when it contains `/JavaScript` or `/EmbeddedFiles`, so ordinary named destinations remain supported. Active page-tree inspection is bounded to 64 levels, 5,000 nodes, and 500 children per node. On each of the at most ten active pages, reject more than 500 annotations and annotation subtypes `/Screen`, `/RichMedia`, `/FileAttachment`, `/Movie`, `/Sound`, or `/3D`. Action inspection allows only `/GoTo` and `/URI`, follows every `/Next` dictionary or array iteratively, and caps depth at 64 and the document at 256 actions. Static form fields and `/Widget` annotations use those same action checks; form trees are capped at depth 64, 5,000 nodes, and 500 children per node. Active-content inspection uses PdfPig's resolved object dictionaries and fails closed on malformed or unresolved references. This is a conservative content policy, not malware certification; PDFs remain authenticated attachments served with `private, no-store` and `nosniff`.

Required adversarial fixtures cover Flate bombs in object and xref streams (including a predictor), xref/object streams using PNG/TIFF predictors, invalid predictor parameters, cyclic/deep page and form trees, dishonest `/Count`, huge `/Kids`, thousands of tiny objects, incremental page-tree replacement, unrelated `/Type /Page` text in a stream, encrypted files, active action chains, static forms and XFA, 11 pages, truncation, and bounded peak time/memory on an 8 MiB worst-case input. Run hard-timeout/memory tests in a subprocess where needed so a hang fails only the test process. If any case cannot be bounded in-process, stop and ask the architecture owner; the fallback is an out-of-process validator with OS-level limits, not a hand-written second parser.

Evidence: the repository describes PdfPig as Apache-2.0 licensed and publishes release history at `https://github.com/UglyToad/PdfPig`; NuGet metadata for 0.1.16 declares Apache-2.0 and no dependencies for .NET 8/9 target groups. The 0.1.16 source exposes `IFilterProvider`/`IFilter.Decode` and `ParsingOptions.MaxStackDepth`. These prove the extension points, not our application-level ceilings; implementation tests must establish those ceilings. There is no per-object hook, so we rely on bounded input/decode data and adversarial time/memory termination tests, and will not claim a separate object-count cap.

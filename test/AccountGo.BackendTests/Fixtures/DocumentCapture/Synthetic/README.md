# Synthetic Document Capture Evaluation Fixtures

This folder contains 18 synthetic, PII-free receipts and invoices (`s01`–`s18`) used to verify the deterministic document capture field parser and self-hosted OCR pipeline in CI without requiring headless Chrome at test time.

## Contents

- `s01.pdf` – `s18.pdf`: Clean vector PDFs with selectable text layers.
- `s01_<variant>.png` – `s18_<variant>.png`: Five rendered image variants per document (`clean`, `skew`, `blur`, `dark`, `lowres`) — 90 PNGs total.
- `truth.json`: Expected `vendor`, `invoiceNo`, `date`, `subtotal`, `tax`, `total`, `currency`, `lang`, and `layout` for each document.
- `gen_synthetic.py`: Deterministic generator script (Python 3 stdlib + headless Chrome; uses SHA-256 for invoice numbers rather than Python's randomised `hash()`).

All documents, names, and numbers are synthetic. No real customer documents or bank numbers are included.

## Regenerating

Requires Python 3.10+ and Google Chrome (or set `CHROME_BIN` to your Chromium binary):

```bash
python3 test/AccountGo.BackendTests/Fixtures/DocumentCapture/Synthetic/gen_synthetic.py
```

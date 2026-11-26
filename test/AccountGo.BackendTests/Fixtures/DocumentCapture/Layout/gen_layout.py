#!/usr/bin/env python3
"""Generate invented reading-order fixtures (PNG screenshots + text-layer PDFs).

Requires Python 3.10+ (stdlib only) and headless Google Chrome / Chromium.
Usage:
    python3 gen_layout.py [output_dir]

Every name, number and address below is invented.
"""
import os
import subprocess
import sys
import tempfile

OUT = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.abspath(__file__)))
CHROME = os.environ.get("CHROME_BIN", "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome")

BASE_CSS = """
@page { size: 1280px 960px; margin: 0; }
* { box-sizing: border-box; }
html, body { margin: 0; padding: 0; background: #fff; color: #111; font-family: Arial, Helvetica, sans-serif; }
.line { display: block; white-space: pre; }
"""

# A documentation website: top bar, left navigation, main content with prose and code.
API_GUIDE = """
<style>
.top { position: absolute; left: 0; top: 0; width: 1280px; height: 64px; border-bottom: 1px solid #ccc; }
.brand { position: absolute; left: 32px; top: 20px; font-size: 22px; font-weight: bold; }
.toplinks { position: absolute; left: 980px; top: 22px; font-size: 18px; }
.nav { position: absolute; left: 0; top: 64px; width: 260px; height: 896px; background: #f3f4f6; padding: 28px 32px; font-size: 18px; }
.nav .line { margin-bottom: 26px; }
.nav .title { font-weight: bold; font-size: 20px; }
.main { position: absolute; left: 340px; top: 92px; width: 880px; font-size: 18px; line-height: 30px; }
h1 { font-size: 34px; margin: 0 0 22px 0; line-height: 40px; }
h2 { font-size: 24px; margin: 26px 0 10px 0; line-height: 30px; }
pre { background: #f6f8fa; font-family: Menlo, Consolas, monospace; font-size: 17px; line-height: 27px; padding: 12px 18px; margin: 0; }
</style>
<div class="top"><span class="brand">Example Docs</span><span class="toplinks">Guides &nbsp;&nbsp;&nbsp;&nbsp; Reference</span></div>
<div class="nav">
  <span class="line title">Navigation</span>
  <span class="line">Overview</span>
  <span class="line">Authentication</span>
  <span class="line">Settings</span>
  <span class="line">Webhooks</span>
  <span class="line">Errors</span>
</div>
<div class="main">
  <h1>API Guide</h1>
  <span class="line">Send a request to create a note.</span>
  <span class="line">Every request needs an API key in the header.</span>
  <h2>Request body</h2>
<pre>{
  "note": "string",
  "items": [1, 2]
}</pre>
  <h2>Response schema</h2>
<pre>total: integer
tax_rate: number
invoice_id: string</pre>
  <span class="line">Errors return a JSON object with a message.</span>
</div>
"""

# An invoice: seller block left, invoice details right, wide item table, totals with a diagonal watermark.
INVOICE = """
<style>
.title { position: absolute; left: 64px; top: 48px; font-size: 40px; font-weight: bold; }
.seller { position: absolute; left: 64px; top: 130px; font-size: 18px; line-height: 30px; }
.meta { position: absolute; left: 760px; top: 130px; font-size: 18px; line-height: 30px; }
.billto { position: absolute; left: 64px; top: 300px; font-size: 18px; line-height: 30px; }
table { position: absolute; left: 64px; top: 450px; width: 1150px; border-collapse: collapse; font-size: 18px; }
th, td { padding: 8px 6px; text-align: left; }
th { border-bottom: 2px solid #333; }
td.n, th.n { text-align: right; }
.totals { position: absolute; left: 820px; top: 680px; width: 394px; font-size: 18px; line-height: 34px; }
.totals .row { display: flex; justify-content: space-between; }
.stamp { position: absolute; left: 850px; top: 742px; font-size: 64px; font-weight: bold; color: rgba(200, 0, 0, 0.35);
         transform: rotate(-30deg); transform-origin: left top; }
</style>
<div class="title">INVOICE</div>
<div class="seller">
  <span class="line">Lumen Paper Works</span>
  <span class="line">Unit 7, 18 Quarry Lane</span>
  <span class="line">Riverton 40210</span>
</div>
<div class="meta">
  <span class="line">Invoice Number LPW-2048</span>
  <span class="line">Invoice Date March 4, 2026</span>
  <span class="line">Due Date April 3, 2026</span>
</div>
<div class="billto">
  <span class="line">Bill To:</span>
  <span class="line">Orchard Lane Studio</span>
  <span class="line">42 Hillcrest Road</span>
</div>
<table>
  <tr><th>Description</th><th class="n">Qty</th><th class="n">Unit Price</th><th class="n">Amount</th></tr>
  <tr><td>Letterpress cards</td><td class="n">3</td><td class="n">$20.00</td><td class="n">$60.00</td></tr>
  <tr><td>Envelope pack</td><td class="n">1</td><td class="n">$25.00</td><td class="n">$25.00</td></tr>
</table>
<div class="totals">
  <div class="row"><span>Sub Total</span><span>$85.00</span></div>
  <div class="row"><span>Tax</span><span>$8.50</span></div>
  <div class="row"><span>Total</span><span>$93.50</span></div>
</div>
<div class="stamp">PAID</div>
"""

# A bank statement: account identifier, dated transactions with an invented counterparty, and a total line.
STATEMENT = """
<style>
.box { position: absolute; left: 64px; top: 56px; width: 1150px; font-size: 18px; line-height: 34px; }
h1 { font-size: 36px; margin: 0 0 18px 0; }
.row { display: flex; justify-content: space-between; width: 700px; }
</style>
<div class="box">
  <h1>Statement</h1>
  <span class="line">Riverbank Savings</span>
  <span class="line">Account Number: 40213377</span>
  <span class="line">Date: March 31, 2026</span>
  <br>
  <div class="row"><span>2026-03-02 Transfer to Casey Sample</span><span>-$40.00</span></div>
  <div class="row"><span>2026-03-09 Card payment Harbor Cafe</span><span>-$80.00</span></div>
  <br>
  <div class="row"><span>Total: $120.00</span><span></span></div>
</div>
"""

# Two text flows whose lines happen to align exactly: small navigation items beside larger section headings.
ALIGNED_FLOWS = """
<style>
.nav { position: absolute; left: 40px; top: 80px; font-size: 16px; }
.main { position: absolute; left: 420px; top: 66px; font-size: 30px; font-weight: bold; }
.nav .line, .main .line { height: 64px; line-height: 64px; }
</style>
<div class="nav"><span class="line">Navigation</span><span class="line">Overview</span><span class="line">Settings</span></div>
<div class="main"><span class="line">API Guide</span><span class="line">Request body</span><span class="line">Errors</span></div>
"""

# The same shape in one type size: indistinguishable from a two-column table, so rows keep their cells apart.
ALIGNED_GRID = """
<style>
.col { position: absolute; top: 80px; font-size: 18px; }
.col .line { height: 40px; line-height: 40px; }
</style>
<div class="col" style="left: 40px"><span class="line">Navigation</span><span class="line">Overview</span><span class="line">Settings</span></div>
<div class="col" style="left: 420px"><span class="line">API Guide</span><span class="line">Request body</span><span class="line">Errors</span></div>
"""

# Two prose columns in one type size whose lines align exactly (a newsletter-style layout).
ALIGNED_PROSE = """
<style>
.col { position: absolute; top: 80px; width: 520px; font-size: 18px; }
.col .line { height: 40px; line-height: 40px; }
</style>
<div class="col" style="left: 40px">
  <span class="line">Requests are sent over HTTPS only.</span>
  <span class="line">Each request carries an API key header.</span>
  <span class="line">Keys can be rotated from the settings page.</span>
</div>
<div class="col" style="left: 660px">
  <span class="line">Responses use JSON with UTF-8 text.</span>
  <span class="line">Errors include a code and a message.</span>
  <span class="line">Retry failed calls after a short delay.</span>
</div>
"""

# An API parameter table: short name and type cells beside a description, all in one type size.
PARAMETER_TABLE = """
<style>
table { position: absolute; left: 40px; top: 80px; border-collapse: collapse; font-size: 18px; }
td, th { text-align: left; padding: 8px 60px 8px 0; }
</style>
<table>
  <tr><th>Name</th><th>Type</th><th>Description</th></tr>
  <tr><td>note</td><td>string</td><td>Text of the note.</td></tr>
  <tr><td>items</td><td>array</td><td>Item ids to attach.</td></tr>
  <tr><td>pinned</td><td>boolean</td><td>Keep it at the top.</td></tr>
</table>
"""

FIXTURES = {"api-guide": API_GUIDE, "two-column-invoice": INVOICE, "bank-statement": STATEMENT,
            "aligned-flows": ALIGNED_FLOWS, "aligned-grid": ALIGNED_GRID, "aligned-prose": ALIGNED_PROSE,
            "parameter-table": PARAMETER_TABLE}


def render(name: str, body: str) -> None:
    html = f"<!doctype html><html><head><meta charset='utf-8'><style>{BASE_CSS}</style></head><body>{body}</body></html>"
    with tempfile.TemporaryDirectory() as tmp:
        page = os.path.join(tmp, name + ".html")
        with open(page, "w", encoding="utf-8") as handle:
            handle.write(html)
        url = "file://" + page
        common = [CHROME, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--force-device-scale-factor=1",
                  f"--user-data-dir={os.path.join(tmp, 'profile')}"]
        subprocess.run(common + ["--window-size=1280,960", f"--screenshot={os.path.join(OUT, name + '.png')}", url],
                       check=True, capture_output=True)
        subprocess.run(common + ["--no-pdf-header-footer", f"--print-to-pdf={os.path.join(OUT, name + '.pdf')}", url],
                       check=True, capture_output=True)


def main() -> None:
    os.makedirs(OUT, exist_ok=True)
    for name, body in FIXTURES.items():
        render(name, body)
        print("wrote", name)


if __name__ == "__main__":
    main()

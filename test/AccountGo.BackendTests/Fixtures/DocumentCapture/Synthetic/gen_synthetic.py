#!/usr/bin/env python3
"""Generate deterministic synthetic receipts/invoices (PNG variants + PDFs + truth.json).

Requires Python 3.10+ (stdlib only) and headless Google Chrome / Chromium.
Usage:
    python3 gen_synthetic.py [output_dir]
"""
import hashlib
import json
import os
import subprocess
import sys
import tempfile

OUT = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.abspath(__file__)))
CHROME = os.environ.get("CHROME_BIN", "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome")

# (id, vendor, date_text, iso_date, currency, lines[(desc, qty, unit)], tax_rate, tax_inclusive, lang, layout)
DOCS = [
    ("s01", "Blue Bean Coffee Supply", "2026-11-20", "2026-11-20", "USD", [("Espresso beans", 2, 25.00), ("Paper cups", 1, 50.00)], 0.10, False, "en", "invoice"),
    ("s02", "Corner Cafe", "21/11/2026", "2026-11-21", "USD", [("Team lunch", 1, 42.50)], 0.0, True, "en", "receipt"),
    ("s03", "Northwind Office Ltd", "Nov 3, 2026", "2026-11-03", "USD", [("Printer toner", 3, 89.99), ("A4 paper box", 5, 24.50), ("Delivery", 1, 15.00)], 0.0825, False, "en", "invoice"),
    ("s04", "Harbor Hardware", "03.10.2026", "2026-10-03", "EUR", [("Drill bits set", 1, 34.90), ("Screws 200pc", 2, 7.45)], 0.19, True, "en", "receipt"),
    ("s05", "Pacific Rim Trading Pte Ltd", "15 Oct 2026", "2026-10-15", "SGD", [("Consulting hours", 12, 180.00)], 0.09, False, "en", "invoice"),
    ("s06", "Green Leaf Grocers", "2026/09/30", "2026-09-30", "USD", [("Organic apples", 3, 4.99), ("Milk 2L", 2, 3.49), ("Bread", 1, 5.25), ("Eggs dozen", 1, 6.10)], 0.0, True, "en", "receipt"),
    ("s07", "Metro Cloud Services Inc.", "October 1, 2026", "2026-10-01", "USD", [("Hosting plan - monthly", 1, 249.00), ("Extra storage 500GB", 2, 20.00)], 0.0, False, "en", "invoice"),
    ("s08", "Sunrise Bakery", "12/11/2026", "2026-11-12", "USD", [("Croissant", 6, 2.75), ("Coffee", 4, 3.50)], 0.07, True, "en", "receipt"),
    ("s09", "ร้านกาแฟบ้านสวน", "20/11/2569", "2026-11-20", "THB", [("กาแฟเย็น", 2, 65.00), ("ขนมปังปิ้ง", 1, 45.00)], 0.07, True, "th", "receipt"),
    ("s10", "บริษัท สยามออฟฟิศ จำกัด", "5 พฤศจิกายน 2569", "2026-11-05", "THB", [("กระดาษ A4", 10, 120.00), ("หมึกพิมพ์", 2, 890.00)], 0.07, False, "th", "invoice"),
    ("s11", "ร้านอาหารครัวไทย", "01/10/2569", "2026-10-01", "THB", [("ผัดไทย", 3, 80.00), ("ต้มยำกุ้ง", 1, 180.00), ("น้ำเปล่า", 4, 15.00)], 0.07, True, "th", "receipt"),
    ("s12", "Bangkok Tech Supplies Co., Ltd.", "2026-11-18", "2026-11-18", "THB", [("USB-C cable", 5, 199.00), ("Wireless mouse", 2, 590.00)], 0.07, False, "mixed", "invoice"),
    ("s13", "Fuel Stop #214", "11/19/2026", "2026-11-19", "USD", [("Unleaded 12.4 gal", 1, 46.38)], 0.0, True, "en", "receipt"),
    ("s14", "Lakeside Legal LLP", "19 November 2026", "2026-11-19", "USD", [("Contract review", 4.5, 300.00), ("Filing fee", 1, 125.00)], 0.0, False, "en", "invoice"),
    ("s15", "Kopi & Co", "2026-11-02", "2026-11-02", "SGD", [("Kopi C", 2, 1.80), ("Kaya toast", 1, 2.60)], 0.09, True, "en", "receipt"),
    ("s16", "Atlas Freight GmbH", "02.11.2026", "2026-11-02", "EUR", [("Freight Hamburg-Bangkok", 1, 1840.00), ("Customs handling", 1, 210.00), ("Insurance", 1, 95.50)], 0.0, False, "en", "invoice"),
    ("s17", "Pixel Print Studio", "Nov 21 2026", "2026-11-21", "USD", [("Business cards 500", 2, 39.00), ("Poster A2", 3, 18.50)], 0.06, False, "en", "invoice"),
    ("s18", "เซเว่น สาขาสุขุมวิท", "22/11/2569", "2026-11-22", "THB", [("น้ำดื่ม", 2, 10.00), ("แซนวิช", 1, 39.00), ("กาแฟกระป๋อง", 1, 25.00)], 0.07, True, "th", "receipt"),
]

VARIANTS = ("clean", "skew", "blur", "dark", "lowres")


def deterministic_doc_number(did: str, vendor: str) -> str:
    digest = hashlib.sha256(f"{did}:{vendor}".encode("utf-8")).digest()
    suffix = int.from_bytes(digest[:4], "big") % 9000 + 1000
    return f"{did.upper()}-{suffix}"


def money(v: float, cur: str) -> str:
    sym = {"USD": "$", "EUR": "€", "SGD": "S$", "THB": "฿"}[cur]
    return f"{sym}{v:,.2f}"


def variant_styles(variant: str, index: int) -> tuple[str, str, float]:
    """Returns (body_style, card_extra_style, scale_factor)."""
    if variant == "clean":
        return "margin:0;background:#fff;", "", 1.0
    if variant == "skew":
        angle = 2.2 if index % 2 == 0 else -2.2
        return (
            "margin:0;background:#f4f4f4;padding:18px;",
            f"background:#fff;transform:rotate({angle}deg);transform-origin:center center;",
            1.0,
        )
    if variant == "blur":
        return "margin:0;background:#fff;", "filter:blur(0.7px);", 1.0
    if variant == "dark":
        return (
            "margin:0;background:#b8b8b8;",
            "background:#d6d6d6;filter:brightness(0.72) contrast(0.92);",
            1.0,
        )
    if variant == "lowres":
        return "margin:0;background:#fff;", "", 0.65
    raise ValueError(f"Unknown variant: {variant}")


def build(doc, variant: str, index: int):
    did, vendor, date_text, iso, cur, lines, rate, inclusive, lang, layout = doc
    sub = round(sum(q * u for _, q, u in lines), 2)
    if inclusive:
        total = sub
        tax = round(total - total / (1 + rate), 2) if rate else 0.0
        net = round(total - tax, 2)
    else:
        net = sub
        tax = round(net * rate, 2)
        total = round(net + tax, 2)

    th = lang == "th"
    labels = {
        "inv": "ใบกำกับภาษี / ใบเสร็จรับเงิน" if th else ("TAX INVOICE" if layout == "invoice" else "RECEIPT"),
        "date": "วันที่" if th else "Date",
        "no": "เลขที่" if th else ("Invoice No." if layout == "invoice" else "Receipt #"),
        "sub": "รวมเป็นเงิน" if th else "Subtotal",
        "tax": "ภาษีมูลค่าเพิ่ม 7%" if th else (f"Tax {rate*100:g}%" if rate else "Tax"),
        "total": "ยอดรวมทั้งสิ้น" if th else "TOTAL",
        "incl": "(รวมภาษีแล้ว)" if th else "(tax included)",
    }
    doc_no = deterministic_doc_number(did, vendor)
    rows = "".join(
        f"<tr><td>{d}</td><td style='text-align:right'>{q:g}</td><td style='text-align:right'>{money(u, cur)}</td><td style='text-align:right'>{money(q * u, cur)}</td></tr>"
        for d, q, u in lines
    )
    width = 760 if layout == "invoice" else 380
    font = (
        "'Thonburi','Sukhumvit Set',sans-serif"
        if lang in ("th", "mixed")
        else ("'Helvetica',sans-serif" if layout == "invoice" else "'Courier New',monospace")
    )
    body_style, card_extra, scale = variant_styles(variant, index)
    html = f"""<html><head><meta charset='utf-8'></head><body style="{body_style}">
<div style="width:{width}px;padding:20px;font-family:{font};font-size:15px;color:#111;{card_extra}">
<div style="font-size:22px;font-weight:bold">{vendor}</div>
<div style="color:#444">123 Example Road, Unit 4</div>
<h3 style="margin:12px 0 6px">{labels['inv']}</h3>
<div>{labels['no']}: {doc_no}</div>
<div>{labels['date']}: {date_text}</div>
<table style="width:100%;margin-top:10px;border-collapse:collapse">{rows}</table>
<hr/>
<div style="text-align:right">{labels['sub']}: {money(net if not inclusive else sub, cur)}</div>
<div style="text-align:right">{labels['tax']}: {money(tax, cur)} {labels['incl'] if inclusive and rate else ''}</div>
<div style="text-align:right;font-size:19px;font-weight:bold">{labels['total']}: {money(total, cur)}</div>
</div></body></html>"""
    truth_entry = {
        "id": did,
        "vendor": vendor,
        "invoiceNo": doc_no,
        "date": iso,
        "subtotal": f"{(net if not inclusive else sub):.2f}",
        "tax": f"{tax:.2f}",
        "total": f"{total:.2f}",
        "currency": cur,
        "lang": lang,
        "layout": layout,
    }
    return html, truth_entry, scale


def main():
    os.makedirs(OUT, exist_ok=True)
    truth = []
    with tempfile.TemporaryDirectory(prefix="accountgo-synth-") as tmpdir:
        for idx, doc in enumerate(DOCS):
            did = doc[0]
            layout = doc[-1]
            base_w = 808 if layout == "invoice" else 428
            base_h = 340 + 24 * len(doc[5])

            for variant in VARIANTS:
                html, truth_entry, scale = build(doc, variant, idx)
                hp = os.path.join(tmpdir, f"{did}_{variant}.html")
                with open(hp, "w", encoding="utf-8") as fh:
                    fh.write(html)

                w = base_w + (40 if variant == "skew" else 0)
                h = base_h + (44 if variant == "skew" else 0)
                png_path = os.path.join(OUT, f"{did}_{variant}.png")
                cmd = [
                    CHROME,
                    "--headless=new",
                    "--disable-gpu",
                    "--hide-scrollbars",
                    f"--force-device-scale-factor={scale}",
                    f"--window-size={w},{h}",
                    f"--screenshot={png_path}",
                    "file://" + hp,
                ]
                subprocess.run(cmd, capture_output=True)
                if not os.path.isfile(png_path) or os.path.getsize(png_path) == 0:
                    raise RuntimeError(f"Chrome failed to write {png_path}")

                if variant == "clean":
                    pdf_path = os.path.join(OUT, f"{did}.pdf")
                    subprocess.run(
                        [
                            CHROME,
                            "--headless=new",
                            "--disable-gpu",
                            "--no-pdf-header-footer",
                            f"--print-to-pdf={pdf_path}",
                            "file://" + hp,
                        ],
                        capture_output=True,
                    )
                    if not os.path.isfile(pdf_path) or os.path.getsize(pdf_path) == 0:
                        raise RuntimeError(f"Chrome failed to write {pdf_path}")
                    truth.append(truth_entry)

    truth_path = os.path.join(OUT, "truth.json")
    with open(truth_path, "w", encoding="utf-8") as fh:
        json.dump(truth, fh, ensure_ascii=False, indent=1)
        fh.write("\n")

    print(f"Generated {len(truth)} PDFs, {len(truth) * len(VARIANTS)} PNG variants, and truth.json in {OUT}")


if __name__ == "__main__":
    main()

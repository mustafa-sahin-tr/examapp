#!/usr/bin/env python3
"""Turns saved pages of the private school list into a CSV for seed-schools --source-file.

Usage: private-schools-csv.py <html-dir> <output.csv>
Pages are named <IL>-<page>.html. Preschools and special education stay out, as with public schools.
"""
import csv, html, pathlib, re, sys

COLUMNS = ["il", "ilce", "okul_adi", "kurum_kodu", "okul_turu", "web_sitesi", "adres", "telefon", "harita", "cekim_tarihi"]
EXCLUDED_TYPES = ("Okul Öncesi", "Özel Eğitim")
# The site spells these districts differently from our reference data.
DISTRICT_NAMES = {"19 MAYIS": "ONDOKUZMAYIS"}


def clean(cell):
    return re.sub(r"\s+", " ", html.unescape(re.sub(r"<[^>]+>", "", cell))).strip()


def seed_type(name, school_type):
    """Maps a source type to what seed-schools understands; returns None to leave the row out."""
    if any(word in school_type for word in EXCLUDED_TYPES) or "ANAOKULU" in name:
        return None
    if "İlkokul" in school_type or "İLKOKUL" in name:
        return "İlkokul"
    if "Ortaokul" in school_type or "ORTAOKUL" in name:
        return "Ortaokul"
    return "Lise"


def rows_of(page):
    province = page.name.rsplit("-", 1)[0]
    text = page.read_bytes().decode("utf-8", "replace")
    for row in re.findall(r"<tr>(.*?)</tr>", text[text.find("<tbody"):], re.S):
        cells = [clean(c) for c in re.findall(r"<td[^>]*>(.*?)</td>", row, re.S)]
        if len(cells) >= 4:
            yield province, cells[1], cells[2], cells[3]


def main():
    source, output = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2])
    seen, skipped = {}, 0
    for page in sorted(source.glob("*.html")):
        for province, district, name, school_type in rows_of(page):
            name = re.sub(r"\(\s*(.*?)\s*\)", r"(\1)", name)
            district = DISTRICT_NAMES.get(district, district)
            kind = seed_type(name, school_type)
            if kind is None:
                skipped += 1
            else:
                seen[(province, district, name)] = kind

    with output.open("w", encoding="utf-8", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(COLUMNS)
        for (province, district, name), kind in sorted(seen.items()):
            writer.writerow([province, district, name, "", kind, "", "", "", "", ""])
    print(f"{len(seen)} schools written, {skipped} rows left out: {output}")


if __name__ == "__main__":
    main()

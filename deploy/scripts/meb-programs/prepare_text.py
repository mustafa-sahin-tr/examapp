#!/usr/bin/env python3
"""
Program PDF'lerini metne çevirir: pdftotext -layout sütunları korur. Metin katmanı Türkçe harf kaybeden PDF'lerde
("Kend ne İnanma") PyMuPDF doğru okur, bu programlar onunla çevrilir.

Kullanım: prepare_text.py <pdf-dizini> <text-dizini>
"""
import os
import subprocess
import sys

import pymupdf

PYMUPDF_PROGRAMS = {"1967", "1968"}


def main(pdf_dir, text_dir):
    os.makedirs(text_dir, exist_ok=True)
    for name in sorted(os.listdir(pdf_dir)):
        pid, extension = os.path.splitext(name)
        if extension.lower() != ".pdf":
            continue
        pdf = os.path.join(pdf_dir, name)
        target = os.path.join(text_dir, f"{pid}.txt")
        if pid in PYMUPDF_PROGRAMS:
            with pymupdf.open(pdf) as document, open(target, "w", encoding="utf-8") as out:
                for page in document:
                    out.write(page.get_text(sort=True) + "\f")
        elif not os.path.exists(target):
            subprocess.run(["pdftotext", "-layout", "-enc", "UTF-8", pdf, target], check=True, timeout=300)


if __name__ == "__main__":
    main(*sys.argv[1:3])

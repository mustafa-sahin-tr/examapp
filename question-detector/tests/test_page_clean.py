"""#1234: sayfa temizliği — sentetik sayfalarda filigran şablonu, cevap işareti ve jeton."""
import io
import os
import sys

import numpy as np
import pytest
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
page_clean = pytest.importorskip("page_clean")
if not page_clean.available():
    pytest.skip("scipy is not installed", allow_module_level=True)

W, H = 1400, 1900
WATERMARK = (205, 205, 205)


def page(seed: int, mark: bool = False) -> np.ndarray:
    """Her sayfada farklı yazı, aynı yerde açık gri filigran; isteğe bağlı pembe kutulu "B)" işareti."""
    rng = np.random.default_rng(seed)
    im = Image.new("RGB", (W, H), "white")
    d = ImageDraw.Draw(im)
    for _ in range(40):
        x, y = int(rng.integers(20, W - 120)), int(rng.integers(20, H - 40))
        d.rectangle([x, y, x + int(rng.integers(30, 100)), y + 6], fill=(30, 30, 30))
    arr = np.asarray(im).astype(np.float32)
    f = np.ones_like(arr)
    f[300:500, 150:450] = np.array(WATERMARK) / 255.0
    arr = arr * f
    out = Image.fromarray(arr.round().astype(np.uint8))
    if mark:
        d = ImageDraw.Draw(out)
        d.rectangle([60, 650, 100, 680], fill=(182, 23, 113))
        d.text((66, 652), "B)", fill=(30, 30, 30), font=page_clean._load_font().font_variant(size=22))
    return np.asarray(out)


def webp(arr: np.ndarray) -> bytes:
    buf = io.BytesIO()
    Image.fromarray(arr).save(buf, "WEBP", lossless=True)
    return buf.getvalue()


def test_watermark_is_learned_and_divided_out():
    pages = [webp(page(i)) for i in range(10)]
    result = page_clean.analyze(pages)
    assert result["watermarks"] and result["watermarks"][0]["size"] == f"{W}x{H}"

    analysis = page_clean.get_analysis(result["token"])
    image, _, removed = page_clean.clean_page(pages[0], analysis)
    assert removed
    clean = np.asarray(Image.open(io.BytesIO(image)).convert("RGB")).astype(int)
    # Filigranın içindeki boş beyaz alan beyaza döner.
    blank = clean[300:500, 150:450][np.asarray(Image.open(io.BytesIO(pages[0])).convert("RGB"))[300:500, 150:450].min(axis=2) > 150]
    assert blank.mean() > 245


def test_too_few_pages_find_nothing_and_unknown_token_expires():
    result = page_clean.analyze([webp(page(i)) for i in range(3)])
    assert result["watermarks"] == []
    assert page_clean.get_analysis("missing") is None


def test_mark_colour_is_not_searched_without_a_pick():
    """2026-11-23: renk kendiliğinden aranmaz (çok renkli kitapta analiz 10 dakikayı aşıyordu); yalnız filigran."""
    pages = [webp(page(i, mark=True)) for i in range(10)]
    result = page_clean.analyze(pages)
    assert result["markColour"] is None and result["marksSeen"] == 0
    assert result["watermarks"]


def test_picked_mark_colour_is_used_and_marks_are_returned():
    pages = [webp(page(i, mark=True)) for i in range(10)]
    result = page_clean.analyze(pages, mark_colour=[182, 23, 113])
    assert result["markColour"] == [182, 23, 113]
    image, marks, _ = page_clean.clean_page(pages[0], page_clean.get_analysis(result["token"]))
    clean = np.asarray(Image.open(io.BytesIO(image)).convert("RGB")).astype(int)
    box = clean[650:681, 60:101]
    # Pembe kutu silindi.
    assert ((box.max(axis=2) - box.min(axis=2)) > 90).sum() < 20
    assert [m["letter"] for m in marks] == ["B"]


def test_wide_letters_in_high_resolution_boxes_are_found_and_b_is_not_read_as_e():
    """
    2026-11-20 demo: 2363 px genişlikte sayfada "A)" / "B)" kutuları yarı boş sayılıp atlanıyor, "B)" ile "E)" karışıyordu
    (gerçek kitapta B -> E; şablonla aynı yazı tipindeki bu sentetik sayfada eski kod E -> B okur).
    """
    colour = (234, 9, 125)
    scale = 2363 / 1400
    im = Image.new("RGB", (2363, 1200), "white")
    d = ImageDraw.Draw(im)
    bold = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
    if not os.path.exists(bold):
        pytest.skip("DejaVu Sans Bold is not installed")
    font = ImageFont.truetype(bold, 34)
    for i, letter in enumerate("ABCDEB"):
        x, y = 240, 100 + i * 150
        # Koyu kenarlı kutu, harf kutuyu dolduruyor, parantez sağ kenara taşıyor.
        d.rectangle([x, y, x + 37, y + 39], fill=colour, outline=(190, 5, 100), width=2)
        d.text((x + 3, y + 3), letter + ")", fill=(60, 5, 40), font=font, stroke_width=1, stroke_fill=(60, 5, 40))
    arr = np.asarray(im).copy()

    boxes, _ = page_clean.colour_boxes(arr, list(colour))
    cleaned, marks = page_clean.remove_marks(arr, list(colour))

    assert scale > 1.5 and len(boxes) == 6
    assert [m["letter"] for m in sorted(marks, key=lambda m: m["y"])] == list("ABCDEB")
    assert (np.abs(cleaned.astype(int) - np.array(colour)).max(axis=2) < 70).sum() == 0

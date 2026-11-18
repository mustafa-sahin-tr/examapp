"""
#1234: kitap aktarımında sayfa temizliği — sabit filigran ve renkli cevap işaretleri (kutu içinde şık harfi).

Yerel `clean_pages.py` betiğinin servis hali. exam API önce kitabın örnek sayfalarıyla `analyze` çağırır (filigran şablonu ve
işaret rengi öğrenilir, bellekte bir jetonla tutulur), sonra sayfaları tek tek `clean_page` ile temizletir. Hesap sabittir:
aynı sayfalar her seferinde aynı sonucu verir; jeton kaybolursa (servis yeniden başladı) analiz yeniden yapılır.

- Filigran: her sayfada aynı yerdedir; sayfaların piksel piksel medyanı içeriği düşürür, filigranı bırakır. Filigran çarpma
  biçimli bir örtüdür (beyazda açık ton, yazıda koyu), her piksel geri bölünür. Sayfa kenarına değmeyen büyük, açık, düz lekeler
  filigran sayılır; başlık ve kenar şeritleri koyu ya da kenara değer.
- Cevap işaretleri: içinde şık harfi (A–E; rakam değil) okunan küçük doygun renkli kutular. Renk kitaptan öğrenilir; kutu
  silinir, harf sayfanın yazı renginde yeniden yazılır. İşaretlerin yeri ve harfi döner (#1235 cevap anahtarı).
"""
import collections
import io
import threading
import time
import uuid

import numpy as np
from PIL import Image, ImageDraw, ImageFont

try:
    from scipy import ndimage
except ImportError:  # paket yok: özellik kapalı
    ndimage = None

LABELS = "ABCDE"
DIGITS = "0123456789"
# Medyan için en çok bu kadar sayfa (tam çözünürlükte sayfa başına ~23 MB; bellek sınırı).
MAX_TEMPLATE_PAGES = 32
MIN_TEMPLATE_PAGES = 8
MARK_SAMPLE_PAGES = 30
FONT_PATHS = (
    "/System/Library/Fonts/HelveticaNeue.ttc",
    "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
)
# Kutu boyut sınırları bu genişlikteki sayfaya göre; başka genişlikte orantılanır.
REFERENCE_WIDTH = 1400
MAX_ANALYSES = 4
ANALYSIS_TTL_SECONDS = 6 * 3600

_lock = threading.Lock()
_analyses: "collections.OrderedDict[str, tuple[float, dict]]" = collections.OrderedDict()


def available() -> bool:
    return ndimage is not None


def _rgb(data: bytes) -> np.ndarray:
    return np.asarray(Image.open(io.BytesIO(data)).convert("RGB"))


# ---------------------------------------------------------------- watermark

def _median(images: list) -> np.ndarray:
    """Piksel piksel medyan; bellek için satır şeritleriyle."""
    h = images[0].shape[0]
    med = np.empty(images[0].shape, np.float32)
    for y in range(0, h, 128):
        med[y:y + 128] = np.median(np.stack([im[y:y + 128] for im in images]), axis=0)
    return med


def learn_watermark(med: np.ndarray):
    """(geçirgenlik çarpanı HxWx3 ya da None, filigran rengi, leke maskesi)."""
    lum = med.mean(axis=2)
    light = (lum > 110) & (lum < 247)
    if light.sum() < 5000:
        return None, None, None
    # Filigran medyandaki en yaygın açık, beyaz olmayan renktir.
    q = (med[light] // 4).astype(int)
    key = q[:, 0] * 4096 + q[:, 1] * 64 + q[:, 2]
    peak = np.bincount(key).argmax()
    colour = np.array([peak // 4096, peak // 64 % 64, peak % 64]) * 4 + 2
    core = np.abs(med - colour).max(axis=2) <= 10
    lab, n = ndimage.label(core)
    if n == 0:
        return None, None, None
    sizes = ndimage.sum(core, lab, range(1, n + 1))
    edge = set(np.unique(np.r_[lab[0], lab[-1], lab[:, 0], lab[:, -1]]))
    big = [i + 1 for i, s in enumerate(sizes) if s > 3000 and i + 1 not in edge]
    if sum(sizes[i - 1] for i in big) < 0.01 * core.size:
        return None, None, None
    keep = np.isin(lab, big)
    region = ndimage.binary_dilation(keep, iterations=3)
    f = np.ones_like(med)
    f[region] = np.clip(med[region], colour, 255) / 255.0
    f[keep] = colour / 255.0
    return f, colour, keep


# ---------------------------------------------------------------- answer marks

def colour_boxes(img: np.ndarray, colour=None):
    """Aday işaret kutuları: doygun (ya da öğrenilen) renkte, kompakt lekeler."""
    if colour is None:
        mask = (img.max(axis=2).astype(np.int16) - img.min(axis=2)) > 90
    else:
        mask = np.abs(img.astype(np.float32) - np.array(colour)).max(axis=2) < 70
    lab, _ = ndimage.label(ndimage.binary_closing(mask, iterations=2))
    s = max(img.shape[1] / REFERENCE_WIDTH, 0.5)
    out = []
    for i, sl in enumerate(ndimage.find_objects(lab), 1):
        h, w = sl[0].stop - sl[0].start, sl[1].stop - sl[1].start
        if not (12 * s <= h <= 60 * s and 12 * s <= w <= 80 * s):
            continue
        area = (lab[sl] == i).sum()
        if area > 0.5 * h * w:
            out.append(sl)
    return out, mask


_glyph_cache: dict = {}
_font = None


def _load_font():
    global _font
    if _font is None:
        for path in FONT_PATHS:
            try:
                _font = ImageFont.truetype(path, 200)
                break
            except OSError:
                continue
        else:
            _font = ImageFont.load_default(200)
    return _font


def candidate_glyph(text: str, h: int) -> np.ndarray:
    """`text`in mürekkebi; ilk karakteri h piksel yüksekliğinde (önbellekli)."""
    key = (text, h)
    if key not in _glyph_cache:
        big = 200
        font = _load_font()

        def ink(t):
            c = Image.new("L", (big * 4, big * 2), 0)
            ImageDraw.Draw(c).text((big // 2, big // 2), t, fill=255, font=font)
            return c

        full, first = ink(text), ink(text[0])
        fb, lb = full.getbbox(), first.getbbox()
        s = h / (lb[3] - lb[1])
        full = full.crop(fb)
        size = (max(1, round(full.width * s)), max(1, round(full.height * s)))
        _glyph_cache[key] = np.asarray(full.resize(size, Image.LANCZOS)).astype(np.float32) / 255
    return _glyph_cache[key]


def read_box(img: np.ndarray, sl, mask, pad: int = 8):
    """Kutuda hangi etiket ("A)".."E)" ya da rakam) basılı ve nerede: (karakter, (y, x, h)) ya da None."""
    by0, by1, bx0, bx1 = sl[0].start, sl[0].stop, sl[1].start, sl[1].stop
    fill = np.median(img[sl][mask[sl]], axis=0)
    ch = int(fill.argmax())
    y0, x0 = max(by0 - pad, 0), max(bx0 - pad, 0)
    y1, x1 = min(by1 + pad, img.shape[0]), min(bx1 + pad * 2, img.shape[1])
    reg = img[y0:y1, x0:x1].astype(np.float32)
    inbox = ndimage.binary_dilation(mask[y0:y1, x0:x1], iterations=1)
    inbox = ndimage.binary_fill_holes(inbox)
    dark_in = np.clip((fill[ch] * 0.85 - reg[..., ch]) / max(fill[ch] * 0.5, 1), 0, 1)
    dark_out = np.clip((200 - reg.mean(axis=2)) / 150, 0, 1)
    dark = np.where(inbox, dark_in, dark_out)
    if dark.std() == 0:
        return None
    bh = by1 - by0
    best = None
    for text in [c + ")" for c in LABELS] + list(DIGITS):
        for h in range(max(6, int(bh * 0.4)), int(bh * 0.85) + 1):
            g = candidate_glyph(text, h)
            if g.shape[0] > dark.shape[0] or g.shape[1] > dark.shape[1]:
                continue
            win = np.lib.stride_tricks.sliding_window_view(dark, g.shape)
            gz = g - g.mean()
            wz = win - win.mean(axis=(2, 3), keepdims=True)
            num = (wz * gz).sum(axis=(2, 3))
            den = np.sqrt((wz ** 2).sum(axis=(2, 3)) * (gz ** 2).sum()) + 1e-6
            ncc = num / den
            # Etiket kutunun içinde, sol kısmında başlar.
            ncc[:, :max(bx0 - 2 - x0, 0)] = -1
            ncc[:, bx0 + (bx1 - bx0) * 2 // 5 - x0:] = -1
            iy, ix = np.unravel_index(ncc.argmax(), ncc.shape)
            # Yerleşimi bütün bölgede puanla: adayın açıklamadığı mürekkep (ör. "1" denenirken B'nin karınları) aleyhine sayılır.
            placed = np.zeros_like(dark)
            placed[iy:iy + g.shape[0], ix:ix + g.shape[1]] = g
            score = np.corrcoef(placed.ravel(), dark.ravel())[0, 1]
            if best is None or score > best[0]:
                best = (score, text[0], (y0 + iy, x0 + ix, h))
    if best is None or best[0] < 0.5:
        return None
    return best[1], best[2]


def text_colour(img: np.ndarray) -> float:
    lum = img.mean(axis=2)
    neutral = ((img.max(axis=2).astype(np.int16) - img.min(axis=2)) < 15) & (lum < 120)
    return float(np.percentile(lum[neutral], 10)) if neutral.any() else 40.0


def learn_mark_colour(images: list, reads_per_colour: int = 12):
    """
    Şık harfi taşıyan kutuların rengi. Harf okumak pahalı (resimlerde çok renkli leke olur): kutular önce renge göre gruplanır,
    kalabalıktan seyreğe her gruptan birkaç kutu okunur; harf okunan ilk grup işaret rengidir. (renk ya da None, okunan işaret).
    """
    groups = collections.defaultdict(list)
    for img in images:
        boxes, mask = colour_boxes(img)
        for sl in boxes:
            c = np.median(img[sl][mask[sl]], axis=0)
            groups[tuple((c // 40).astype(int))].append((img, sl, mask, c))
    for _, boxes in sorted(groups.items(), key=lambda g: -len(g[1])):
        if len(boxes) < 3:
            break
        step = max(1, len(boxes) // reads_per_colour)
        sample = boxes[::step][:reads_per_colour]
        read = [c for img, sl, mask, c in sample if (r := read_box(img, sl, mask)) and r[0] in LABELS]
        # Grubun çoğu şık harfi: işaret rengi bu.
        if len(read) >= 3 and len(read) * 2 >= len(sample):
            return [int(v) for v in np.median(read, axis=0).round()], len(boxes)
    return None, 0


def remove_marks(img: np.ndarray, colour):
    """İşaret kutularını siler, harfi düz yazıyla yeniden yazar; (sayfa, işaretler [{letter,x,y,w,h}])."""
    boxes, mask = colour_boxes(img, colour)
    tl = text_colour(img)
    marks = []
    for sl in boxes:
        r = read_box(img, sl, mask)
        if not r or r[0] not in LABELS:
            continue
        letter, (gy, gx, lh) = r
        glyph = candidate_glyph(letter + ")", lh)
        by0, by1, bx0, bx1 = sl[0].start, sl[0].stop, sl[1].start, sl[1].stop
        gh, gw = glyph.shape
        # Kutuyu ve kenarını, eski etiketin taştığı yeri de boşalt.
        img[max(min(by0 - 3, gy), 0):max(by1 + 3, gy + gh + 1),
            max(min(bx0 - 3, gx), 0):max(bx1 + 3, gx + gw + 1)] = 255
        tgt = img[gy:gy + gh, gx:gx + gw]
        tgt[:] = (255 - glyph[..., None] * (255 - tl))[:tgt.shape[0], :tgt.shape[1]]
        marks.append({"letter": letter, "x": int(bx0), "y": int(by0), "w": int(bx1 - bx0), "h": int(by1 - by0)})
    return img, marks


# ---------------------------------------------------------------- service

def analyze(pages: list, mark_colour=None) -> dict:
    """Örnek sayfalardan filigran şablonları (boyut başına) ve işaret rengi. Sonuç jetonla bellekte tutulur."""
    if not available():
        raise RuntimeError("page cleaning is not available")
    images = [_rgb(p) for p in pages]
    by_size = collections.defaultdict(list)
    for im in images:
        by_size[f"{im.shape[1]}x{im.shape[0]}"].append(im)

    templates, watermarks = {}, []
    for size, ims in by_size.items():
        if len(ims) < MIN_TEMPLATE_PAGES:
            continue
        step = max(1, len(ims) // MAX_TEMPLATE_PAGES)
        f, colour, keep = learn_watermark(_median(ims[::step][:MAX_TEMPLATE_PAGES]))
        if f is None:
            continue
        templates[size] = f.astype(np.float16)
        watermarks.append({"size": size, "colour": [int(c) for c in colour], "pixels": int(keep.sum())})

    marks_seen = 0
    if mark_colour is None:
        step = max(1, len(images) // MARK_SAMPLE_PAGES)
        mark_colour, marks_seen = learn_mark_colour(images[::step][:MARK_SAMPLE_PAGES])

    token = uuid.uuid4().hex
    analysis = {"templates": templates, "mark_colour": mark_colour}
    with _lock:
        _analyses[token] = (time.monotonic(), analysis)
        _analyses.move_to_end(token)
        while len(_analyses) > MAX_ANALYSES:
            _analyses.popitem(last=False)
    return {
        "token": token,
        "pages": len(images),
        "watermarks": watermarks,
        "markColour": mark_colour,
        "marksSeen": marks_seen,
    }


def get_analysis(token: str):
    with _lock:
        entry = _analyses.get(token)
        if entry is None or time.monotonic() - entry[0] > ANALYSIS_TTL_SECONDS:
            _analyses.pop(token, None)
            return None
        return entry[1]


def clean_page(data: bytes, analysis: dict, quality: int = 90):
    """Bir sayfayı temizler: (webp baytları, işaretler, filigran silindi mi)."""
    img = _rgb(data).astype(np.float32)
    size = f"{img.shape[1]}x{img.shape[0]}"
    template = analysis["templates"].get(size)
    if template is not None:
        img = np.clip(img / template.astype(np.float32), 0, 255)
    marks = []
    if analysis["mark_colour"] is not None:
        out = img.round().astype(np.uint8)
        out, marks = remove_marks(out, analysis["mark_colour"])
        img = out
    buffer = io.BytesIO()
    Image.fromarray(np.asarray(img).round().astype(np.uint8)).save(buffer, "WEBP", quality=quality)
    return buffer.getvalue(), marks, template is not None

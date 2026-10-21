"""Eğitim veri seti deposu (send-to-fix).

Her kayıt kendi dosyalarına yazılır; ortak bir JSON yoktur, hiçbir şey silinmez. Böylece klasör
Drive'a senkronlanırken birleştirme/üzerine yazma ile veri kaybı olmaz.

Düzen (DATASET_DIR altında, YOLO'ya hazır):
    questions/images/<page_id>.jpg            tam sayfa
    questions/labels/<page_id>.txt            soru kutuları (class 0)
    answers/images/<page_id>_<crop_id>.jpg    tek soru kırpması
    answers/labels/<page_id>_<crop_id>.txt    şık kutuları (class 0, kırpmaya göre)
    manifest.jsonl                            kayıt başına bir satır (izlenebilirlik)

page_id görselin içerik hash'idir: aynı sayfa tekrar kaydedilirse etiketi güncellenir, kopya oluşmaz.
Kırpma adları page_id ile başlar; train/val ayrımı sayfa bazında yapılır, kırpmalar sayfasını izler.

Bağımlılığı yalnız stdlib + Pillow: ultralytics yüklemeden test edilebilsin diye main.py'den ayrı.
"""
import hashlib
import json
import os
import threading
from dataclasses import dataclass
from datetime import datetime, timezone
from io import BytesIO
from pathlib import Path
from typing import List, Sequence, Tuple

from PIL import Image

from guards import clamp_box

# Box = (x, y, width, height), sayfa pikseli
Box = Tuple[float, float, float, float]

CLASS_ID = 0  # mevcut ağırlıklar (nc=2, names=[question, answers]) class 0 ile eğitildi
JPEG_QUALITY = 95

_manifest_lock = threading.Lock()


@dataclass
class QuestionSample:
    box: Box
    answers: List[Box]


def dataset_dir() -> Path:
    return Path(os.getenv("DATASET_DIR", "dataset"))


def content_id(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()[:16]


def yolo_lines(boxes: Sequence[Box], img_w: int, img_h: int) -> str:
    lines = []
    for x, y, w, h in boxes:
        x1, y1, x2, y2 = clamp_box(x, y, w, h, img_w, img_h)
        cx = (x1 + x2) / 2 / img_w
        cy = (y1 + y2) / 2 / img_h
        lines.append(f"{CLASS_ID} {cx:.6f} {cy:.6f} {(x2 - x1) / img_w:.6f} {(y2 - y1) / img_h:.6f}")
    return "\n".join(lines) + "\n" if lines else ""


def _write_atomic(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(path.name + ".tmp")
    tmp.write_bytes(data)
    os.replace(tmp, path)


def _jpeg_bytes(image: Image.Image) -> bytes:
    with BytesIO() as buffer:
        image.save(buffer, format="JPEG", quality=JPEG_QUALITY)
        return buffer.getvalue()


def _append_manifest(root: Path, record: dict) -> None:
    root.mkdir(parents=True, exist_ok=True)
    with _manifest_lock, open(root / "manifest.jsonl", "a", encoding="utf-8") as f:
        f.write(json.dumps(record, ensure_ascii=False) + "\n")


def labeled_page_ids(root: Path | None = None) -> List[str]:
    """Daha önce kaydedilmiş sayfaların page_id'leri (manifest'ten, tekrarsız, kayıt sırasıyla).

    İşaretleme modu bunlarla aynı görseli tekrar getirmez (page_id = içerik hash'i, dosya adından bağımsız).
    page_complete=False ("Kaydet ve Düzelt" kısmi) kayıtlar da sayılır: kullanıcı kararı, kısmi sayfa tekrar gelmez.
    Yazma kilidi alınmaz (send-to-fix beklemesin): ekleme satır satır, yarım/bozuk satır atlanır.
    """
    path = (root or dataset_dir()) / "manifest.jsonl"
    if not path.is_file():
        return []
    ids: dict[str, None] = {}
    with open(path, encoding="utf-8") as f:
        for line in f:
            try:
                page_id = json.loads(line).get("page_id")
            except (ValueError, AttributeError):
                continue
            if isinstance(page_id, str) and page_id:
                ids[page_id] = None
    return list(ids)


def save_page(
    image_bytes: bytes,
    questions: Sequence[QuestionSample],
    page_complete: bool = True,
    root: Path | None = None,
) -> dict:
    """Sayfayı ve kırpmaları veri setine yazar.

    page_complete=False: sayfadaki soruların hepsi işaretli değil; tam sayfa soru modeline girmez
    (işaretlenmemiş soru 'arka plan' diye öğrenilirdi), yalnız kırpmalar şık modeline girer.
    Kutu görselin dışındaysa ValueError (clamp_box).
    """
    root = root or dataset_dir()
    image = Image.open(BytesIO(image_bytes)).convert("RGB")
    page_id = content_id(image_bytes)
    img_w, img_h = image.size

    # Önce tüm kutuları doğrula: geçersiz kutuda hiçbir dosya yazılmasın.
    page_labels = yolo_lines([q.box for q in questions], img_w, img_h)

    page_image = None
    if page_complete:
        page_image = f"questions/images/{page_id}.jpg"
        _write_atomic(root / page_image, _jpeg_bytes(image))
        _write_atomic(root / f"questions/labels/{page_id}.txt", page_labels.encode())

    crops = []
    skipped_crops = 0
    for q in questions:
        if not q.answers:
            continue
        x1, y1, x2, y2 = clamp_box(*q.box, img_w, img_h)
        crop = image.crop((x1, y1, x2, y2))
        # Şık koordinatları kırpmaya göre (sayfa -> kırpma ötelemesi)
        answers = [(ax - x1, ay - y1, aw, ah) for ax, ay, aw, ah in q.answers]
        try:
            answer_labels = yolo_lines(answers, crop.width, crop.height)
        except ValueError:
            # Şıklardan biri soru kutusunun tamamen dışında: eksik etiketli kırpma şık modelini bozar.
            skipped_crops += 1
            continue
        crop_bytes = _jpeg_bytes(crop)
        name = f"{page_id}_{content_id(crop_bytes)}"
        _write_atomic(root / f"answers/images/{name}.jpg", crop_bytes)
        _write_atomic(root / f"answers/labels/{name}.txt", answer_labels.encode())
        crops.append(f"answers/images/{name}.jpg")

    _append_manifest(root, {
        "page_id": page_id,
        "saved_at": datetime.now(timezone.utc).isoformat(timespec="seconds"),
        "page_complete": page_complete,
        "width": img_w,
        "height": img_h,
        "questions": len(questions),
        "answer_counts": [len(q.answers) for q in questions],
        "crops": len(crops),
        "skipped_crops": skipped_crops,
    })

    return {"page_id": page_id, "page_image": page_image, "crops": crops, "skipped_crops": skipped_crops}

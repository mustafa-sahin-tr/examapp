"""Kitap aktarımı dışa aktarımını (issue #1133) eğitim veri setine yazar.

ZIP exam API'den iner (Admin): GET /api/book-imports/training-export?dryRun=false[&jobId=...]
İçinde sayfa görselleri (pages/<page_id>.webp) ve manifest.json (sayfa başına soru kutuları + şıklar, sayfa pikseli) vardır.
Yalnız onaylı testlerin sayfaları gelir; düzeltilen sayfada elle doğrulanmış kutular, diğerlerinde Claude'un kutuları.

Her sayfa dataset_store.save_page ile yazılır (işaretleme ekranının "eğitime gönder"iyle aynı düzen). page_id görselin
içerik hash'idir: veri setinde zaten olan sayfa atlanır (manifest.jsonl), tekrar çalıştırmak bir şey eklemez.
complete=false sayfa (etkinlik bloğu var) tam sayfa soru modeline girmez, yalnız şık kırpmaları yazılır.

Örnek (Mac, question-detector/ içinden):
  python training/import_book_export.py ~/Downloads/book-import-training-20261111-1200.zip --dry-run
  python training/import_book_export.py ~/Downloads/book-import-training-20261111-1200.zip
Sonra dataset/ her zamanki gibi Drive'a yüklenir (EGITIM.md).
"""
import argparse
import json
import sys
import zipfile
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from dataset_store import QuestionSample, content_id, dataset_dir, labeled_page_ids, save_page  # noqa: E402


def _samples(page: dict) -> list:
    return [
        QuestionSample(box=tuple(q["box"]), answers=[tuple(a) for a in q.get("answers", [])])
        for q in page.get("questions", [])
    ]


def import_export(zip_path: Path, root: Path, dry_run: bool = False) -> Counter:
    """ZIP'teki sayfaları veri setine yazar; sonuç sayaçları döner (written, existing, mismatch, invalid, crops ...)."""
    stats: Counter = Counter()
    existing = set(labeled_page_ids(root))
    with zipfile.ZipFile(zip_path) as zf:
        manifest = json.loads(zf.read("manifest.json"))
        for page in manifest.get("pages", []):
            page_id = page["pageId"]
            if page_id in existing:
                stats["existing"] += 1
                continue
            image_bytes = zf.read(page["file"])
            # Görsel ile kutular aynı sayfaya ait olmalı (page_id = içerik hash'i).
            if content_id(image_bytes) != page_id:
                stats["mismatch"] += 1
                continue
            samples = _samples(page)
            complete = bool(page.get("complete", True))
            if dry_run:
                stats["written"] += 1
                stats["questions"] += len(samples)
                stats["incomplete"] += 0 if complete else 1
                continue
            try:
                saved = save_page(image_bytes, samples, page_complete=complete, root=root)
            except ValueError:
                stats["invalid"] += 1
                continue
            existing.add(page_id)
            stats["written"] += 1
            stats["questions"] += len(samples)
            stats["incomplete"] += 0 if complete else 1
            stats["crops"] += len(saved["crops"])
            stats["skipped_crops"] += saved["skipped_crops"]
    return stats


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("zip", type=Path, help="exam API'den inen training-export ZIP'i")
    p.add_argument("--dataset", type=Path, default=None, help="veri seti klasörü (varsayılan DATASET_DIR ya da dataset/)")
    p.add_argument("--dry-run", action="store_true", help="yazmadan ne ekleneceğini say")
    args = p.parse_args()

    root = args.dataset or dataset_dir()
    stats = import_export(args.zip, root, dry_run=args.dry_run)
    verb = "eklenecek" if args.dry_run else "eklendi"
    print(
        f"{root}: {stats['written']} sayfa {verb} ({stats['questions']} soru, {stats['incomplete']} yalnız kırpma), "
        f"{stats['existing']} zaten vardı, {stats['mismatch']} görsel uyuşmadı, {stats['invalid']} geçersiz kutu"
        + ("" if args.dry_run else f"; {stats['crops']} şık kırpması, {stats['skipped_crops']} kırpma atlandı")
    )


if __name__ == "__main__":
    main()

"""Haftalık eğitim: birikmiş veri setiyle soru ve şık modellerini fine-tune eder (Colab'da çalışır).

Akış:
  1. Arşiv  : --dataset (Mac'ten Drive'a senkronlanan klasör) -> --archive (My Drive, silinmez).
              Mac'teki klasör silinse bile arşivdeki veri kalır; yalnız eklenir/günceller, hiç silmez.
  2. Ayrım  : arşivin TAMAMI train/val'e bölünür. Ayrım sayfa hash'ine göre sabittir (bir sayfa hep
              aynı tarafta kalır; kırpmalar sayfalarını izler) -> val sızıntısı yok, metrikler karşılaştırılabilir.
  3. Eğitim : mevcut models/<tür>-best.pt'den fine-tune (tüm veriyle -> eski bilgi unutulmaz).
  4. Karşılaştırma: eski ve yeni model aynı val setinde ölçülür; yeni model en az eski kadar iyiyse
              models/<tür>-best.pt'nin yerine geçer, sürümlü kopyası arşive yazılır.

Örnek (Colab):
  python train.py \
    --dataset /content/drive/Othercomputers/PersonalMacBookPro/question-detector/dataset \
    --models  /content/drive/Othercomputers/PersonalMacBookPro/question-detector/models \
    --archive /content/drive/MyDrive/hedefokul-ml
"""
import argparse
import json
import shutil
from datetime import datetime, timezone
from pathlib import Path

KINDS = ("questions", "answers")
MODEL_NAME = {"questions": "question", "answers": "answer"}
# Mevcut ağırlıkların başı nc=2 ile eğitildi; aynı isimler korunur (etiketler class 0).
CLASS_NAMES = {0: "question", 1: "answers"}
VAL_MODULO = 10  # sayfaların ~%10'u val
MIN_TRAIN_IMAGES = 5


def log(msg: str) -> None:
    print(msg, flush=True)


# ---------------------------------------------------------------------------- 1. arşiv

def sync_to_archive(dataset: Path, archive_data: Path) -> dict:
    """dataset -> archive_data; yeni/değişen dosyaları kopyalar, hiçbir şeyi silmez."""
    copied = 0
    for kind in KINDS:
        for sub in ("images", "labels"):
            src_dir = dataset / kind / sub
            if not src_dir.is_dir():
                continue
            dst_dir = archive_data / kind / sub
            dst_dir.mkdir(parents=True, exist_ok=True)
            for src in src_dir.iterdir():
                if not src.is_file() or src.name.endswith(".tmp"):
                    continue
                dst = dst_dir / src.name
                # Etiket aynı sayfa yeniden kaydedilince değişebilir; resim adı içerik hash'idir.
                if dst.exists() and dst.stat().st_size == src.stat().st_size and (
                    sub == "images" or dst.read_bytes() == src.read_bytes()
                ):
                    continue
                shutil.copy2(src, dst)
                copied += 1

    # manifest: satır birleşimi (Mac'teki dosya sıfırlansa bile arşivdeki satırlar kalır)
    src_manifest = dataset / "manifest.jsonl"
    dst_manifest = archive_data / "manifest.jsonl"
    if src_manifest.is_file():
        existing = dst_manifest.read_text(encoding="utf-8").splitlines() if dst_manifest.is_file() else []
        seen = set(existing)
        new_lines = [l for l in src_manifest.read_text(encoding="utf-8").splitlines() if l and l not in seen]
        if new_lines:
            with open(dst_manifest, "a", encoding="utf-8") as f:
                f.write("\n".join(new_lines) + "\n")
    return {"copied_files": copied}


# ---------------------------------------------------------------------------- 2. ayrım

def page_of(stem: str) -> str:
    return stem.split("_")[0]


def is_val(page_id: str) -> bool:
    try:
        return int(page_id[:8], 16) % VAL_MODULO == 0
    except ValueError:
        return False


def build_split(archive_data: Path, kind: str, work: Path) -> dict:
    """Arşivden work/<kind>/{train,val}/{images,labels} + data.yaml üretir."""
    images_dir = archive_data / kind / "images"
    labels_dir = archive_data / kind / "labels"
    pairs = []
    if images_dir.is_dir():
        for img in sorted(images_dir.glob("*.jpg")):
            label = labels_dir / f"{img.stem}.txt"
            if label.is_file():
                pairs.append((img, label))

    pages = sorted({page_of(img.stem) for img, _ in pairs})
    val_pages = {p for p in pages if is_val(p)}
    if not val_pages and len(pages) >= 2:
        val_pages = {pages[0]}  # az veriyle başlarken de bir val sayfası olsun

    root = work / kind
    if root.exists():
        shutil.rmtree(root)
    counts = {"train": 0, "val": 0}
    for img, label in pairs:
        split = "val" if page_of(img.stem) in val_pages else "train"
        for src, sub in ((img, "images"), (label, "labels")):
            dst = root / split / sub / src.name
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dst)
        counts[split] += 1

    yaml_path = root / "data.yaml"
    root.mkdir(parents=True, exist_ok=True)
    names = "\n".join(f"  {i}: {n}" for i, n in CLASS_NAMES.items())
    yaml_path.write_text(f"path: {root}\ntrain: train/images\nval: val/images\nnames:\n{names}\n", encoding="utf-8")
    return {"yaml": yaml_path, "pages": len(pages), "val_pages": len(val_pages), **counts}


# ---------------------------------------------------------------------------- 3-4. eğitim + terfi

def evaluate(weights: Path, data_yaml: Path, imgsz: int) -> dict:
    from ultralytics import YOLO

    m = YOLO(str(weights)).val(data=str(data_yaml), split="val", imgsz=imgsz, plots=False, verbose=False)
    return {"map50_95": float(m.box.map), "map50": float(m.box.map50)}


def train_kind(kind: str, split: dict, args, stamp: str, archive: Path) -> dict:
    from ultralytics import YOLO

    current = args.models / f"{MODEL_NAME[kind]}-best.pt"
    result = {"kind": kind, **{k: v for k, v in split.items() if k != "yaml"}}
    if not current.is_file():
        raise SystemExit(f"Mevcut model yok: {current}")
    if split["train"] < MIN_TRAIN_IMAGES or split["val"] == 0:
        log(f"[{kind}] yetersiz veri (train={split['train']}, val={split['val']}), atlanıyor.")
        return {**result, "status": "skipped"}

    model = YOLO(str(current))
    # optimizer açıkça verilmeli: 'auto' lr0'ı yok sayar. Düşük lr = mevcut bilgiyi koruyarak uyum.
    model.train(
        data=str(split["yaml"]),
        epochs=args.epochs,
        imgsz=args.imgsz,
        batch=args.batch,
        optimizer="SGD",
        lr0=args.lr0,
        warmup_epochs=1,
        freeze=args.freeze or None,
        patience=args.patience,
        project=str(args.work / "runs"),
        name=f"{MODEL_NAME[kind]}-{stamp}",
        exist_ok=True,
        plots=True,
    )
    new_best = Path(model.trainer.best)

    old_metrics = evaluate(current, split["yaml"], args.imgsz)
    new_metrics = evaluate(new_best, split["yaml"], args.imgsz)
    promote = args.force or new_metrics["map50_95"] >= old_metrics["map50_95"]
    log(f"[{kind}] eski mAP50-95={old_metrics['map50_95']:.4f}  yeni={new_metrics['map50_95']:.4f}  "
        f"-> {'TERFİ' if promote else 'eski model kalıyor'}")

    models_archive = archive / "models"
    models_archive.mkdir(parents=True, exist_ok=True)
    initial = models_archive / f"{MODEL_NAME[kind]}-initial.pt"
    if not initial.exists():
        shutil.copy2(current, initial)  # ilk öğretmen modeli bir kez saklanır
    versioned = models_archive / f"{MODEL_NAME[kind]}-{stamp}.pt"
    shutil.copy2(new_best, versioned)
    run_dir = new_best.parent.parent
    shutil.copytree(run_dir, archive / "runs" / run_dir.name, dirs_exist_ok=True)
    if promote:
        shutil.copy2(new_best, current)

    return {**result, "status": "promoted" if promote else "kept",
            "old": old_metrics, "new": new_metrics, "versioned": str(versioned)}


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--dataset", type=Path, required=True, help="Mac'ten senkronlanan dataset/ klasörü")
    p.add_argument("--models", type=Path, required=True, help="Mac'ten senkronlanan models/ klasörü")
    p.add_argument("--archive", type=Path, required=True, help="My Drive altında kalıcı arşiv kökü")
    p.add_argument("--work", type=Path, default=Path("/content/work"), help="Colab yerel diski (hızlı)")
    p.add_argument("--only", choices=("questions", "answers"), help="Yalnız bir modeli eğit")
    p.add_argument("--epochs", type=int, default=30)
    p.add_argument("--imgsz", type=int, default=640)
    p.add_argument("--batch", type=int, default=16)
    p.add_argument("--lr0", type=float, default=0.002)
    p.add_argument("--freeze", type=int, default=0, help="Dondurulacak ilk katman sayısı (örn. 10)")
    p.add_argument("--patience", type=int, default=10)
    p.add_argument("--force", action="store_true", help="Metrik düşse de yeni modeli terfi ettir")
    p.add_argument("--dry-run", action="store_true", help="Yalnız arşivle ve sayıları göster")
    args = p.parse_args()

    stamp = datetime.now(timezone.utc).strftime("%Y%m%d-%H%M")
    archive_data = args.archive / "dataset"
    log(f"Arşive senkron: {args.dataset} -> {archive_data}")
    report = {"stamp": stamp, "sync": sync_to_archive(args.dataset, archive_data), "models": []}

    kinds = (args.only,) if args.only else KINDS
    splits = {k: build_split(archive_data, k, args.work) for k in kinds}
    for k, s in splits.items():
        log(f"[{k}] sayfa={s['pages']} (val sayfa={s['val_pages']})  train={s['train']}  val={s['val']}")

    if not args.dry_run:
        for k in kinds:
            report["models"].append(train_kind(k, splits[k], args, stamp, args.archive))
        reports = args.archive / "reports"
        reports.mkdir(parents=True, exist_ok=True)
        (reports / f"{stamp}.json").write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
        log(f"Rapor: {reports / f'{stamp}.json'}")
    log(json.dumps(report, indent=2, ensure_ascii=False, default=str))


if __name__ == "__main__":
    main()

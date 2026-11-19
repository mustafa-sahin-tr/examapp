"""Soru / şık tespit modelini kitap aktarımı dışa aktarımına (issue #1133) karşı ölçer (issue #1136, #474).

Referans: exam API'nin eğitim dışa aktarımı (ZIP: pages/<page_id>.webp + manifest.json). Kutular onaylı testlerin
sayfalarından gelir: düzeltilen sayfada elle doğrulanmış (source=Manual), diğerlerinde Claude'un kutuları (source=Ai).
Yani Ai sayfalarında ölçülen şey modelin Claude ile ne kadar uyuştuğudur; Manual sayfalar gerçek doğrudur.

Tahmin iki yoldan alınır:
  --url   http://127.0.0.1:8080   çalışan servisin /predict ucu (konteynerdeki modeller)
  --models models                 yerel ağırlıklar (ultralytics kurulu olmalı)

Ölçütler (soru ve şık ayrı): IoU >= 0.5 ve >= 0.75 eşleşme oranı (açgözlü, en yüksek IoU önce), kaçırılan / fazla kutu,
soru sayısı tutan sayfa, kusursuz sayfa (bütün sorular 0.75'te eşleşmiş, fazla kutu yok), sayfa başına süre.
"complete=false" sayfalar (etkinlik bloğu var) soru modeli ölçümüne girmez.

Hibrit tespit (issue #1250) için kabul kuralı da sınanır: bir sayfa "kabul edilir" sayılır, eğer bulunan soru sayısı
beklenenle aynıysa ve her soruda --choices kadar şık varsa. "Kabul edilip yanlış" = kabul edilen ama kusursuz olmayan sayfa
(bir soru 0.75'te ya da bir şık 0.5'te eşleşmiyor): Claude'a sorulmadan geçecek hatalı sayfaların oranı.

Örnek (question-detector/ içinden):
  python training/evaluate_export.py ~/Downloads/book-import-training.zip --url http://127.0.0.1:8080 --report rapor.md
"""
import argparse
import base64
import io
import json
import statistics
import sys
import time
import urllib.request
import zipfile
from collections import defaultdict
from pathlib import Path

Box = tuple  # (x, y, w, h), sayfa pikseli

IOU_LEVELS = (0.5, 0.75)


def iou(a: Box, b: Box) -> float:
    ax2, ay2, bx2, by2 = a[0] + a[2], a[1] + a[3], b[0] + b[2], b[1] + b[3]
    iw = min(ax2, bx2) - max(a[0], b[0])
    ih = min(ay2, by2) - max(a[1], b[1])
    if iw <= 0 or ih <= 0:
        return 0.0
    inter = iw * ih
    return inter / (a[2] * a[3] + b[2] * b[3] - inter)


def match(truth: list, predicted: list, threshold: float) -> list:
    """Açgözlü eşleme: (truth_index, predicted_index, iou) listesi; her kutu en çok bir kez eşleşir."""
    pairs = sorted(
        ((iou(t, p), ti, pi) for ti, t in enumerate(truth) for pi, p in enumerate(predicted)),
        reverse=True,
    )
    used_t, used_p, matches = set(), set(), []
    for score, ti, pi in pairs:
        if score < threshold:
            break
        if ti in used_t or pi in used_p:
            continue
        used_t.add(ti)
        used_p.add(pi)
        matches.append((ti, pi, score))
    return matches


def new_counts() -> dict:
    return {
        "pages": 0, "count_match_pages": 0, "perfect_pages": 0,
        "questions": 0, "predicted_questions": 0, "q_matched": {level: 0 for level in IOU_LEVELS},
        "choices": 0, "predicted_choices": 0, "c_matched": {level: 0 for level in IOU_LEVELS},
        "choice_count_match": 0, "choice_count_questions": 0, "seconds": [],
        "accepted_pages": 0, "accepted_wrong_pages": 0,
    }


def score_page(counts: dict, truth: list, predicted: list, seconds: float, expected_choices: int = 4) -> None:
    """truth / predicted: [{"box": Box, "answers": [Box, ...]}]."""
    counts["pages"] += 1
    counts["seconds"].append(seconds)
    counts["questions"] += len(truth)
    counts["predicted_questions"] += len(predicted)
    t_boxes = [q["box"] for q in truth]
    p_boxes = [q["box"] for q in predicted]
    for level in IOU_LEVELS:
        counts["q_matched"][level] += len(match(t_boxes, p_boxes, level))
    if len(truth) == len(predicted):
        counts["count_match_pages"] += 1
    strict = match(t_boxes, p_boxes, IOU_LEVELS[-1])
    if len(strict) == len(truth) == len(predicted):
        counts["perfect_pages"] += 1
    # Hibrit kabul kuralı: sayı tutuyor ve her soruda beklenen kadar şık var.
    if len(predicted) == len(truth) and all(len(q["answers"]) == expected_choices for q in predicted):
        counts["accepted_pages"] += 1
        choices_ok = all(
            len(match(truth[ti]["answers"], predicted[pi]["answers"], IOU_LEVELS[0])) == len(truth[ti]["answers"])
            for ti, pi, _ in strict
        )
        if len(strict) != len(truth) or not choices_ok:
            counts["accepted_wrong_pages"] += 1
    # Şıklar yalnız eşleşen sorularda karşılaştırılır (soru kaçtıysa şık hatası soru hatasıdır).
    for ti, pi, _ in match(t_boxes, p_boxes, IOU_LEVELS[0]):
        t_answers, p_answers = truth[ti]["answers"], predicted[pi]["answers"]
        counts["choices"] += len(t_answers)
        counts["predicted_choices"] += len(p_answers)
        counts["choice_count_questions"] += 1
        counts["choice_count_match"] += 1 if len(t_answers) == len(p_answers) else 0
        for level in IOU_LEVELS:
            counts["c_matched"][level] += len(match(t_answers, p_answers, level))


def ratio(part: int, whole: int) -> float:
    return round(part / whole, 4) if whole else 0.0


def summarize(counts: dict) -> dict:
    seconds = counts["seconds"]
    return {
        "pages": counts["pages"],
        "questions": counts["questions"],
        "predictedQuestions": counts["predicted_questions"],
        "questionRecall": {str(level): ratio(counts["q_matched"][level], counts["questions"]) for level in IOU_LEVELS},
        "questionPrecision": {str(level): ratio(counts["q_matched"][level], counts["predicted_questions"]) for level in IOU_LEVELS},
        "missedQuestions": counts["questions"] - counts["q_matched"][IOU_LEVELS[0]],
        "extraQuestions": counts["predicted_questions"] - counts["q_matched"][IOU_LEVELS[0]],
        "countMatchPages": ratio(counts["count_match_pages"], counts["pages"]),
        "perfectPages": ratio(counts["perfect_pages"], counts["pages"]),
        "choices": counts["choices"],
        "choiceRecall": {str(level): ratio(counts["c_matched"][level], counts["choices"]) for level in IOU_LEVELS},
        "choiceCountMatch": ratio(counts["choice_count_match"], counts["choice_count_questions"]),
        "acceptedPages": ratio(counts["accepted_pages"], counts["pages"]),
        "acceptedWrongPages": ratio(counts["accepted_wrong_pages"], counts["accepted_pages"]),
        "secondsPerPage": round(statistics.mean(seconds), 3) if seconds else 0.0,
    }


def is_val_page(page_id: str) -> bool:
    """train.py'deki ayrımla aynı kural (is_val): sayfa hash'inin ilk 8 hanesi mod 10 == 0."""
    try:
        return int(page_id[:8], 16) % 10 == 0
    except ValueError:
        return False


# ---------------------------------------------------------------------------- tahmin

def to_questions(predictions: list) -> list:
    """/predict yanıtı -> [{"box", "answers"}]; yalnız soru sınıfı (class 0)."""
    return [
        {
            "box": (p["x"], p["y"], p["width"], p["height"]),
            "answers": [(s["x"], s["y"], s["width"], s["height"]) for s in p.get("subpredictions", [])],
        }
        for p in predictions
        if p.get("class_id") == 0
    ]


def url_predictor(base_url: str):
    def predict(image_bytes: bytes) -> list:
        body = json.dumps({"image_base64": base64.b64encode(image_bytes).decode()}).encode()
        request = urllib.request.Request(base_url.rstrip("/") + "/predict", data=body, headers={"Content-Type": "application/json"})
        with urllib.request.urlopen(request, timeout=300) as response:
            result = json.loads(response.read())
        if not result.get("success"):
            raise RuntimeError(result.get("error") or "predict failed")
        return to_questions(result["predictions"])

    return predict


def local_predictor(models_dir: Path, conf: float):
    from PIL import Image  # noqa: PLC0415 - yalnız yerel kipte gerekir
    from ultralytics import YOLO  # noqa: PLC0415

    question_model = YOLO(str(models_dir / "question-best.pt"))
    answer_model = YOLO(str(models_dir / "answer-best.pt"))

    def boxes_of(model, image) -> list:
        result = model.predict(source=image, conf=conf, verbose=False)[0]
        return [
            (int(x1), int(y1), int(x2 - x1), int(y2 - y1))
            for (x1, y1, x2, y2), cls in zip(result.boxes.xyxy.tolist(), result.boxes.cls.tolist())
            if int(cls) == 0
        ]

    def predict(image_bytes: bytes) -> list:
        image = Image.open(io.BytesIO(image_bytes)).convert("RGB")
        questions = []
        for x, y, w, h in boxes_of(question_model, image):
            crop = image.crop((x, y, x + w, y + h))
            answers = [(ax + x, ay + y, aw, ah) for ax, ay, aw, ah in boxes_of(answer_model, crop)]
            questions.append({"box": (x, y, w, h), "answers": answers})
        return questions

    return predict


# ---------------------------------------------------------------------------- çalıştırma

def evaluate(zip_path: Path, predict, limit: int = 0, expected_choices: int = 4, only_pages: set | None = None) -> dict:
    groups = defaultdict(new_counts)
    failed = 0
    with zipfile.ZipFile(zip_path) as zf:
        manifest = json.loads(zf.read("manifest.json"))
        pages = [
            page for page in manifest.get("pages", [])
            if page.get("complete", True) and (only_pages is None or page["pageId"] in only_pages)
        ]
        for index, page in enumerate(pages[:limit] if limit else pages):
            truth = [
                {"box": tuple(q["box"]), "answers": [tuple(a) for a in q.get("answers", [])]}
                for q in page.get("questions", [])
            ]
            started = time.monotonic()
            try:
                predicted = predict(zf.read(page["file"]))
            except Exception as error:  # noqa: BLE001 - bir sayfa ölçümü durdurmasın
                failed += 1
                print(f"  {page['pageId']}: {error}", file=sys.stderr)
                continue
            seconds = time.monotonic() - started
            for key in ("all", f"source:{page.get('source', '?')}", f"book:{page.get('bookName', '?')}"):
                score_page(groups[key], truth, predicted, seconds, expected_choices)
            if (index + 1) % 25 == 0:
                print(f"  {index + 1} sayfa", file=sys.stderr)
    return {"failedPages": failed, "groups": {key: summarize(counts) for key, counts in sorted(groups.items())}}


def to_markdown(result: dict) -> str:
    lines = [
        "| Grup | Sayfa | Soru | Bulunan | Soru IoU≥0.5 | Soru IoU≥0.75 | Kaçan | Fazla | Sayı tutan sayfa | Kusursuz sayfa | Şık IoU≥0.5 | Şık IoU≥0.75 | Şık sayısı tutan soru | Kabul edilen sayfa | Kabul edilip yanlış | sn/sayfa |",
        "|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|",
    ]
    pct = lambda value: f"{value * 100:.1f}%"  # noqa: E731
    for name, g in result["groups"].items():
        lines.append(
            f"| {name} | {g['pages']} | {g['questions']} | {g['predictedQuestions']} | {pct(g['questionRecall']['0.5'])} | "
            f"{pct(g['questionRecall']['0.75'])} | {g['missedQuestions']} | {g['extraQuestions']} | {pct(g['countMatchPages'])} | "
            f"{pct(g['perfectPages'])} | {pct(g['choiceRecall']['0.5'])} | {pct(g['choiceRecall']['0.75'])} | "
            f"{pct(g['choiceCountMatch'])} | {pct(g['acceptedPages'])} | {pct(g['acceptedWrongPages'])} | {g['secondsPerPage']} |"
        )
    if result["failedPages"]:
        lines.append(f"\nTahmin alınamayan sayfa: {result['failedPages']}")
    return "\n".join(lines)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("zip", type=Path, help="eğitim dışa aktarımı (ZIP)")
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--url", help="çalışan question-detector adresi (/predict)")
    source.add_argument("--models", type=Path, help="yerel ağırlık klasörü (question-best.pt, answer-best.pt)")
    parser.add_argument("--conf", type=float, default=0.25, help="yerel kipte güven eşiği (servis 0.25 kullanır)")
    parser.add_argument("--limit", type=int, default=0, help="yalnız ilk N sayfa (deneme)")
    parser.add_argument("--choices", type=int, default=4, help="kabul kuralında soru başına beklenen şık sayısı")
    parser.add_argument("--val-only", action="store_true",
                        help="yalnız eğitimde val'e düşen sayfalar (train.py ile aynı ayrım): yeni modeli görmediği sayfada ölç")
    parser.add_argument("--json", type=Path, help="sonucu JSON olarak yaz")
    parser.add_argument("--report", type=Path, help="sonucu Markdown tablosu olarak yaz")
    args = parser.parse_args()

    predict = url_predictor(args.url) if args.url else local_predictor(args.models, args.conf)
    only_pages = None
    if args.val_only:
        with zipfile.ZipFile(args.zip) as zf:
            only_pages = {p["pageId"] for p in json.loads(zf.read("manifest.json")).get("pages", []) if is_val_page(p["pageId"])}
    result = evaluate(args.zip, predict, args.limit, args.choices, only_pages)
    if args.json:
        args.json.write_text(json.dumps(result, ensure_ascii=False, indent=2))
    markdown = to_markdown(result)
    if args.report:
        args.report.write_text(markdown + "\n")
    print(markdown)


if __name__ == "__main__":
    main()

import json
import os
import sys
from io import BytesIO
from pathlib import Path

import pytest
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))
sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(__file__)), "training"))
from dataset_store import QuestionSample, content_id, labeled_page_ids, save_page, yolo_lines  # noqa: E402
import train  # noqa: E402


def _page(color=(255, 255, 255), size=(200, 100)) -> bytes:
    buf = BytesIO()
    Image.new("RGB", size, color).save(buf, format="PNG")
    return buf.getvalue()


def _q(box, answers=()):
    return QuestionSample(box=box, answers=list(answers))


def test_yolo_lines_normalizes_center_and_size():
    assert yolo_lines([(0, 0, 100, 50)], 200, 100) == "0 0.250000 0.250000 0.500000 0.500000\n"


def test_save_page_writes_page_labels_and_answer_crops(tmp_path):
    q = _q((10, 10, 100, 50), [(20, 20, 30, 10), (20, 40, 30, 10)])
    saved = save_page(_page(), [q], root=tmp_path)

    pid = saved["page_id"]
    assert (tmp_path / f"questions/images/{pid}.jpg").is_file()
    assert (tmp_path / f"questions/labels/{pid}.txt").read_text().count("\n") == 1
    assert len(saved["crops"]) == 1
    crop_label = (tmp_path / saved["crops"][0]).with_suffix(".txt").as_posix().replace("/images/", "/labels/")
    lines = Path(crop_label).read_text().splitlines()
    # şık kırpmaya göre: (20-10, 20-10) -> merkez (25, 15) / (100, 50)
    assert lines[0] == "0 0.250000 0.300000 0.300000 0.200000"
    assert Path(saved["crops"][0]).name.startswith(pid + "_")

    record = json.loads((tmp_path / "manifest.jsonl").read_text().strip())
    assert record["page_id"] == pid and record["answer_counts"] == [2]


def test_incomplete_page_keeps_only_crops(tmp_path):
    saved = save_page(_page(), [_q((10, 10, 100, 50), [(20, 20, 30, 10)])], page_complete=False, root=tmp_path)
    assert saved["page_image"] is None
    assert not (tmp_path / "questions").exists()
    assert len(saved["crops"]) == 1


def test_same_page_resaved_overwrites_labels_not_duplicates(tmp_path):
    img = _page()
    save_page(img, [_q((10, 10, 50, 50))], root=tmp_path)
    save_page(img, [_q((10, 10, 50, 50)), _q((100, 10, 50, 50))], root=tmp_path)
    labels = list((tmp_path / "questions/labels").iterdir())
    assert len(labels) == 1 and labels[0].read_text().count("\n") == 2


def test_box_outside_image_writes_nothing(tmp_path):
    with pytest.raises(ValueError):
        save_page(_page(), [_q((500, 500, 10, 10))], root=tmp_path)
    assert not (tmp_path / "questions").exists()


def test_answer_outside_question_skips_that_crop(tmp_path):
    saved = save_page(_page(), [_q((10, 10, 50, 50), [(150, 80, 10, 10)])], root=tmp_path)
    assert saved["crops"] == [] and saved["skipped_crops"] == 1


def test_archive_sync_never_deletes_and_split_follows_page(tmp_path):
    dataset, archive = tmp_path / "ds", tmp_path / "archive"
    for color in [(255, 255, 255), (250, 250, 250), (240, 240, 240)]:
        save_page(_page(color), [_q((10, 10, 100, 50), [(20, 20, 30, 10)])], root=dataset)
    train.sync_to_archive(dataset, archive)

    # Mac'teki klasör silinse bile arşiv kalır
    for p in (dataset / "questions/images").iterdir():
        p.unlink()
    train.sync_to_archive(dataset, archive)
    assert len(list((archive / "questions/images").iterdir())) == 3
    assert len((archive / "manifest.jsonl").read_text().splitlines()) == 3

    q = train.build_split(archive, "questions", tmp_path / "work")
    a = train.build_split(archive, "answers", tmp_path / "work")
    assert q["train"] + q["val"] == 3 and q["val"] >= 1
    val_pages = {p.stem for p in (tmp_path / "work/questions/val/images").iterdir()}
    val_crops = {train.page_of(p.stem) for p in (tmp_path / "work/answers/val/images").iterdir()}
    assert val_crops == val_pages


def test_save_page_with_empty_questions_and_page_complete_true(tmp_path):
    """Boş soru listesi + page_complete=True: sayfa resmi ve BOŞ etiket dosyası yazılır, kırpma yok."""
    saved = save_page(_page(), [], page_complete=True, root=tmp_path)

    pid = saved["page_id"]
    assert saved["page_image"] == f"questions/images/{pid}.jpg"
    assert saved["crops"] == []
    assert saved["skipped_crops"] == 0

    # Sayfa resmi var
    assert (tmp_path / saved["page_image"]).is_file()

    # Etiket dosyası var ve BOŞ
    label_path = tmp_path / f"questions/labels/{pid}.txt"
    assert label_path.is_file()
    assert label_path.read_text() == ""

    # Manifest kaydı var
    record = json.loads((tmp_path / "manifest.jsonl").read_text().strip())
    assert record["page_id"] == pid
    assert record["questions"] == 0
    assert record["answer_counts"] == []
    assert record["page_complete"] is True


def test_save_page_with_empty_questions_and_page_complete_false(tmp_path):
    """Boş soru listesi + page_complete=False: sayfa dosyaları yazılmaz, kırpma yok."""
    saved = save_page(_page(), [], page_complete=False, root=tmp_path)

    assert saved["page_image"] is None
    assert saved["crops"] == []
    assert saved["skipped_crops"] == 0

    # questions/ klasörü yok (page_complete=False olduğunda)
    assert not (tmp_path / "questions").exists()
    # answers/ klasörü de yok (sorular yoksa kırpma olmaz)
    assert not (tmp_path / "answers").exists()

    # Manifest kaydı her zaman yazılıyor (page_complete durumundan bağımsız)
    record = json.loads((tmp_path / "manifest.jsonl").read_text().strip())
    assert record["page_complete"] is False
    assert record["questions"] == 0
    assert record["answer_counts"] == []


def test_build_split_includes_empty_label_pages(tmp_path):
    """build_split: boş etiketli sayfayı train/val'e kopyalıyor."""
    archive = tmp_path / "archive"

    # Normal sayfa (soru + şık etiketi)
    img1 = _page((255, 255, 255))
    saved1 = save_page(img1, [_q((10, 10, 100, 50), [(20, 20, 30, 10)])], root=archive)

    # Boş soru sayfası (etiket dosyası BOŞ)
    img2 = _page((240, 240, 240))
    saved2 = save_page(img2, [], page_complete=True, root=archive)

    # build_split çalıştır
    split = train.build_split(archive, "questions", tmp_path / "work")

    # Her iki sayfa da ayrıma dahil edilmeli
    assert split["train"] + split["val"] == 2

    # Val sayfasını bul
    val_images = list((tmp_path / "work/questions/val/images").glob("*.jpg"))
    val_stems = {p.stem for p in val_images}

    # Her val sayfasının etiket dosyası olmalı (boş da olsa)
    for stem in val_stems:
        label = (tmp_path / "work/questions/val/labels" / f"{stem}.txt")
        assert label.is_file(), f"Label missing for {stem}"


def test_labeled_page_ids_reads_manifest_unique_and_skips_bad_lines(tmp_path):
    assert labeled_page_ids(tmp_path) == []
    first = save_page(_page(), [], page_complete=True, root=tmp_path)["page_id"]
    save_page(_page(), [], page_complete=True, root=tmp_path)  # aynı sayfa: tekrar listelenmez
    with open(tmp_path / "manifest.jsonl", "a", encoding="utf-8") as f:
        f.write('{"page_id": "bozuk\n[]\n')
    second = save_page(_page(color=(0, 0, 0)), [], page_complete=False, root=tmp_path)["page_id"]
    assert labeled_page_ids(tmp_path) == [first, second]
    assert first == content_id(_page())

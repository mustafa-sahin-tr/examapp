import json
import os
import sys
import zipfile
from io import BytesIO

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))
sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(__file__)), "training"))
from dataset_store import content_id, labeled_page_ids  # noqa: E402
from import_book_export import import_export  # noqa: E402


def _page(color) -> bytes:
    buf = BytesIO()
    Image.new("RGB", (400, 600), color).save(buf, format="WEBP")
    return buf.getvalue()


def _export(tmp_path, pages):
    path = tmp_path / "export.zip"
    entries = []
    with zipfile.ZipFile(path, "w") as zf:
        for image, complete, questions, page_id in pages:
            page_id = page_id or content_id(image)
            name = f"pages/{page_id}.webp"
            zf.writestr(name, image)
            entries.append({"pageId": page_id, "file": name, "complete": complete, "questions": questions})
        zf.writestr("manifest.json", json.dumps({"pages": entries}))
    return path


QUESTION = {"box": [10, 10, 200, 150], "answers": [[20, 100, 50, 20], [100, 100, 50, 20]]}


def test_writes_pages_once_and_skips_mismatched_images(tmp_path):
    white, grey, blue = _page((255, 255, 255)), _page((128, 128, 128)), _page((0, 0, 255))
    export = _export(tmp_path, [
        (white, True, [QUESTION], None),
        (grey, False, [QUESTION], None),
        (blue, True, [], "0000000000000000"),
    ])
    root = tmp_path / "dataset"

    dry = import_export(export, root, dry_run=True)
    assert dry["written"] == 2 and dry["mismatch"] == 1
    assert not root.exists()

    stats = import_export(export, root)
    assert stats["written"] == 2 and stats["questions"] == 2 and stats["incomplete"] == 1 and stats["crops"] == 2
    assert set(labeled_page_ids(root)) == {content_id(white), content_id(grey)}
    # Etkinlikli (eksik) sayfa tam sayfa olarak yazılmaz.
    assert (root / f"questions/labels/{content_id(white)}.txt").is_file()
    assert not (root / f"questions/labels/{content_id(grey)}.txt").exists()

    again = import_export(export, root)
    assert again["written"] == 0 and again["existing"] == 2


def test_box_outside_the_image_is_counted_not_written(tmp_path):
    image = _page((255, 255, 255))
    export = _export(tmp_path, [(image, True, [{"box": [5000, 5000, 10, 10], "answers": []}], None)])
    stats = import_export(export, tmp_path / "dataset")
    assert stats["invalid"] == 1 and stats["written"] == 0

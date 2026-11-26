"""
/send-to-fix endpoint testini FastAPI TestClient ile yap.
YOLO modeli yüklemeden test etmek için unittest.mock patch kullanılır.
"""
import base64
import os
import sys
from io import BytesIO
from unittest.mock import MagicMock, patch

import pytest
from PIL import Image

# main.py fastapi + ultralytics ister; yalnız pillow + pytest olan ortamda (./commands.sh test) atlanır.
pytest.importorskip("fastapi")
pytest.importorskip("ultralytics")

# Mock YOLO model'ini hazırla ve patch'le YOLO import'unu
yolo_mock = MagicMock()

# main.py'yi import etmeden önce YOLO'yu patch'le
with patch("ultralytics.YOLO", return_value=yolo_mock):
    # Şimdi main.py'yi import et (YOLO mock olacak)
    sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))
    import main
    from fastapi.testclient import TestClient

client = TestClient(main.app)


def _page_base64(color=(255, 255, 255), size=(200, 100)) -> str:
    """PNG görsel döndür (base64)."""
    buf = BytesIO()
    Image.new("RGB", size, color).save(buf, format="PNG")
    return base64.b64encode(buf.getvalue()).decode()


def test_send_to_fix_with_empty_questions(tmp_path, monkeypatch):
    """Boş soru listesi ile /send-to-fix endpoint → 200, added: 0."""
    # dataset_dir'i geçici dizine yönlendir
    monkeypatch.setenv("DATASET_DIR", str(tmp_path))
    
    payload = {
        "answerCount": 4,
        "imageData": {"image_base64": _page_base64()},
        "questions": [],
        "pageComplete": True,
    }
    
    response = client.post("/send-to-fix", json=payload)
    assert response.status_code == 200
    data = response.json()
    assert data["success"] is True
    assert data["added"] == 0


def test_labeled_pages_lists_trained_page_ids(tmp_path, monkeypatch):
    monkeypatch.setenv("DATASET_DIR", str(tmp_path))
    assert client.get("/labeled-pages").json() == {"pageIds": []}
    page = _page_base64()
    saved = client.post("/send-to-fix", json={
        "answerCount": 4, "imageData": {"image_base64": page}, "questions": [], "pageComplete": True,
    }).json()
    assert client.get("/labeled-pages").json() == {"pageIds": [saved["imageFile"]]}


# --- Issue #461 ---

def _fix_payload(image_base64: str) -> dict:
    return {"answerCount": 4, "imageData": {"image_base64": image_base64}, "questions": [], "pageComplete": True}


def test_send_to_fix_records_gateway_submitter(tmp_path, monkeypatch):
    monkeypatch.setenv("DATASET_DIR", str(tmp_path))
    sub = "0b6f2c1e-1111-2222-3333-444455556666"
    assert client.post("/send-to-fix", json=_fix_payload(_page_base64()), headers={"X-Examapp-User": sub}).status_code == 200
    import json as _json
    record = _json.loads((tmp_path / "manifest.jsonl").read_text().splitlines()[-1])
    assert record["submitted_by"] == sub


def test_send_to_fix_low_disk_returns_507_and_writes_nothing(tmp_path, monkeypatch):
    monkeypatch.setenv("DATASET_DIR", str(tmp_path))
    monkeypatch.setattr(main, "has_free_disk", lambda _path: False)
    assert client.post("/send-to-fix", json=_fix_payload(_page_base64())).status_code == 507
    assert not (tmp_path / "manifest.jsonl").exists()


def test_send_to_fix_invalid_base64_returns_400(tmp_path, monkeypatch):
    monkeypatch.setenv("DATASET_DIR", str(tmp_path))
    assert client.post("/send-to-fix", json=_fix_payload("not*base64")).status_code == 400


def test_send_to_fix_unsupported_format_returns_400(tmp_path, monkeypatch):
    monkeypatch.setenv("DATASET_DIR", str(tmp_path))
    buf = BytesIO()
    Image.new("RGB", (20, 20)).save(buf, format="BMP")
    encoded = base64.b64encode(buf.getvalue()).decode()
    assert client.post("/send-to-fix", json=_fix_payload(encoded)).status_code == 400


def test_yolo_models_load_on_the_first_predict_not_at_startup(monkeypatch):
    # Bellek: torch + modeller yalnız /predict ile yüklenir.
    assert main._yolo is None or isinstance(main._yolo, tuple)
    monkeypatch.setattr(main, "_yolo", None)
    loads = []

    class FakeYolo:
        def __init__(self, path):
            loads.append(path)

    with patch("ultralytics.YOLO", FakeYolo):
        first = main.yolo_models()
        second = main.yolo_models()

    assert loads == [main.QUESTION_MODEL_PATH, main.ANSWER_MODEL_PATH]
    assert first is second

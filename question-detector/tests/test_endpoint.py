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

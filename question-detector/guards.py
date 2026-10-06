"""Issue #366: istek gövdesi boyut sınırı + güvenli yol birleştirme.

Bağımlılığı yalnız stdlib: ultralytics/torch yüklemeden test edilebilsin diye main.py'den ayrı.
"""
import json
import os
from pathlib import Path

MAX_BODY_BYTES = int(os.getenv("MAX_REQUEST_BODY_BYTES", str(25 * 1024 * 1024)))  # 25 MiB
MAX_QUESTIONS_PER_REQUEST = int(os.getenv("MAX_QUESTIONS_PER_REQUEST", "200"))
MAX_ANSWERS_PER_QUESTION = int(os.getenv("MAX_ANSWERS_PER_QUESTION", "20"))


class BodySizeLimitMiddleware:
    """Saf ASGI: Content-Length veya akan gövde sınırı aşarsa 413 döner."""

    def __init__(self, app, max_bytes: int = MAX_BODY_BYTES):
        self.app = app
        self.max_bytes = max_bytes

    async def __call__(self, scope, receive, send):
        if scope["type"] != "http":
            await self.app(scope, receive, send)
            return

        for name, value in scope.get("headers", []):
            if name == b"content-length":
                try:
                    declared = int(value)
                except ValueError:
                    declared = 0
                if declared > self.max_bytes:
                    await self._reject(send)
                    return

        received = 0
        started = False
        too_big = False

        async def limited_receive():
            nonlocal received, too_big
            message = await receive()
            if message["type"] == "http.request":
                received += len(message.get("body", b""))
                if received > self.max_bytes:
                    too_big = True
                    # Uygulamaya boş/sonlanmış gövde ver; yanıtı biz 413 yapacağız.
                    return {"type": "http.request", "body": b"", "more_body": False}
            return message

        async def guarded_send(message):
            nonlocal started
            if too_big:
                if not started:
                    started = True
                    await self._reject(send)
                return
            await send(message)

        await self.app(scope, limited_receive, guarded_send)
        if too_big and not started:
            await self._reject(send)

    @staticmethod
    async def _reject(send):
        body = json.dumps({"detail": "Request body too large"}).encode()
        await send({
            "type": "http.response.start",
            "status": 413,
            "headers": [(b"content-type", b"application/json"),
                        (b"content-length", str(len(body)).encode())],
        })
        await send({"type": "http.response.body", "body": body})


def safe_join(base_dir, filename: str) -> Path:
    """base_dir altında kalan yolu döndürür; '..', ayırıcı veya mutlak yol içeren ad ValueError."""
    if not filename or filename != os.path.basename(filename) or "/" in filename or "\\" in filename:
        raise ValueError("invalid filename")
    base = Path(base_dir).resolve()
    target = (base / filename).resolve()
    if base != target.parent:
        raise ValueError("path escapes base directory")
    return target


MAX_COORD = float(os.getenv("MAX_BOX_COORD", "20000"))


def clamp_box(x: float, y: float, width: float, height: float, img_w: int, img_h: int):
    """Kutuyu goruntu sinirlarina kirpar; (left, upper, right, lower) int doner.
    Kirpma sonrasi bos kalirsa ValueError (PIL'e dev/bos crop gitmesin)."""
    left = max(0, min(int(x), img_w))
    upper = max(0, min(int(y), img_h))
    right = max(0, min(int(x + width), img_w))
    lower = max(0, min(int(y + height), img_h))
    if right <= left or lower <= upper:
        raise ValueError("box is empty after clamping to image bounds")
    return left, upper, right, lower

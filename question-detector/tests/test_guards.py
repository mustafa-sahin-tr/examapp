import asyncio
import os
import sys
import tempfile

import pytest

sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))
from guards import BodySizeLimitMiddleware, clamp_box, safe_join  # noqa: E402


async def _ok_app(scope, receive, send):
    # Gövdeyi tüketir, 200 döner.
    while True:
        msg = await receive()
        if not msg.get("more_body"):
            break
    await send({"type": "http.response.start", "status": 200, "headers": []})
    await send({"type": "http.response.body", "body": b"ok"})


def _call(app, headers, chunks):
    sent = []
    queue = list(chunks)

    async def receive():
        body = queue.pop(0) if queue else b""
        return {"type": "http.request", "body": body, "more_body": bool(queue)}

    async def send(m):
        sent.append(m)

    scope = {"type": "http", "headers": headers}
    asyncio.run(app(scope, receive, send))
    return next(m["status"] for m in sent if m["type"] == "http.response.start")


def test_declared_content_length_over_limit_returns_413():
    app = BodySizeLimitMiddleware(_ok_app, max_bytes=10)
    assert _call(app, [(b"content-length", b"11")], [b"x" * 11]) == 413


def test_streamed_body_over_limit_returns_413():
    app = BodySizeLimitMiddleware(_ok_app, max_bytes=10)
    assert _call(app, [], [b"x" * 6, b"x" * 6]) == 413


def test_within_limit_passes():
    app = BodySizeLimitMiddleware(_ok_app, max_bytes=10)
    assert _call(app, [(b"content-length", b"5")], [b"x" * 5]) == 200


@pytest.mark.parametrize("name", ["../evil.jpg", "..\\evil.jpg", "a/b.jpg", "/etc/passwd", "", "C:\\x.jpg"])
def test_safe_join_rejects_traversal(name):
    with tempfile.TemporaryDirectory() as d:
        with pytest.raises(ValueError):
            safe_join(d, name)


def test_safe_join_accepts_plain_name():
    with tempfile.TemporaryDirectory() as d:
        assert str(safe_join(d, "abc_q1.jpg")).endswith("abc_q1.jpg")


def test_clamp_box_limits_to_image_bounds():
    assert clamp_box(-5, -5, 1_000_000, 1_000_000, 100, 50) == (0, 0, 100, 50)


def test_clamp_box_normal_box_unchanged():
    assert clamp_box(10, 10, 20, 20, 100, 100) == (10, 10, 30, 30)


@pytest.mark.parametrize("box", [(200, 0, 10, 10), (0, 0, 0, 10), (10, 10, -50, 5)])
def test_clamp_box_empty_raises(box):
    with pytest.raises(ValueError):
        clamp_box(*box, 100, 100)

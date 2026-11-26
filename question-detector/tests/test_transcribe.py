"""#1219: ses → yazı. faster-whisper yoksa özellik kapalı (exam API "QR'dan al"ı göstermez)."""
import os
import sys

import pytest

sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))
import transcribe  # noqa: E402


def test_without_faster_whisper_transcription_is_unavailable(monkeypatch):
    monkeypatch.setattr(transcribe, "WhisperModel", None)

    assert transcribe.available() is False
    with pytest.raises(RuntimeError):
        transcribe.transcribe(b"ID3", ".mp3")


def test_a_model_that_cannot_load_turns_the_feature_off(monkeypatch):
    def broken(*args, **kwargs):
        raise OSError("download failed")

    monkeypatch.setattr(transcribe, "WhisperModel", broken)
    monkeypatch.setattr(transcribe, "_model", None)
    monkeypatch.setattr(transcribe, "_error", None)

    with pytest.raises(RuntimeError):
        transcribe.transcribe(b"ID3", ".mp3")
    assert transcribe.available() is False


def test_segments_are_joined_into_one_text(monkeypatch):
    class Segment:
        def __init__(self, text):
            self.text = text

    class Model:
        def __init__(self, *args, **kwargs):
            pass

        def transcribe(self, path, **kwargs):
            assert kwargs["language"] == "tr"
            assert os.path.exists(path)
            return iter([Segment(" Güneşin şekli küreseldir. "), Segment("B seçeneğini işaretleyebiliriz.")]), None

    monkeypatch.setattr(transcribe, "WhisperModel", Model)
    monkeypatch.setattr(transcribe, "_model", None)
    monkeypatch.setattr(transcribe, "_error", None)

    assert transcribe.transcribe(b"ID3", ".mp3") == "Güneşin şekli küreseldir. B seçeneğini işaretleyebiliriz."


class _Model:
    def __init__(self, *args, **kwargs):
        pass

    def transcribe(self, path, **kwargs):
        return iter([]), None


def _fresh(monkeypatch, idle=600):
    monkeypatch.setattr(transcribe, "WhisperModel", _Model)
    monkeypatch.setattr(transcribe, "_model", None)
    monkeypatch.setattr(transcribe, "_error", None)
    monkeypatch.setattr(transcribe, "_busy", 0)
    monkeypatch.setattr(transcribe, "IDLE_SECONDS", idle)
    # Arka plan iş parçacığı testte başlamaz; bırakma unload_if_idle ile elle sürülür.
    monkeypatch.setattr(transcribe, "_start_reaper", lambda: None)


def test_the_model_is_released_after_the_idle_time_and_reloaded_on_the_next_request(monkeypatch):
    _fresh(monkeypatch)
    transcribe.transcribe(b"ID3", ".mp3")
    used = transcribe._last_used

    assert transcribe.loaded()
    assert transcribe.unload_if_idle(now=used + 599) is False
    assert transcribe.unload_if_idle(now=used + 600) is True
    assert transcribe.loaded() is False

    transcribe.transcribe(b"ID3", ".mp3")
    assert transcribe.loaded()


def test_a_running_transcription_keeps_the_model(monkeypatch):
    _fresh(monkeypatch)
    model = transcribe._acquire()
    assert model is not None

    assert transcribe.unload_if_idle(now=transcribe._last_used + 10_000) is False
    transcribe._release()
    assert transcribe.unload_if_idle(now=transcribe._last_used + 10_000) is True


def test_zero_idle_seconds_never_releases(monkeypatch):
    _fresh(monkeypatch, idle=0)
    transcribe.transcribe(b"ID3", ".mp3")

    assert transcribe.unload_if_idle(now=transcribe._last_used + 10_000) is False
    assert transcribe.loaded()

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

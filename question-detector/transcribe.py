"""
#1219: ses kaydını yazıya çevirme (kitap aktarımında QR çözümlerinden cevap anahtarı).

faster-whisper isteğe bağlıdır: paket yoksa ya da model yüklenemezse `available()` False döner ve exam API "QR'dan al"ı
göstermez. Model `WHISPER_MODEL` (varsayılan `large-v3-turbo` (~1,6 GB; `small` Türkçe şık harflerinde zayıf); ilk kullanımda indirilir, `WHISPER_CACHE` dizinine), dil Türkçe.
"""
import logging
import os
import tempfile
import threading

logger = logging.getLogger(__name__)

MODEL_NAME = os.getenv("WHISPER_MODEL", "large-v3-turbo")
# Çözüm kayıtları "B seçeneğini işaretleyebiliriz" diye biter; ipucu modeli şık harflerini harf olarak yazmaya yöneltir
# ("de seçeneği" yerine "D seçeneği").
PROMPT = "Doğru cevap A seçeneği. B şıkkı. Cevabımız C seçeneğidir. D seçeneğini işaretleyebiliriz. E şıkkı."
CACHE_DIR = os.getenv("WHISPER_CACHE") or None
_lock = threading.Lock()
_model = None
_error = None

try:
    from faster_whisper import WhisperModel  # type: ignore
except ImportError:  # paket yok: özellik kapalı
    WhisperModel = None


def available() -> bool:
    return WhisperModel is not None and _error is None


def _load():
    global _model, _error
    with _lock:
        if _model is None and _error is None:
            try:
                _model = WhisperModel(MODEL_NAME, device="cpu", compute_type="int8", download_root=CACHE_DIR)
            except Exception as e:  # indirilemedi / bozuk: kapalı say, nedeni logla
                _error = e
                logger.exception("whisper model %s could not be loaded", MODEL_NAME)
    return _model


def transcribe(audio: bytes, suffix: str = ".mp3") -> str:
    model = _load() if available() else None
    if model is None:
        raise RuntimeError("transcription is not available")
    # faster-whisper dosya yolu ister (ffmpeg / PyAV kapsayıcıyı uzantıdan değil içerikten çözer).
    with tempfile.NamedTemporaryFile(suffix=suffix) as f:
        f.write(audio)
        f.flush()
        segments, _ = model.transcribe(f.name, language="tr", beam_size=1, vad_filter=True,
                                       initial_prompt=PROMPT)
        return " ".join(s.text.strip() for s in segments).strip()

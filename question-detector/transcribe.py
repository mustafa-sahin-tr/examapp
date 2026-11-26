"""
#1219: ses kaydını yazıya çevirme (kitap aktarımında QR çözümlerinden cevap anahtarı).

faster-whisper isteğe bağlıdır: paket yoksa ya da model yüklenemezse `available()` False döner ve exam API "QR'dan al"ı
göstermez. Model `WHISPER_MODEL` (varsayılan `large-v3-turbo` (~1,6 GB; `small` Türkçe şık harflerinde zayıf); ilk kullanımda indirilir, `WHISPER_CACHE` dizinine), dil Türkçe.

Bellek: model ilk istekte yüklenir (~900 MB) ve `WHISPER_IDLE_SECONDS` (varsayılan 600; 0 = hiç bırakma) boyunca istek
gelmezse bırakılır; süren çeviri varken bırakılmaz. Sonraki istek modeli diskten yeniden yükler (birkaç saniye).
"""
import logging
import os
import tempfile
import threading
import time

import memory

logger = logging.getLogger(__name__)

MODEL_NAME = os.getenv("WHISPER_MODEL", "large-v3-turbo")
# Çözüm kayıtları "B seçeneğini işaretleyebiliriz" diye biter; ipucu modeli şık harflerini harf olarak yazmaya yöneltir
# ("de seçeneği" yerine "D seçeneği").
PROMPT = "Doğru cevap A seçeneği. B şıkkı. Cevabımız C seçeneğidir. D seçeneğini işaretleyebiliriz. E şıkkı."
CACHE_DIR = os.getenv("WHISPER_CACHE") or None
IDLE_SECONDS = int(os.getenv("WHISPER_IDLE_SECONDS", "600"))
_lock = threading.Lock()
_model = None
_error = None
_busy = 0
_last_used = 0.0
_reaper = None

try:
    from faster_whisper import WhisperModel  # type: ignore
except ImportError:  # paket yok: özellik kapalı
    WhisperModel = None


def available() -> bool:
    return WhisperModel is not None and _error is None


def _load_locked():
    global _model, _error
    if _model is None and _error is None:
        try:
            _model = WhisperModel(MODEL_NAME, device="cpu", compute_type="int8", download_root=CACHE_DIR)
            logger.info("whisper model %s loaded", MODEL_NAME)
        except Exception as e:  # indirilemedi / bozuk: kapalı say, nedeni logla
            _error = e
            logger.exception("whisper model %s could not be loaded", MODEL_NAME)
    return _model


def _acquire():
    global _busy
    with _lock:
        model = _load_locked()
        if model is not None:
            _busy += 1
        return model


def _release():
    global _busy, _last_used
    with _lock:
        _busy -= 1
        _last_used = time.monotonic()
    _start_reaper()


def loaded() -> bool:
    return _model is not None


def unload_if_idle(now=None) -> bool:
    """Model boşta ve süre dolduysa bırakır; bıraktıysa True."""
    global _model
    with _lock:
        now = time.monotonic() if now is None else now
        if _model is None or _busy or IDLE_SECONDS <= 0 or now - _last_used < IDLE_SECONDS:
            return False
        _model = None
    memory.trim()
    logger.info("whisper model %s unloaded after %d s idle", MODEL_NAME, IDLE_SECONDS)
    return True


def _start_reaper():
    global _reaper
    with _lock:
        if IDLE_SECONDS <= 0 or (_reaper is not None and _reaper.is_alive()):
            return
        _reaper = threading.Thread(target=_reap, name="whisper-idle", daemon=True)
        _reaper.start()


def _reap():
    while True:
        time.sleep(min(60, IDLE_SECONDS))
        if not loaded() or unload_if_idle():
            return


def transcribe(audio: bytes, suffix: str = ".mp3") -> str:
    model = _acquire() if available() else None
    if model is None:
        raise RuntimeError("transcription is not available")
    try:
        # faster-whisper dosya yolu ister (ffmpeg / PyAV kapsayıcıyı uzantıdan değil içerikten çözer).
        with tempfile.NamedTemporaryFile(suffix=suffix) as f:
            f.write(audio)
            f.flush()
            segments, _ = model.transcribe(f.name, language="tr", beam_size=1, vad_filter=True,
                                           initial_prompt=PROMPT)
            return " ".join(s.text.strip() for s in segments).strip()
    finally:
        _release()
        memory.trim()

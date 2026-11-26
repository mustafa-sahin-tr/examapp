"""
Boşalan belleği işletim sistemine geri verme.

Temizlik analizi (32 sayfanın medyanı), sayfa temizliği, Whisper ve YOLO büyük geçici diziler ayırır. glibc boşalan yığını
çoğu zaman sürece bağlı tutar (iş parçacığı başına ayrı arena); demoda servis işler bitince de ~3 GB'ta kalıyordu.
`trim()` çöp toplayıcıyı çalıştırır ve `malloc_trim(0)` ile boş sayfaları geri verir. glibc yoksa (macOS, musl) yalnız gc.
"""
import ctypes
import ctypes.util
import gc
import logging

logger = logging.getLogger(__name__)

try:
    _libc = ctypes.CDLL(ctypes.util.find_library("c") or "libc.so.6")
    _malloc_trim = _libc.malloc_trim
    _malloc_trim.argtypes = [ctypes.c_size_t]
    _malloc_trim.restype = ctypes.c_int
except (OSError, AttributeError):
    _malloc_trim = None


def trim() -> None:
    gc.collect()
    if _malloc_trim is not None:
        try:
            _malloc_trim(0)
        except Exception:  # yalnız bir iyileştirme; isteği düşürmez
            logger.debug("malloc_trim failed", exc_info=True)

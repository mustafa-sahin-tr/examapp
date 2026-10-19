"""Eğitim veri toplamaya başlamadan önce temiz başlangıç kontrolü (yalnız stdlib).

Repo kökünden:  python3 question-detector/training/check_clean_start.py
Hiçbir şeyi silmez/değiştirmez; sorun varsa ne yapılacağını yazar. Çıkış kodu: 0 = hazır, 1 = sorun var.
"""
import hashlib
import platform
import socket
import subprocess
import sys
from pathlib import Path

QD = Path(__file__).resolve().parent.parent  # question-detector/
REPO = QD.parent
LEGACY = ["data", "runs", "YOLOv8", "scripts", "prediction.json", "predictions.json", "yolov8n.pt"]
MODELS = ["question-best.pt", "answer-best.pt"]

problems = 0


def ok(msg):
    print(f"  [OK]   {msg}")


def bad(msg, fix=None):
    global problems
    problems += 1
    print(f"  [HATA] {msg}")
    if fix:
        print(f"         -> {fix}")


def info(msg):
    print(f"  [BİLGİ] {msg}")


def git(*args):
    r = subprocess.run(["git", "-C", str(REPO), *args], capture_output=True, text=True)
    return r.stdout.strip() if r.returncode == 0 else None


def size(path: Path) -> int:
    if path.is_file():
        return path.stat().st_size
    return sum(p.stat().st_size for p in path.rglob("*") if p.is_file())


print(f"\nquestion-detector: {QD}\n")

print("1) Kod")
branch = git("rev-parse", "--abbrev-ref", "HEAD")
info(f"branch: {branch}")
if (QD / "dataset_store.py").is_file() and (QD / "training" / "train.py").is_file():
    ok("yeni veri toplama kodu mevcut (dataset_store.py, training/train.py)")
else:
    bad("yeni kod yok", "git fetch && git checkout <eğitim-pipeline branch'i veya master> && git pull")
dirty = git("status", "--porcelain", "--", "question-detector")
if dirty:
    bad("question-detector altında commit edilmemiş değişiklik var:\n" + "\n".join("           " + l for l in dirty.splitlines()[:10]))
else:
    ok("question-detector çalışma ağacı temiz")

print("\n2) Eski artıklar (temiz başlangıç)")
leftovers = [n for n in LEGACY if (QD / n).exists()]
if leftovers:
    for n in leftovers:
        bad(f"eski artık duruyor: {n} ({size(QD / n) / 1e6:.1f} MB)")
    print(f"         -> silmek için: cd \"{QD}\" && rm -rf {' '.join(leftovers)}")
else:
    ok("eski data/runs/scripts artıkları yok")

print("\n3) Öğretmen modeller (models/)")
for name in MODELS:
    p = QD / "models" / name
    if p.is_file() and p.stat().st_size > 1_000_000:
        digest = hashlib.sha256(p.read_bytes()).hexdigest()[:12]
        ok(f"{name}  {p.stat().st_size / 1e6:.1f} MB  sha256:{digest}")
    else:
        bad(f"{name} yok veya bozuk", "git checkout -- question-detector/models (git-lfs değil, normal dosya)")

print("\n4) Veri seti (dataset/)")
ds = QD / "dataset"
pages = list((ds / "questions" / "images").glob("*.jpg")) if ds.exists() else []
crops = list((ds / "answers" / "images").glob("*.jpg")) if ds.exists() else []
if not ds.exists() or (not pages and not crops):
    ok("dataset/ boş - sıfırdan başlanıyor (ilk kayıtta oluşur)")
else:
    info(f"dataset/ dolu: {len(pages)} sayfa, {len(crops)} soru kırpması (bunlar birikmiş veri, silme!)")

print("\n5) Drive senkronu")
if platform.system() == "Darwin":
    drive_app = Path("/Applications/Google Drive.app").exists()
    (ok if drive_app else bad)("Google Drive for desktop " + ("kurulu" if drive_app else "kurulu değil"))
info("Drive for desktop > Tercihler > 'Bilgisayarım' altında şu klasörün yedeklendiğini doğrula:")
info(f"  {QD}  (Colab'da /content/drive/Othercomputers/<bilgisayar>/question-detector olarak görünür)")
info("Bu senkron bir AYNADIR: Mac'te silinen Drive'da da silinir. Kalıcı kopya eğitimde My Drive/hedefokul-ml'e alınır.")

print("\n6) Çalışma ortamı")
apphost = REPO / "AppHost" / "AppHost.cs"
if apphost.is_file() and '"../question-detector", "/app"' in apphost.read_text(encoding="utf-8"):
    ok("Aspire question-detector klasörünü /app'e bağlıyor (kayıtlar dataset/'e düşer)")
else:
    bad("AppHost.cs'de question-detector bind mount bulunamadı")
try:
    with socket.create_connection(("127.0.0.1", 8080), timeout=1):
        ok("question-detector :8080'de çalışıyor")
except OSError:
    info("question-detector :8080'de çalışmıyor (Aspire'ı başlatınca tekrar çalıştır)")

print()
if problems:
    print(f"SONUÇ: {problems} sorun var - yukarıdaki HATA satırlarını düzelt ve tekrar çalıştır.")
    sys.exit(1)
print("SONUÇ: Hazır. Soru işaretlemeye başlayabilirsin.")

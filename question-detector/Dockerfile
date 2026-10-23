# syntax=docker/dockerfile:1

FROM python:3.11-slim

# Sistem paketleri — yalnız gerçekten kullanılanlar, öneriler olmadan:
#   libgl1 + libglib2.0-0 → ultralytics'in çektiği opencv-python (libGL.so.1, libgthread)
#   libzbar0              → pyzbar (QR okuma)
# tesseract/mesa-dri/X11 paketleri kullanılmıyordu; --no-install-recommends olmadan
# ~120 MB indiriyordu ve yavaş Debian aynasında apt adımı tek başına ~8 dk sürüp
# Aspire'ın build timeout'una takılıyordu.
# apt/pip cache mount'ları: katman cache'i silinse bile (builder prune) indirilen
# paketler yeniden kullanılır; imaja girmezler.
RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \
    --mount=type=cache,target=/var/lib/apt/lists,sharing=locked \
    rm -f /etc/apt/apt.conf.d/docker-clean \
 && apt-get update \
 && apt-get install -y --no-install-recommends -o Acquire::Retries=3 \
      libgl1 \
      libglib2.0-0 \
      libzbar0

# Çalışma dizinine geç
WORKDIR /app

# requirements.txt dosyasını kopyala
COPY ./question-detector/requirements.txt .

# Python bağımlılıklarını yükle.
# torch/torchvision'ı önce CPU index'inden kur — aksi halde ultralytics
# ~2.5 GB'lık CUDA/nvidia wheel'lerini indirir ve build dakikalarca sürüp
# Aspire'ın build timeout'una takılır ("context canceled").
ENV PIP_DEFAULT_TIMEOUT=120 PIP_RETRIES=5 PIP_DISABLE_PIP_VERSION_CHECK=1
RUN --mount=type=cache,target=/root/.cache/pip \
    pip install \
      --index-url https://download.pytorch.org/whl/cpu \
      torch==2.5.1 torchvision==0.20.1 \
 && pip install -r requirements.txt

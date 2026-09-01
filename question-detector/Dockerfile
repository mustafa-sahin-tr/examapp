FROM python:3.11-slim

# Sistem paketlerini yükle
RUN apt update && apt install -y \
    tree \
    libgl1 \
    libglib2.0-0 \
    libgl1-mesa-dri \
    libsm6 \
    libxext6 \
    libxrender-dev \
    libzbar0 \
    tesseract-ocr \
    tesseract-ocr-tur \
    && apt clean && rm -rf /var/lib/apt/lists/*

# Çalışma dizinine geç
WORKDIR /app

# requirements.txt dosyasını kopyala
COPY ./question-detector/requirements.txt .

# Python bağımlılıklarını yükle.
# torch/torchvision'ı önce CPU index'inden kur — aksi halde ultralytics
# ~2.5 GB'lık CUDA/nvidia wheel'lerini indirir ve build dakikalarca sürüp
# Aspire'ın build timeout'una takılır ("context canceled").
RUN pip install --no-cache-dir --upgrade pip \
 && pip install --no-cache-dir \
      --index-url https://download.pytorch.org/whl/cpu \
      torch==2.5.1 torchvision==0.20.1 \
 && pip install --no-cache-dir -r requirements.txt

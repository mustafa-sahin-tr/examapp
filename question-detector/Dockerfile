FROM python:3.11-slim

# Sistem paketlerini yükle
RUN apt update && apt install -y \
    tree \
    libglib2.0-0 \
    libgl1 \
    libsm6 \
    libxext6 \
    libgl1-mesa-glx \
    libglib2.0-0 \
    libxrender-dev \
    && apt clean && rm -rf /var/lib/apt/lists/*

# Çalışma dizinine geç
WORKDIR /app

# requirements.txt dosyasını kopyala
COPY ./question-detector/app/requirements.txt .

# Python bağımlılıklarını yükle
RUN pip install --upgrade pip && pip install -r requirements.txt

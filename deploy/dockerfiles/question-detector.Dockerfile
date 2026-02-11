FROM python:3.11-slim

RUN apt-get update && apt-get install -y --no-install-recommends \
    libgl1 \
    libglib2.0-0 \
    libgl1-mesa-dri \
    libsm6 \
    libxext6 \
    libxrender-dev \
    libzbar0 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY question-detector/requirements.prod.txt ./requirements.txt
RUN pip install --no-cache-dir --upgrade pip && pip install --no-cache-dir -r requirements.txt

RUN mkdir -p /app/models
COPY question-detector/main.py ./main.py

# Only ship the weights actually used by the API.
COPY question-detector/data/questions/runs/train20251206-22/weights/best.pt /app/models/question-best.pt
COPY question-detector/data/answers/runs/train-answers-v11/weights/best.pt /app/models/answer-best.pt

ENV PYTHONDONTWRITEBYTECODE=1
ENV PYTHONUNBUFFERED=1

EXPOSE 8080
CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8080"]

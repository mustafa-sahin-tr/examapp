from ultralytics import YOLO
import os
import json
from PIL import Image

# Modeli yükle
model = YOLO("runs/detect/train/weights/best.pt")

# Test edilecek görsel
image_path = "data/images/3.webp"  # <- buraya kendi görselini yaz

# Görsel boyutlarını al
img = Image.open(image_path)
img_width, img_height = img.size

# Tahmin yap
results = model.predict(source=image_path, conf=0.09, save=False)

# Sonuçları işleyip JSON formatına dönüştür
output = []
for r in results:
    for box in r.boxes:
        cls_id = int(box.cls[0])
        x_center, y_center, w, h = box.xywh[0].tolist()

        # Normalize edilmiş değerleri gerçek piksellere çevir
        x = (x_center - w / 2) * img_width
        y = (y_center - h / 2) * img_height
        width = w * img_width
        height = h * img_height

        output.append({
            "class_id": cls_id,
            "x": round(x),
            "y": round(y),
            "width": round(width),
            "height": round(height)
        })

# Sonucu yazdır
json_path = os.path.splitext(image_path)[0] + ".json"
with open(json_path, "w", encoding="utf-8") as f:
    json.dump(output, f, indent=2, ensure_ascii=False)

print(f"✅ JSON çıktı üretildi: {json_path}")

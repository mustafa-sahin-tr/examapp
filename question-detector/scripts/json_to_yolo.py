import os
import json
from PIL import Image
from tqdm import tqdm

# Girdi ve çıktı yolları
JSON_PATH = "/app/data/json/questionsv2.json"
IMAGE_DIR = "/app/data/images"     
LABEL_DIR = "/app/data/labels"

# Her nesneye class ID atayalım
CLASS_QUESTION = 0
CLASS_ANSWER = 1

# Çıktı klasörünü oluştur
os.makedirs(LABEL_DIR, exist_ok=True)

# JSON dosyasını yükle
with open(JSON_PATH, "r", encoding="utf-8") as f:
    data = json.load(f)

# Görsel bazlı gruplama için dict oluştur
images = {}

# Her soruyu işle
for item in tqdm(data):
    q = item["question"]
    image_name = os.path.basename(q["imageUrl"]).replace("..", "").replace("\\", "/").split("/")[-1]

    if image_name not in images:
        images[image_name] = []

    # Soru kutusunu ekle (class_id = 0)
    images[image_name].append({
        "class_id": CLASS_QUESTION,
        "x": q["x"],
        "y": q["y"],
        "width": q["width"],
        "height": q["height"]
    })

    # Şıkları da ekle (class_id = 1)
    for a in q["answers"]:
        images[image_name].append({
            "class_id": CLASS_ANSWER,
            "x": a["x"],
            "y": a["y"],
            "width": a["width"],
            "height": a["height"]
        })

# Şimdi her görsel için YOLO formatı üretelim
for image_name, boxes in tqdm(images.items()):
    image_path = os.path.join(IMAGE_DIR, image_name)

    if not os.path.exists(image_path):
        print(f"[Uyarı] Görsel bulunamadı: {image_path}")
        continue

    # Görselin boyutlarını al
    with Image.open(image_path) as img:
        img_width, img_height = img.size

    # .txt dosyasını yaz
    label_file = os.path.join(LABEL_DIR, image_name.replace(".jpg", ".txt").replace(".png", ".txt").replace(".webp", ".txt"))
    with open(label_file, "w") as f:
        for box in boxes:
            x_center = (box["x"] + box["width"] / 2) / img_width
            y_center = (box["y"] + box["height"] / 2) / img_height
            width = box["width"] / img_width
            height = box["height"] / img_height

            line = f"{box['class_id']} {x_center:.6f} {y_center:.6f} {width:.6f} {height:.6f}\n"
            f.write(line)

print("✅ Dönüştürme tamamlandı. YOLO label dosyaları 'data/labels/' klasörüne yazıldı.")

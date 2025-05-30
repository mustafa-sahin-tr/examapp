import os
import json
from PIL import Image
from collections import defaultdict

# 📁 Dosya yolları
JSON_PATH = "/content/drive/Othercomputers/PersonalMacBookPro/app/data/json/questions.json"
IMAGES_FOLDER = "/content/drive/Othercomputers/PersonalMacBookPro/app/data/questions/images"
LABELS_FOLDER = "/content/drive/Othercomputers/PersonalMacBookPro/app/data/questions/labels"

# 📁 Çıktı klasörünü oluştur
os.makedirs(LABELS_FOLDER, exist_ok=True)

# 📖 JSON dosyasını yükle
with open(JSON_PATH, "r", encoding="utf-8") as f:
    data = json.load(f)

# 📦 Görselleri grupla: image_name → [kutular]
grouped = defaultdict(list)

for item in data:
    q = item["question"]
    image_name = os.path.basename(q["imageUrl"])
    grouped[image_name].append(q)

print(f"📦 Toplam {len(grouped)} görsel bulundu.")

# 🔁 Her görsel için tüm kutuları işle
for image_name, questions in grouped.items():
    image_path = os.path.join(IMAGES_FOLDER, image_name)

    try:
        with Image.open(image_path) as img:
            img_width, img_height = img.size
    except Exception as e:
        print(f"⚠️ Resim açılamadı: {image_name} -> {e}")
        continue

    label_lines = []

    for q in questions:
        x_center = (q["x"] + q["width"] / 2) / img_width
        y_center = (q["y"] + q["height"] / 2) / img_height
        w = q["width"] / img_width
        h = q["height"] / img_height

        class_id = 0  # question
        line = f"{class_id} {x_center:.6f} {y_center:.6f} {w:.6f} {h:.6f}"
        label_lines.append(line)

    # 🔄 Tek seferde dosyaya tüm satırları yaz
    txt_path = os.path.join(LABELS_FOLDER, os.path.splitext(image_name)[0] + ".txt")
    with open(txt_path, "w") as f:
        f.write("\n".join(label_lines) + "\n")

    print(f"✅ {image_name} için {len(label_lines)} adet kutu yazıldı.")

print("🎉 Tüm YOLO etiketleri başarıyla oluşturuldu!")
print(f"📦 Toplam {len(grouped)} görsel bulundu.")  


# !yolo detect train data=/content/examapp/train/data/dataset-answers.yaml model=/content/examapp/train/data/answers/runs/train-answers-v1/weights/epoch2.pt epochs=50 imgsz=640 resume=True name=train-answers-v1 project=/content/drive/MyDrive/examapp/train/data/answers/runs




#!yolo detect train data=/content/examapp/train/data/dataset-answers.yaml model=/content/examapp/train/data/answers/runs/train-answers-v1/weights/epoch2.pt epochs=50 imgsz=640 resume=True name=train-answers-v1 project=/content/drive/MyDrive/examapp/train/data/answers/runs
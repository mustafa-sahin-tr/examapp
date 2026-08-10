import os
import json
from PIL import Image
from collections import defaultdict

# 📁 Dosya yolları
JSON_PATH = "/content/drive/Othercomputers/PersonalMacBookPro/question-detector/data/json/answers.json"
IMAGES_FOLDER = "/content/drive/Othercomputers/PersonalMacBookPro/question-detector/data/answers/images"
LABELS_FOLDER = "/content/drive/Othercomputers/PersonalMacBookPro/question-detector/data/answers/labels"

# 📁 Çıktı klasörünü oluştur
os.makedirs(LABELS_FOLDER, exist_ok=True)

# 📖 JSON dosyasını yükle
with open(JSON_PATH, "r", encoding="utf-8") as f:
    data = json.load(f)

# 📦 Görselleri grupla: image_name → [kutular]
grouped = defaultdict(list)
image_to_items = defaultdict(list)  # hangi image hangi item'larla eşleşiyor

for item in data:
    q = item["question"]
    image_name = os.path.basename(q["imageUrl"])
    grouped[image_name].append(q)
    image_to_items[image_name].append(item)

print(f"📦 Toplam {len(grouped)} görsel bulundu.")

# ✅ Yeni json verisi için liste
new_json_data = []

# 🔁 Her görsel için tüm kutuları işle
for image_name, questions in grouped.items():
    image_path = os.path.join(IMAGES_FOLDER, image_name)
    label_path = os.path.join(LABELS_FOLDER, os.path.splitext(image_name)[0] + ".txt")

    try:
        with Image.open(image_path) as img:
            img_width, img_height = img.size
    except Exception as e:
        print(f"⚠️ Resim açılamadı: {image_name} -> {e}")
        continue

    if len(questions) == 4:
        label_lines = []
        for q in questions:        
            x_center = (q["x"] + q["width"] / 2) / img_width
            y_center = (q["y"] + q["height"] / 2) / img_height
            w = q["width"] / img_width
            h = q["height"] / img_height

            class_id = 0  # question
            line = f"{class_id} {x_center:.6f} {y_center:.6f} {w:.6f} {h:.6f}"
            label_lines.append(line)

        with open(label_path, "w") as f:
            f.write("\n".join(label_lines) + "\n")

        print(f"✅ {image_name} için {len(label_lines)} kutu yazıldı.")

        # ✅ Bu image'a ait item'ları yeni JSON'a ekle
        new_json_data.extend(image_to_items[image_name])

    else:
        try:
            os.remove(image_path)
            print(f"🗑️ {image_name} silindi (3 kutu yoktu).")
        except Exception as e:
            print(f"❌ Resim silinemedi: {image_name} -> {e}")
        
        # Label da varsa onu da sil
        if os.path.exists(label_path):
            try:
                os.remove(label_path)
                print(f"🗑️ Label dosyası da silindi: {label_path}")
            except Exception as e:
                print(f"❌ Label silinemedi: {label_path} -> {e}")

# 💾 Yeni json dosyasını kaydet
with open(JSON_PATH, "w", encoding="utf-8") as f:
    json.dump(new_json_data, f, ensure_ascii=False, indent=2)

print("🎉 Etiketleme ve temizlik tamamlandı. JSON da güncellendi.")
print(f"🧹 Kalan geçerli görsel sayısı: {len(new_json_data)}")

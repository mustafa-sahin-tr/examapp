


from ultralytics import YOLO
from PIL import Image
import json

model = YOLO("runs/detect/train/weights/best.pt")

# Test edilecek görsel
image_path = "data/images/04a3609b-8a98-4849-949d-32c14a6d72c1.jpg"  # <- buraya kendi görselini yaz


image = Image.open(image_path)
original_width, original_height = image.size

results = model.predict(
    source=image_path,
    conf=0.09,
    save=False,
    save_txt=False,
    save_crop=False
)

output = []

for box in results[0].boxes:
    # box.xyxy[0] → [x1, y1, x2, y2]
    x1, y1, x2, y2 = box.xyxy[0].tolist()
    class_id = int(box.cls[0].item())

    x = int(x1)
    y = int(y1)
    width = int(x2 - x1)
    height = int(y2 - y1)

    output.append({
        "class_id": class_id,
        "x": x,
        "y": y,
        "width": width,
        "height": height
    })

with open("predictions.json", "w") as f:
    json.dump(output, f, indent=2)

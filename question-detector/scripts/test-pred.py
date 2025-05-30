from ultralytics import YOLO
import json

model = YOLO("/app/runs/detect/only-questions-exp/weights/best.pt")
results = model.predict(
    source="/app/data/images/0b0faf33-3567-4c93-8eb3-40baa476e64d.jpg",
    conf=0.01,
    save=False,
    save_txt=False,
    save_crop=False,
    imgsz=640
)

output = []
for box in results[0].boxes:
    class_id = int(box.cls.item())
    x_center, y_center, w, h = box.xywh[0].tolist()
    output.append({
        "class_id": class_id,
        "x": x_center,
        "y": y_center,
        "width": w,
        "height": h
    })

with open("prediction.json", "w") as f:
    json.dump(output, f, indent=2)

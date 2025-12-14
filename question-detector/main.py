from fastapi import FastAPI, File, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
from typing import List
from pathlib import Path
from ultralytics import YOLO
from PIL import Image
import base64
import io
from io import BytesIO
import uuid
import json
import os
import logging
from pyzbar.pyzbar import decode
app = FastAPI()


# Basit log yapılandırması (konsola yazdırır)
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s"
)

logger = logging.getLogger(__name__)

IMAGES_DIR = "data/questions/images"
ANSWERS_DIR = "data/answers/images"
QUESTIONS_JSON_PATH = "data/json/questions.json"
ANSWERS_JSON_PATH = "data/json/answers.json"
QUESTION_IMAGE_URL_PREFIX = "../data/questions/images"
ANSWER_IMAGE_URL_PREFIX = "../data/answers/images"
CROPS_DIR = Path("data/crops")

# Ensure dirs exist
os.makedirs(IMAGES_DIR, exist_ok=True)
Path(QUESTIONS_JSON_PATH).parent.mkdir(parents=True, exist_ok=True)
if not os.path.exists(QUESTIONS_JSON_PATH):
    with open(QUESTIONS_JSON_PATH, "w") as f:
        json.dump([], f)

# Request body model
class ImageData(BaseModel):
    image_base64: str

class AnswerBox(BaseModel):
    x: float
    y: float
    width: float
    height: float

class QuestionBox(BaseModel):
    x: float
    y: float
    width: float
    height: float
    answers: List[AnswerBox]    

class UploadQuestionsRequest(BaseModel):
    answerCount: int
    imageData: ImageData
    questions: List[QuestionBox]


# CORS (gerekirse frontend için aç)
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Modeli yükle (model yolunu değiştir)
# model = YOLO("runs/detect/train-only-q-v5/weights/best.pt")
# model = YOLO("runs/detect/train2/weights/best.pt")
# model = YOLO("data/questions/runs/train32/weights/best.pt")
# sub_model = YOLO("data/answers/runs/train-answers-v4/weights/best.pt")  # <--- Alt modelin yolu
# model = YOLO("data/questions/runs/train5/weights/best.pt")
# sub_model = YOLO("data/answers/runs/train-answers-v10/weights/best.pt")  # <--- Alt modelin yolu

model = YOLO("data/questions/runs/train20251206-22/weights/best.pt")
sub_model = YOLO("data/answers/runs/train-answers-v11/weights/best.pt")  # <--- Alt modelin yolu



class ImageData(BaseModel):
    image_base64: str


def upload_answers_logic(payload: UploadQuestionsRequest) -> dict:
    try:
        # 👇 Base64 temizleme
        base64_str = payload.imageData.image_base64
        if "," in base64_str:
            base64_str = base64_str.split(",")[1]

        # Görseli decode et ve kaydet
        image_bytes = base64.b64decode(base64_str)
        image = Image.open(BytesIO(image_bytes)).convert("RGB")

        filename = f"{uuid.uuid4()}.jpg"
        image_path = os.path.join(ANSWERS_DIR, filename)
        image.save(image_path)

        image_url = f"{ANSWER_IMAGE_URL_PREFIX}/{filename}"
        logger.info(f"Image Added : {image_url}")
        # Yeni question objelerini oluştur
        new_entries = []
        for idx, q in enumerate(payload.questions):
            entry = {
                "question": {
                    "x": q.x,
                    "y": q.y,
                    "width": q.width,
                    "height": q.height,
                    "imageUrl": image_url
                }
            }

            new_entries.append(entry)
            logger.info(f"Added question: {entry['question']}")

        # Mevcut questions.json dosyasına ekle
        with open(ANSWERS_JSON_PATH, "r+", encoding="utf-8") as f:
            current_data = json.load(f)
            current_data.extend(new_entries)
            f.seek(0)
            json.dump(current_data, f, indent=2, ensure_ascii=False)
            f.truncate()

        logger.info(f"Successfully appended {len(new_entries)} answers to {ANSWERS_JSON_PATH}")


        return {
            "success": True,
            "added": len(new_entries),
            "imageFile": filename
        }

    except Exception as e:
        logger.error(f"Error occurred: {str(e)}")
        raise HTTPException(status_code=500, detail=str(e))  

@app.post("/predict")
def predict(data: ImageData):
    try:
        # Base64 başlığını ayıkla (data:image/...;base64, varsa)
        base64_data = data.image_base64
        if base64_data.startswith("data:"):
            base64_data = base64_data.split(",", 1)[1]
        # Base64'ten image oluştur
        image_bytes = base64.b64decode(base64_data)
        image = Image.open(io.BytesIO(image_bytes)).convert("RGB")
        logger.info(f"Received image with shape: {image.size}")
        # Predict et
        results = model.predict(
            source=image,
            conf=0.25,
            save=False,
            save_txt=False,
            save_crop=False,
            verbose=False
        )

        result = results[0]
        boxes = result.boxes
        width, height = result.orig_shape[1], result.orig_shape[0]
        logger.info(f"Detected {len(boxes)} objects")
        predictions = []

        for box in boxes:
            x1, y1, x2, y2 = box.xyxy[0].tolist()
            class_id = int(box.cls[0].item())

            prediction_data = {
                "class_id": class_id,
                "x": int(x1),
                "y": int(y1),
                "width": int(x2 - x1),
                "height": int(y2 - y1),
            }

            # Eğer class_id == 0 ise crop edip sub prediction al
            if class_id == 0:
                cropped = image.crop((x1, y1, x2, y2))
                sub_results = sub_model.predict(
                    source=cropped,
                    conf=0.25,
                    save=False,
                    save_txt=False,
                    save_crop=False,
                    verbose=False
                )
                sub_result = sub_results[0]
                sub_boxes = sub_result.boxes

                subpredictions = []
                for sbox in sub_boxes:
                    sx1, sy1, sx2, sy2 = sbox.xyxy[0].tolist()
                    sclass_id = int(sbox.cls[0].item())

                    # Alt kutuların koordinatlarını ana resme göre ayarla
                    subpredictions.append({
                        "class_id": sclass_id,
                        "x": int(sx1 + x1),
                        "y": int(sy1 + y1),
                        "width": int(sx2 - sx1),
                        "height": int(sy2 - sy1),
                    })

                prediction_data["subpredictions"] = subpredictions

            predictions.append(prediction_data)

        return {"success": True, "predictions": predictions}

    except Exception as e:
        return {"success": False, "error": str(e)}


@app.post("/send-to-fix")
def upload_questions(payload: UploadQuestionsRequest):
    try:
        # ❗️Ön kontrol: answerCount ile eşleşmeyen var mı?
        invalid_questions = [q for q in payload.questions if len(q.answers) != payload.answerCount]
        if invalid_questions:
            raise HTTPException(
                status_code=400,
                detail=f"{len(invalid_questions)} question(s) have answer count mismatch. Expected {payload.answerCount}."
            )
        # 👇 Base64 temizleme
        base64_str = payload.imageData.image_base64
        if "," in base64_str:
            base64_str = base64_str.split(",")[1]

        # Görseli decode et ve kaydet
        image_bytes = base64.b64decode(base64_str)
        image = Image.open(BytesIO(image_bytes)).convert("RGB")

        filename = f"{uuid.uuid4()}.jpg"
        image_path = os.path.join(IMAGES_DIR, filename)
        image.save(image_path)

        image_url = f"{QUESTION_IMAGE_URL_PREFIX}/{filename}"
        logger.info(f"Image Added : {image_url}")
        crops = []
        # Yeni question objelerini oluştur
        new_entries = []
        for idx, q in enumerate(payload.questions):
            entry = {
                "question": {
                    "x": q.x,
                    "y": q.y,
                    "width": q.width,
                    "height": q.height,
                    "imageUrl": image_url
                }
            }

            left = q.x
            upper = q.y
            right = q.x + q.width
            lower = q.y + q.height

            crop = image.crop((left, upper, right, lower))

            crop_filename = f"{filename}_q{idx + 1}.jpg"
            crop_path = CROPS_DIR / crop_filename
            crop.save(crop_path)

            crops.append(str(crop_path))

            # Eğer answer varsa: crop edilmiş görüntüyle birlikte yeni payload oluştur ve gönder
            if q.answers:
                # Crop image to base64
                with BytesIO() as buffer:
                    crop.save(buffer, format="JPEG")
                    crop_base64 = base64.b64encode(buffer.getvalue()).decode()

                # AnswerBox -> QuestionBox çevir
                converted_questions = [
                    QuestionBox(x=a.x - q.x, y=a.y - q.y, width=a.width, height=a.height, answers=[])
                    for a in q.answers
                ]                

                base64_with_prefix = f"data:image/webp;base64,{crop_base64}"
                new_payload = UploadQuestionsRequest(   
                    imageData={"image_base64":base64_with_prefix},
                    questions=converted_questions,
                    answerCount=payload.answerCount
                )

                result = upload_answers_logic(new_payload)
                logger.info(f"Sub-answers added: {result}")

            new_entries.append(entry)
            logger.info(f"Added question: {entry['question']}")

        # Mevcut questions.json dosyasına ekle
        with open(QUESTIONS_JSON_PATH, "r+", encoding="utf-8") as f:
            current_data = json.load(f)
            current_data.extend(new_entries)
            f.seek(0)
            json.dump(current_data, f, indent=2, ensure_ascii=False)
            f.truncate()

        logger.info(f"Successfully appended {len(new_entries)} questions to {QUESTIONS_JSON_PATH}")


        return {
            "success": True,
            "added": len(new_entries),
            "imageFile": filename,
            "crops": crops
        }

    except Exception as e:
        logger.error(f"Error occurred: {str(e)}")
        raise HTTPException(status_code=500, detail=str(e))
    

@app.post("/send-to-fix-for-answers")
def upload_answers(payload: UploadQuestionsRequest):
    return upload_answers_logic(payload)


@app.post("/read-qr")
async def read_qr_code(request: ImageData):
    try:
        base64_data = request.image_base64
        if base64_data.startswith("data:"):
            base64_data = base64_data.split(",", 1)[1]
        # Base64'ten image oluştur
        image_bytes = base64.b64decode(base64_data)
        image = Image.open(io.BytesIO(image_bytes)).convert("RGB")

        # Base64 decode
        # image_data = base64.b64decode(request.image_base64)
        # image = Image.open(BytesIO(image_data))

        # QR kodları çöz
        qr_codes = decode(image)

        if not qr_codes:
            raise HTTPException(status_code=404, detail="QR kod bulunamadı.")

        # İlk QR kodun verisini döndür
        qr_data = qr_codes[0].data.decode('utf-8')
        return {"qr_data": qr_data}

    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Hata oluştu: {str(e)}")
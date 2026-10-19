from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel, Field
from typing import List
from pathlib import Path
from ultralytics import YOLO
from PIL import Image
import base64
from io import BytesIO
import os
import logging
from pyzbar.pyzbar import decode
from guards import (
    BodySizeLimitMiddleware,
    MAX_ANSWERS_PER_QUESTION,
    MAX_COORD,
    MAX_QUESTIONS_PER_REQUEST,
)
from dataset_store import QuestionSample, save_page

_enable_docs = os.getenv("ENABLE_API_DOCS", "").lower() in ("1", "true", "yes")
app = FastAPI(
    docs_url="/docs" if _enable_docs else None,
    redoc_url="/redoc" if _enable_docs else None,
    openapi_url="/openapi.json" if _enable_docs else None,
)


# Basit log yapılandırması (konsola yazdırır)
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s"
)

logger = logging.getLogger(__name__)

# Request body model
class ImageData(BaseModel):
    image_base64: str

# Issue #366: koordinatlar sonlu (nan/inf reddedilir: ge/le), >=0 ve makul ust sinirli.
Coord = Field(ge=0, le=MAX_COORD)


class AnswerBox(BaseModel):
    x: float = Coord
    y: float = Coord
    width: float = Coord
    height: float = Coord

class QuestionBox(BaseModel):
    x: float = Coord
    y: float = Coord
    width: float = Coord
    height: float = Coord
    answers: List[AnswerBox]    

class UploadQuestionsRequest(BaseModel):
    answerCount: int
    imageData: ImageData
    questions: List[QuestionBox]
    # False: sayfadaki sorularin hepsi isaretli degil -> tam sayfa soru modeline girmez (dataset_store).
    pageComplete: bool = True


# Issue #366: servis yalnız gateway arkasindan cagrilir (tarayici dogrudan gelmez); CORS varsayilan
# KAPALI. Gerekirse CORS_ALLOWED_ORIGINS (virgullu, acik origin listesi) ile acilir; "*" + credentials yok.
_cors_origins = [o.strip() for o in os.getenv("CORS_ALLOWED_ORIGINS", "").split(",") if o.strip() and o.strip() != "*"]
if _cors_origins:
    app.add_middleware(
        CORSMiddleware,
        allow_origins=_cors_origins,
        allow_credentials=True,
        allow_methods=["GET", "POST", "OPTIONS"],
        allow_headers=["Authorization", "Content-Type"],
    )

# Govde boyut siniri (413) - tum uclar (predict/read-qr/send-to-fix*).
app.add_middleware(BodySizeLimitMiddleware)

# Modeller: models/question-best.pt (sayfa -> soru) ve models/answer-best.pt (soru kirpmasi -> sik).
# Haftalik egitim (training/train.py) yeni modeli bu adlarla models/ altina yazar; ortam degiskeni ezer.
MODELS_DIR = Path(os.getenv("MODELS_DIR", "models"))
QUESTION_MODEL_PATH = os.getenv("QUESTION_MODEL_PATH") or str(MODELS_DIR / "question-best.pt")
ANSWER_MODEL_PATH = os.getenv("ANSWER_MODEL_PATH") or str(MODELS_DIR / "answer-best.pt")

model = YOLO(QUESTION_MODEL_PATH)
sub_model = YOLO(ANSWER_MODEL_PATH)
logger.info(f"Models loaded: question={QUESTION_MODEL_PATH} answer={ANSWER_MODEL_PATH}")


def validate_upload_limits(payload: UploadQuestionsRequest) -> None:
    # Issue #366: sayi siniri - tek istekle sinirsiz crop/kayit uretilmesin.
    if len(payload.questions) > MAX_QUESTIONS_PER_REQUEST:
        raise HTTPException(status_code=422, detail=f"Too many questions (max {MAX_QUESTIONS_PER_REQUEST}).")
    if any(len(q.answers) > MAX_ANSWERS_PER_QUESTION for q in payload.questions):
        raise HTTPException(status_code=422, detail=f"Too many answers per question (max {MAX_ANSWERS_PER_QUESTION}).")


@app.post("/predict")
def predict(data: ImageData):
    try:
        # Base64 başlığını ayıkla (data:image/...;base64, varsa)
        base64_data = data.image_base64
        if base64_data.startswith("data:"):
            base64_data = base64_data.split(",", 1)[1]
        # Base64'ten image oluştur
        image_bytes = base64.b64decode(base64_data)
        image = Image.open(BytesIO(image_bytes)).convert("RGB")
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

    except Exception:
        logger.exception("predict failed")
        return {"success": False, "error": "Prediction failed"}


@app.post("/send-to-fix")
def upload_questions(payload: UploadQuestionsRequest):
    """Elle dogrulanmis sayfayi egitim veri setine yazar (DATASET_DIR, bkz. dataset_store)."""
    validate_upload_limits(payload)
    # Ön kontrol: answerCount ile eşleşmeyen var mı? (try dışında: 400 olarak kalsın)
    invalid_questions = [q for q in payload.questions if len(q.answers) != payload.answerCount]
    if invalid_questions:
        raise HTTPException(
            status_code=400,
            detail=f"{len(invalid_questions)} question(s) have answer count mismatch. Expected {payload.answerCount}."
        )
    try:
        base64_str = payload.imageData.image_base64
        if "," in base64_str:
            base64_str = base64_str.split(",")[1]
        image_bytes = base64.b64decode(base64_str)

        samples = [
            QuestionSample(
                box=(q.x, q.y, q.width, q.height),
                answers=[(a.x, a.y, a.width, a.height) for a in q.answers],
            )
            for q in payload.questions
        ]
        try:
            saved = save_page(image_bytes, samples, page_complete=payload.pageComplete)
        except ValueError:
            raise HTTPException(status_code=400, detail="Box is outside the image.")

        logger.info(
            f"Training sample saved: page={saved['page_id']} complete={payload.pageComplete} "
            f"questions={len(samples)} crops={len(saved['crops'])} skipped_crops={saved['skipped_crops']}"
        )
        return {
            "success": True,
            "added": len(samples),
            "imageFile": saved["page_id"],
            "crops": saved["crops"],
        }

    except HTTPException:
        raise
    except Exception:
        logger.exception("Error occurred")
        raise HTTPException(status_code=500, detail="Internal error")


@app.post("/read-qr")
async def read_qr_code(request: ImageData):
    try:
        base64_data = request.image_base64
        if base64_data.startswith("data:"):
            base64_data = base64_data.split(",", 1)[1]
        # Base64'ten image oluştur
        image_bytes = base64.b64decode(base64_data)
        image = Image.open(BytesIO(image_bytes)).convert("RGB")

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

    except HTTPException:
        raise
    except Exception:
        logger.exception("read-qr failed")
        raise HTTPException(status_code=500, detail="QR okuma hatasi")
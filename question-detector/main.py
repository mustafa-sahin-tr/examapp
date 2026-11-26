from fastapi import FastAPI, HTTPException, Request, Response
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel, Field
from typing import List
from pathlib import Path
from PIL import Image, UnidentifiedImageError
import base64
import json
from io import BytesIO
import os
import logging
import threading
from pyzbar.pyzbar import decode
from guards import (
    BodySizeLimitMiddleware,
    MAX_ANSWERS_PER_QUESTION,
    MAX_COORD,
    MAX_QUESTIONS_PER_REQUEST,
    SUBMITTED_BY_HEADER,
    has_free_disk,
    submitted_by,
)
from dataset_store import (
    QuestionSample,
    dataset_dir,
    decode_base64_image,
    labeled_page_ids,
    open_oriented_rgb,
    save_page,
)
import memory
import transcribe
import page_clean

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

# Bellek: torch + iki model ~300 MB; yalnız /predict (işaretleme ekranındaki YOLO tespiti) ister. İlk istekte yüklenir; temizlik
# ve ses uçlarını kullanan süreç torch'u hiç yüklemez.
_yolo_lock = threading.Lock()
_yolo = None


def yolo_models():
    global _yolo
    with _yolo_lock:
        if _yolo is None:
            from ultralytics import YOLO
            _yolo = (YOLO(QUESTION_MODEL_PATH), YOLO(ANSWER_MODEL_PATH))
            logger.info(f"Models loaded: question={QUESTION_MODEL_PATH} answer={ANSWER_MODEL_PATH}")
        return _yolo


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
        image_bytes = decode_base64_image(data.image_base64)
        image = open_oriented_rgb(image_bytes)
        logger.info(f"Received image with shape: {image.size}")
        model, sub_model = yolo_models()
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
                # Kutunun güveni (0-1): hibrit tespitin kabul kuralı eşik olarak kullanır (issue #1250).
                "confidence": round(float(box.conf[0].item()), 4),
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
                        "confidence": round(float(sbox.conf[0].item()), 4),
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
    finally:
        memory.trim()


@app.post("/send-to-fix")
def upload_questions(payload: UploadQuestionsRequest, request: Request):
    """Elle dogrulanmis sayfayi egitim veri setine yazar (DATASET_DIR, bkz. dataset_store)."""
    validate_upload_limits(payload)
    # Ön kontrol: answerCount ile eşleşmeyen var mı? (try dışında: 400 olarak kalsın)
    invalid_questions = [q for q in payload.questions if len(q.answers) != payload.answerCount]
    if invalid_questions:
        raise HTTPException(
            status_code=400,
            detail=f"{len(invalid_questions)} question(s) have answer count mismatch. Expected {payload.answerCount}."
        )
    # Issue #461: disk dolmadan önce dur (veri seti host'a bağlı ve Drive'a aynalanıyor).
    if not has_free_disk(dataset_dir()):
        logger.warning("send-to-fix rejected: low disk space")
        raise HTTPException(status_code=507, detail="Not enough storage for training data.")
    try:
        try:
            image_bytes = decode_base64_image(payload.imageData.image_base64)
        except (ValueError, IndexError):
            raise HTTPException(status_code=400, detail="Invalid image data.")

        samples = [
            QuestionSample(
                box=(q.x, q.y, q.width, q.height),
                answers=[(a.x, a.y, a.width, a.height) for a in q.answers],
            )
            for q in payload.questions
        ]
        try:
            saved = save_page(
                image_bytes,
                samples,
                page_complete=payload.pageComplete,
                submitted_by=submitted_by(request.headers.get(SUBMITTED_BY_HEADER)),
            )
        except ValueError:
            raise HTTPException(status_code=400, detail="Box is outside the image.")
        except (UnidentifiedImageError, Image.DecompressionBombError):
            raise HTTPException(status_code=400, detail="Unsupported or too large image.")

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


@app.get("/labeled-pages")
def labeled_pages():
    """Egitim veri setine kaydedilmis sayfa id'leri (sha256(gorsel)[:16]); isaretleme modu bunlari atlar."""
    try:
        return {"pageIds": labeled_page_ids()}
    except Exception:
        logger.exception("labeled-pages failed")
        raise HTTPException(status_code=500, detail="Internal error")


@app.post("/read-qr")
async def read_qr_code(request: ImageData):
    try:
        image_bytes = decode_base64_image(request.image_base64)
        image = open_oriented_rgb(image_bytes)

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

# #1219: kitap aktarımında QR çözümlerinin ses kayıtları (exam API çağırır). faster-whisper yoksa available=false.
@app.get("/transcribe/status")
async def transcribe_status():
    return {"available": transcribe.available(), "model": transcribe.MODEL_NAME}


@app.post("/transcribe")
async def transcribe_audio(request: Request):
    if not transcribe.available():
        raise HTTPException(status_code=503, detail="transcription is not available")
    audio = await request.body()
    if not audio:
        raise HTTPException(status_code=400, detail="empty audio")
    suffix = ".mp4" if request.headers.get("content-type", "").startswith("video/") else ".mp3"
    try:
        # Model CPU'da çalışır; olay döngüsünü kilitlememek için iş parçacığında.
        from starlette.concurrency import run_in_threadpool
        return {"text": await run_in_threadpool(transcribe.transcribe, audio, suffix)}
    except RuntimeError:
        raise HTTPException(status_code=503, detail="transcription is not available")
    except Exception:
        logger.exception("transcribe failed")
        raise HTTPException(status_code=500, detail="transcription failed")


# #1234: kitap aktarımında sayfa temizliği (filigran + renkli cevap işaretleri). exam API önce örnek sayfalarla analiz eder,
# sonra sayfaları tek tek temizletir. scipy yoksa available=false ve aktarım orijinal sayfalarla sürer.
MAX_CLEAN_SAMPLE_PAGES = 40


class CleanAnalyzeRequest(BaseModel):
    pages: List[str] = Field(min_length=1, max_length=MAX_CLEAN_SAMPLE_PAGES)
    mark_colour: List[int] | None = Field(default=None, alias="markColour", min_length=3, max_length=3)


@app.get("/clean/status")
async def clean_status():
    return {"available": page_clean.available()}


@app.post("/clean/analyze")
async def clean_analyze(request: CleanAnalyzeRequest):
    if not page_clean.available():
        raise HTTPException(status_code=503, detail="page cleaning is not available")
    try:
        pages = [decode_base64_image(p) for p in request.pages]
        from starlette.concurrency import run_in_threadpool
        return await run_in_threadpool(page_clean.analyze, pages, request.mark_colour)
    except Exception:
        logger.exception("clean analyze failed")
        raise HTTPException(status_code=500, detail="page analysis failed")
    finally:
        pages = None
        memory.trim()


@app.post("/clean/page")
async def clean_page(request: Request, token: str):
    analysis = page_clean.get_analysis(token)
    if analysis is None:
        # Servis yeniden başladı ya da analiz süresi doldu: exam API yeniden analiz eder.
        raise HTTPException(status_code=410, detail="analysis expired")
    data = await request.body()
    if not data:
        raise HTTPException(status_code=400, detail="empty page")
    try:
        from starlette.concurrency import run_in_threadpool
        image, marks, watermark = await run_in_threadpool(page_clean.clean_page, data, analysis)
    except Exception:
        logger.exception("clean page failed")
        raise HTTPException(status_code=500, detail="page cleaning failed")
    finally:
        memory.trim()
    return Response(content=image, media_type="image/webp", headers={
        "X-Page-Marks": json.dumps(marks, separators=(",", ":")),
        "X-Watermark-Removed": "true" if watermark else "false",
    })

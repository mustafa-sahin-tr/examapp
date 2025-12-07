python scripts/predict_to_json.py

yolo detect train data=data/dataset.yaml model=yolov8n.pt epochs=50 imgsz=640 batch=4 device=cpu

# bir önceki train'in bilgilerini kullanarak devam etme 
yolo detect train data=data/dataset.yaml model=runs/detect/train/weights/best.pt epochs=30 imgsz=640 batch=2 device=cpu

# test
yolo detect predict model=runs/detect/train/weights/best.pt source=data/images/image.png conf=0.25

apt update && apt install tree -y
tree -L 2

source venv/bin/activate

uvicorn main:app --host 0.0.0.0 --port 8080


yolo detect train \
  model=runs/detect/train-only-q/weights/best.pt \
  data=data/dataset.yaml \
  epochs=50 \
  imgsz=640 \
  batch=1 \
  project=runs/detect \
  name=train-only-q-v2 \
  device=cpu

yolo detect train \
  model=yolov8n.pt \
  data=data/dataset.yaml \
  epochs=50 \
  imgsz=640 \
  batch=1 \
  name=train-only-q \
  device=cpu

source venv/bin/activate

yolo detect train \
  model=runs/detect/train-only-q-v4/weights/best.pt \
  data=data/dataset.yaml \
  epochs=50 \
  imgsz=640 \
  batch=1 \
  project=runs/detect \
  name=train-only-q-v5 \
  device=cpu

  yolo detect train \
  model=runs/detect/train-only-q-v5/weights/best.pt \
  data=data/dataset.yaml \
  epochs=50 \
  imgsz=640 \
  batch=1 \
  project=runs/detect \
  name=train-only-q-v6 \
  device=cpu


yolo detect train \
  model=runs/detect/train-answers/weights/best.pt \
  data=data/dataset-answers.yaml \
  epochs=50 \
  imgsz=640 \
  batch=1 \
  project=runs/detect \
  name=train-answers-v2 \
  device=cpu


yolo detect train \
  model=runs/detect/train-answers-v2/weights/best.pt \
  data=data/dataset-answers.yaml \
  epochs=25 \
  imgsz=640 \
  batch=2 \
  project=runs/detect \
  name=train-answers-v3 \
  device=cpu


for img in *.jpg; do   txt_file="../labels/${img%.jpg}.txt";   if [ ! -f "$txt_file" ]; then     echo "Removing $img (no label)";     rm "$img";   fi; done
for img in *.png; do   txt_file="../labels/${img%.jpg}.txt";   if [ ! -f "$txt_file" ]; then     echo "Removing $img (no label)";     rm "$img";   fi; done


for img in *.png; do   txt_file="../labels/${img%.jpg}.txt";   if [ ! -f "$txt_file" ]; then     echo "Removing $img (no label)";   fi; done
for img in *.jpg; do   txt_file="../labels/${img%.jpg}.txt";   if [ ! -f "$txt_file" ]; then     echo "Removing $img (no label)";   fi; done

yolo detect train data=data/dataset.yaml model=yolov8n.pt epochs=50 imgsz=640 batch=1 name=train-answers device=cpu

apt-get update
apt-get install libzbar0 





# save_period parametresi ile her N epoch'ta bir ağırlıkları kaydedebilirsiniz (ör: save_period=5 her 5 epoch'ta bir kaydeder)
!yolo detect train data=/content/drive/Othercomputers/PersonalMacBookPro/app/data/dataset-answers.yaml cache=True model=/content/drive/Othercomputers/PersonalMacBookPro/app/runs/detect/train-answers-v2/weights/best.pt epochs=50 imgsz=640 name=train-answers-v4 project=/content/drive/Othercomputers/PersonalMacBookPro/app/data/answers/runs save_period=5
!yolo detect train data=/content/drive/Othercomputers/PersonalMacBookPro/app/data/dataset.yaml cache=True model=/content/drive/Othercomputers/PersonalMacBookPro/app/runs/detect/train2/weights/best.pt epochs=50 imgsz=640 name=train3 project=/content/drive/Othercomputers/PersonalMacBookPro/app/data/questions/runs

!yolo detect train data=/content/drive/Othercomputers/PersonalMacBookPro/app/data/dataset-answers.yaml cache=True  save_period=1 model=/content/drive/Othercomputers/PersonalMacBookPro/app/data/answers/runs/train-answers-v6/weights/best.pt epochs=50 imgsz=640 name=train-answers-v8 project=/content/drive/Othercomputers/PersonalMacBookPro/app/data/answers/runs



!pip install ultralytics

!python /content/drive/Othercomputers/PersonalMacBookPro/question-detector/scripts/json_to_yolo_answers.py

!python /content/drive/Othercomputers/PersonalMacBookPro/question-detector/scripts/json_to_yolo_only_questions.py

  !yolo detect train data=/content/drive/Othercomputers/PersonalMacBookPro/question-detector/data/dataset.yaml cache=True model=/content/drive/Othercomputers/PersonalMacBookPro/question-detector/data/questions/runs/train5/weights/best.pt epochs=50 imgsz=640 name=train20251206-2 project=/content/drive/Othercomputers/PersonalMacBookPro/question-detector/data/questions/runs
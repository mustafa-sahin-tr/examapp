# question-detector

YOLO ile sayfadaki soruları (`models/question-best.pt`) ve her sorunun şıklarını (`models/answer-best.pt`) bulan
FastAPI servisi. Gateway üzerinden `/question-detector-dev/*` (`/predict`, `/read-qr`, `/send-to-fix`).

## Eğitim veri seti — sürekli toplama

QuestionCanvas'ta Admin olarak kaydettiğin (ya da "Eğit"e bastığın) her sayfa `dataset/` altına YOLO'ya hazır yazılır
(`dataset_store.py`):

```
dataset/questions/images|labels   tam sayfa + soru kutuları   -> soru modeli
dataset/answers/images|labels     soru kırpması + şık kutuları -> şık modeli
dataset/manifest.jsonl            kayıt günlüğü
```

- Dosya adı resmin içerik hash'i: aynı sayfayı tekrar kaydedersen etiketi güncellenir, kopya oluşmaz. Hiçbir şey silinmez.
- **"Bu sayfayı soru eğitimine alma"** kutusu: sayfadaki soruların hepsini işaretlemediysen seç. Sayfa soru
  modeline girmez (işaretsiz sorular "soru değil" diye öğrenilirdi); şık kırpmaları yine kaydedilir.
- `dataset/` git'e girmez. Mac'te `question-detector/` Drive for desktop ile Drive'a aynalanır; eğitim de her
  seferinde veriyi `My Drive/hedefokul-ml/dataset` arşivine kopyalar (ayna silinse bile arşiv kalır).

## Mac'te adım adım

```bash
git fetch && git checkout <branch> && git pull
cd question-detector
./commands.sh check      # temiz başlangıç kontrolü
./commands.sh clean      # gerekirse eski artıkları sil (dataset/ ve models/'e dokunmaz)
./commands.sh check      # "SONUÇ: Hazır" görmelisin
```

Aspire'ı başlat → Admin ile giriş → soru tuvalinde sayfa yükle → tahmin edilmeyen/yanlış kutuları düzelt → kaydet.

```bash
./commands.sh status     # kaç sayfa/kırpma birikti
```

## Eğitim (Colab)

1. Drive for desktop senkronu bitsin (`dataset/` Drive'da görünsün).
2. Colab'da `Othercomputers/<Mac>/question-detector/training/weekly_training.ipynb`'i aç, çalışma zamanı **GPU**.
3. Hücreleri sırayla çalıştır: önce `--dry-run` (sayılar), sonra eğitim.
4. Yeni model eskisinden kötü değilse `models/*-best.pt` güncellenir (sürümlü kopyalar ve rapor `My Drive/hedefokul-ml/`).
5. Mac'te senkron gelince: `git add question-detector/models && git commit -m "model: <tarih>"`, Aspire'da
   question-detector'ı yeniden başlat.

Eğitim her seferinde **birikmiş verinin tamamıyla** ve mevcut modelden başlar (düşük `lr0`), bu yüzden eski bilgi
unutulmaz. Train/val ayrımı sayfa hash'ine göre sabittir; eski ve yeni model aynı val setinde karşılaştırılır.

## Test

```bash
./commands.sh test   # pillow + pytest
```

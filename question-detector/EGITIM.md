# Soru/şık modeli eğitimi — adım adım (Mac)

Amaç: soru oluştururken elle düzelttiğin sayfaları biriktirmek ve haftada bir bu birikimle modeli yeniden eğitmek.
Eğitim her seferinde **birikmiş verinin tamamıyla** ve mevcut modelden başladığı için model eski bilgisini unutmaz.

## Genel resim

```
QuestionCanvas (Admin / QuestionTrainer, "Kaydet ve Düzelt")
   └─ POST /send-to-fix ──> question-detector/dataset/        (Mac, Drive for desktop ile Drive'a aynalanır)
                               questions/images|labels        tam sayfa + soru kutuları  -> soru modeli
                               answers/images|labels          soru kırpması + şık kutuları -> şık modeli
                               manifest.jsonl                 kayıt günlüğü

Colab: training/weekly_training.ipynb
   1. dataset/  ──kopyala──> My Drive/hedefokul-ml/dataset   (kalıcı arşiv, hiçbir şey silinmez)
   2. arşivin tamamı -> sabit train/val ayrımı (sayfa hash'ine göre)
   3. models/question-best.pt ve models/answer-best.pt'den fine-tune
   4. eski ve yeni model aynı val setinde karşılaştırılır; yeni en az eski kadar iyiyse models/'e yazılır
```

Önemli: Drive for desktop'ın "Bilgisayarım" senkronu bir **aynadır**. Mac'te silinen dosya Drive'da da silinir.
Eski veri seti büyük olasılıkla bu yüzden kayboldu. Kalıcı kopya `My Drive/hedefokul-ml/` altındaki arşivdir;
eğitim her çalıştığında yeni veriyi oraya kopyalar.

## 1. İlk kurulum ve temiz başlangıç (bir kez)

Aspire kapalıyken, repo kökünden:

```bash
git fetch && git checkout feature/question-detector-training-pipeline && git pull   # merge sonrası: master
cd question-detector
./commands.sh check      # durum raporu, hiçbir şey silmez
./commands.sh clean      # eski data/, runs/, YOLOv8/, scripts/ artıklarını siler (onay sorar)
./commands.sh check      # sonunda "SONUÇ: Hazır" yazmalı
```

`check` neye bakar:

| Kontrol | Beklenen |
|---|---|
| Kod | `dataset_store.py`, `training/train.py` var; `question-detector` altında commit edilmemiş değişiklik yok |
| Eski artıklar | `data/`, `runs/`, `YOLOv8/`, `scripts/`, `prediction*.json`, `yolov8n.pt` yok |
| Öğretmen modeller | `models/question-best.pt`, `models/answer-best.pt` var (sha256 yazdırılır) |
| Veri seti | `dataset/` boş (ilk kayıtta oluşur) veya birikmiş sayılar |
| Drive | Google Drive for desktop kurulu; senkronlanması gereken klasörün yolu yazdırılır |
| Aspire | `AppHost.cs` question-detector klasörünü `/app`'e bağlıyor; servis `:8080`'de mi |

Drive for desktop > Tercihler > **Bilgisayarım** altında, `check` çıktısında yazan `question-detector` klasörünün
senkronlandığından emin ol. Colab'da bu klasör `/content/drive/Othercomputers/PersonalMacBookPro/question-detector`
olarak görünür. Bilgisayarın adı farklıysa notebook'taki `QD` değişkenini düzelt.

`commands.sh clean` yalnız eski artıkları siler; `dataset/` ve `models/` klasörlerine **asla** dokunmaz.

## 2. Her gün: soru işaretle

Eğitime veri yalnız **Admin** veya **`QuestionTrainer`** realm rolü olan hesaplar gönderebilir (gateway
`send-to-fix`'i yalnız bu iki role açar). Yetkili öğretmene rolü Keycloak admin console'dan ver:
`exam-realm` → Users → *(öğretmen)* → Role mapping → Assign role → realm role `QuestionTrainer`.
Rol yeni giriş/token yenilemede geçerli olur (öğretmen çıkış yapıp tekrar girsin).
**Öğretmeni askıya alır veya yetkisini geri alırsan `QuestionTrainer` rolünü de elle kaldır** (Role mapping → Unassign):
gateway yalnız token'daki role bakar, öğretmen askıya alma bu rolü kaldırmaz.
Çalışan (eski) bir Keycloak'ta rol yoksa önce Realm roles → Create role → `QuestionTrainer` ile oluştur;
temiz import'ta `deploy/keycloak/dev-import/realm-export.json` ile gelir.

1. Aspire'ı başlat.
2. Yetkili hesapla (Admin veya `QuestionTrainer` rolü olan öğretmen) gir, soru tuvalinde sayfayı yükle.
3. Model tahmin edemediyse veya yanlış bulduysa soru ve şık kutularını elle düzelt ya da çiz.
4. Kaydet düğmesinin yanındaki menüden **"Kaydet ve Düzelt"**i seç: sorular kaydedilir ve sayfa veri setine gider.
   Düz **"Kaydet"** eğitime göndermez. Kaydetmeden yalnız eğitime göndermek için "Eğit" düğmesini kullan.
   Bu düğmeler ve aşağıdaki kutu yalnız yetkili hesaplarda görünür.
5. **Sayfadaki soruların hepsini işaretlemediysen** "Eğit"in yanındaki **"Bu sayfayı soru eğitimine alma"**
   kutusunu seç. Aksi halde işaretsiz sorular "soru değil" diye öğrenilir ve soru modeli bozulur. Bu durumda sayfa
   soru modeline girmez, şık kırpmaları yine kaydedilir. Kutu her yeni sayfada boş başlar.

Kurallar:

- Temiz arka planlı sayfaları da kareli/çizgili defter arka planlı sayfaları da kaydet. Eski bilgiyi korumak
  için temiz sayfalara da ihtiyaç var.
- Aynı sayfayı tekrar kaydetmek kopya oluşturmaz, son çizdiğin etiket geçerli olur (dosya adı resmin hash'i).
- Bir şıkkı soru kutusunun tamamen dışında çizersen o soru şık eğitimine alınmaz (`skipped_crops` loguna yazılır).

Biriken veriyi görmek için:

```bash
./commands.sh status     # sayfa / kırpma sayıları + son 5 kayıt
```

**İlk kayıttan sonra mutlaka `status` çalıştır** ve sayıların arttığını gör.

## 3. Haftalık: eğitim (Colab)

1. Mac'te Drive senkronunun bitmesini bekle; `dataset/` Drive'da görünmeli.
2. Colab'da `Othercomputers/PersonalMacBookPro/question-detector/training/weekly_training.ipynb` dosyasını aç.
3. **Çalışma zamanı > Çalışma zamanı türünü değiştir > GPU (T4).**
4. Hücreleri sırayla çalıştır:
   - Drive'ı bağla
   - `pip install ultralytics==8.3.55`
   - `QD` ve `ARCHIVE` yolları (`ls` çıktısında `dataset/ models/ training/` görünmeli)
   - **Kuru çalıştırma** (`--dry-run`): veriyi arşive kopyalar, train/val sayılarını gösterir, eğitmez
   - **Eğitim**

Eğitim çıktısı:

| Nerede | Ne |
|---|---|
| `question-detector/models/*-best.pt` | Yeni model eskisinden kötü değilse bunun yerine geçer |
| `My Drive/hedefokul-ml/models/` | Her eğitimin sürümlü modeli (`question-YYYYMMDD-HHMM.pt`) + ilk öğretmen (`*-initial.pt`) |
| `My Drive/hedefokul-ml/reports/` | Sayılar, eski/yeni mAP, terfi kararı (JSON) |
| `My Drive/hedefokul-ml/runs/` | Eğitim grafikleri (results.png, confusion matrix ...) |
| `My Drive/hedefokul-ml/dataset/` | Kalıcı veri arşivi |

`train.py` seçenekleri (notebook'taki komutun sonuna eklenir):

| Seçenek | Varsayılan | Not |
|---|---|---|
| `--only questions\|answers` | ikisi | Yalnız bir modeli eğit |
| `--epochs` | 30 | |
| `--lr0` | 0.002 | Düşük tutulur, mevcut bilgiyi korur |
| `--imgsz` | 640 | Küçük yazılı sayfalarda 960/1024 denenebilir |
| `--batch` | 16 | GPU belleği yetmezse 8 |
| `--freeze` | 0 | Örn. 10: backbone'u dondurur (unutmaya karşı daha temkinli) |
| `--patience` | 10 | İyileşme yoksa erken durur |
| `--force` | kapalı | Metrik düşse de yeni modeli al (önerilmez) |

Bir model en az **5 train resmi ve 1 val sayfası** yoksa eğitilmez, atlanır. Sayfaların yaklaşık %10'u val'e
ayrılır; bir sayfa her zaman aynı tarafta kalır, kırpmalar da kendi sayfalarını izler.

## 4. Yeni modeli devreye al

Mac'te senkron gelince:

```bash
git status question-detector/models           # *-best.pt değişmiş olmalı
git add question-detector/models && git commit -m "model: <tarih>" && git push
```

Ardından Aspire'da `question-detector` kaynağını yeniden başlat. Modeller servis açılırken bir kez yüklenir;
logda `Models loaded: question=models/question-best.pt ...` satırı görünür.

Colab `Othercomputers` klasörüne yazamazsa yeni model yine `My Drive/hedefokul-ml/models/` altındadır.
Oradan indirip `question-detector/models/question-best.pt` / `answer-best.pt` adıyla kopyala.

**Geri alma:** `My Drive/hedefokul-ml/models/` altından istediğin sürümü `models/*-best.pt` üzerine kopyala
ya da `git checkout <eski-commit> -- question-detector/models`.

## Sorun giderme

| Belirti | Ne yap |
|---|---|
| `status` kayıttan sonra artmıyor | Aspire'da question-detector logunda `Training sample saved` satırına bak; "Kaydet ve Düzelt" ile mi kaydettin, hesapta Admin veya `QuestionTrainer` rolü var mı (yoksa düğme görünmez, gateway 403 döner)? |
| Kayıtta "answer count mismatch" | Her sorudaki şık sayısı sayfanın şık sayısı ayarıyla aynı olmalı |
| Kayıtta "Box is outside the image" | Bir soru kutusu resmin tamamen dışında; düzelt |
| Colab'da `ls` boş | Mac'te Drive senkronu bitmemiş veya `QD` yolundaki bilgisayar adı farklı |
| `yetersiz veri, atlanıyor` | O model için daha çok sayfa biriktir |
| Yeni model "eski model kalıyor" | Val setinde iyileşmedi; daha çok/çeşitli veri biriktir veya `--epochs`, `--imgsz` dene |

## Dosyalar

| Dosya | Görev |
|---|---|
| `dataset_store.py` | `/send-to-fix` kaydını `dataset/` altına YOLO formatında yazar |
| `main.py` | FastAPI: `/predict`, `/read-qr`, `/send-to-fix`; modelleri `models/`'ten yükler |
| `training/train.py` | Arşivleme, train/val ayrımı, fine-tune, karşılaştırma, terfi |
| `training/weekly_training.ipynb` | Colab notebook'u |
| `training/check_clean_start.py` | `./commands.sh check` |
| `commands.sh` | `check` / `clean` / `status` / `test` |

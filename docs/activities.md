# Etkinlikler — etkileşim tipleri, editör, oturum kuralları

Eskiden `.claude/rules/local-dev.md` içindeydi; 2026-11-01'de taşındı. Bir tipe dokunmadan önce o bölümü oku
(`grep -n '#585' docs/activities.md`). Plan / çözüm / payload sözleşmesi, kontrolcü kuralları ve editör davranışı buradadır;
bir davranışı değiştirirsen bölümü de güncelle.

**Issue #609 — tamamlanmış etkinliği yeniden çöz.** `POST /api/exam/test-instances/{id}/retake` (gövdesiz; yalnız oturumun sahibi,
yalnız `Kind=Activity`, yalnız Completed; start-test ile aynı erişim kararı — atama / sınıf + görünürlük) **aynı oturumu** sıfırlar,
yeni oturum açılmaz (#367 unique index'i değişmedi): cevaplar / kontrol sonuçları silinir, `Status=Started`, `EndTime`/`SolutionsRevealedAt`
boş, `CheckCount=0`, `AttemptStartedAt=now`, `RetakeCount+1`; ilk kontrol özeti (`FirstCheck*`), `StartTime` (atama penceresi),
`ActiveSeconds` (yeniden tamamlamada birikir; deneme penceresi `AttemptStartedAt`'ten) ve `FirstCompletedAt` korunur. Hata: 404 (yok /
başkasının; silinmiş etkinlik `WorksheetNotFound`), 403 `AccessDenied`, 409 `NotAnActivity` (test davranışı aynı) / `ActivityNotCompleted`
(Started ya da Expired), 429 `RateLimited` (`RateLimiting:ActivityRetake`, kullanıcı başına dakikada 5); zaten yeniden çözülüyorsa 200
`alreadyRetaking: true` (hiçbir şey silinmez). Öğretmen/veli takibi, öğrencinin atama durumu (`isCompleted`/`isFinished`) ve takvim yeniden
çözülen oturumu tamamlandı sayar (`WorksheetInstanceStatusRules.TrackingStatus`; DTO'larda `retaking`, öğretmende `retakeCount`).
Bilinçli kabuller: (1) öğrencinin kendi "Tamamlanan" filtresi gerçek durumu kullanır — yeniden çözülen etkinlik "devam eden"de görünür;
(2) veli yeniden çözüm sürerken sonuç göremez (önceki cevaplar silindi; liste sonuç bağlantısı vermez, sonuç ucu 404), yeniden
tamamlanınca **son denemeyi** görür (etkinlik puansız, pratik); (3) yeniden tamamlama aynı `WorksheetInstanceId` ile ikinci
`ActivityCompletedEvent` yazar ve BadgeService (UserId, WorksheetInstanceId) ile yok sayar → rozet sayacı / çalışma süresi şişmez; ilk
tamamlama rozet şartını sağlamadıysa (`CountsTowardBadges=false`) yeniden tamamlama da sayılmaz (aynı tekillik). Migration
`AddTestInstanceRetakeTracking` (`FirstCompletedAt`, `AttemptStartedAt`, `RetakeCount`; mevcut Completed satırları `EndTime` ile doldurur)
exam API açılışında uygulanır (Aspire'da exam API'yi yeniden başlat).

**Issue #583 — `openText` (bileşen H, açık uçlu cevap; AI'sız ilk sürüm).** Plan `{"version":1,"type":"openText","minWords?","maxWords?","lines?"}`
(sözcük 1..2000, min ≤ max, satır 1..30), çözüm `{"sampleAnswer?","rubric?"}` (düz metin ≤ 4000; boş geçerli). Öğrenci payload'u
`{"version":1,"type":"openText","text"}` (≤ 8000 karakter; boş = cevapsız); sözcük sınırı zorlanmaz, yalnız `maxWords × 2`'yi aşan
metin 400. Otomatik kontrol yok: satır 0/0 (doğru/yanlış ve ilk kontrol özetine girmez), geri bildirim
`{"rows":[{"id":"text","status":"pending"|"sufficient"|"partial"|"insufficient"}]}`; açık uçlu cevap `AnswerSubmittedEvent` yazmaz
(puan yok). **Yalnız etkinlikte:** havuzdan ekle, tekli/toplu soru kaydı, test kopyalama 400, Activity → Test tür değişimi 409
`OpenTextNotAllowedInTest`; skor kuralı (`WorksheetScoring`) savunma olarak açık uçlu satırı toplamdan düşer, "yanlışlarım" testine
almaz. Öğrenciye çözümden yalnız `sampleAnswer` gider. Kontrol edilen (ya da oturumu etkin durumda bitmiş) cevap öğretmen kuyruğuna
düşer: `GET /api/exam/open-text-reviews?status=pending|reviewed|all&worksheetId=&page=&pageSize=` (page ≤ 10000, pageSize ≤ 50;
kullanıcı başına dakikada 60, `RateLimiting:OpenTextReviewRead`) ve `PUT /api/exam/open-text-reviews/{instanceQuestionId}`
`{evaluation, feedback?, answerHash}` (dakikada 60, `RateLimiting:OpenTextReview`). Yetki: gateway Teacher + Admin
(`RouteRoleAuthExtensions`), backend onaylı öğretmen + #573 takip kuralı — öğretmen etkinliği **kendi atamasıyla** hedeflediği, okul
kapsamındaki öğrencinin **atama penceresinde sayılan** oturumunu görür; Admin hepsini. Başkasının verdiği değerlendirmeyi yalnız o
kullanıcı ve Admin düzeltir (diğerine 403 `OpenTextReviewForbidden`, satırda `canEdit:false`). **Rubrik yalnız sorunun sahibine ve
Admin'e** döner (diğer değerlendirenlere `rubric: null`); örnek cevap herkese. Öğrenci değerlendirmeyi oturum ve kontrol yanıtlarında
`openTextReview` (`pending` / `reviewed` / `outdated` + değerlendirme, geri bildirim) olarak görür; metni değişince değerlendirme eskir
(`outdated`, gönderilince yeniden kuyrukta), cevap boşaltılınca gösterilmez, yeniden çözme (#609) siler. Düz metin süzgeci
(`PlainTextAttribute`; öğrenci metni, geri bildirim, çözüm, pasaj yönergesi) C0 (\n \r \t hariç), DEL + C1 (U+007F–U+009F) ve bidi
(U+202A–U+202E, U+2066–U+2069) karakterlerini reddeder. İşaretleme: H → `openText` (sayfa prompt'u `SchemaRevision` 6; kayıtlı özel
prompt sürümü yeni varsayılanı ezer). Değerlendirme bildirimi (BadgeService) bu sürümde yok. Migration `AddOpenTextReviews` (tablo +
`IX_Questions_InteractionType_OpenText` kısmi, `IX_WorksheetAssignments_CreateUserId_WorksheetId`) exam API açılışında uygulanır
(Aspire'da exam API'yi yeniden başlat).

**Issue #584 — öğretmen etkinlik analizi ve veli özeti.** Etkinlik sayfası detayında (öğretmen, "Çalışma takibi"nin altında)
**Analizi göster** → `GET /api/exam/worksheet/{id}/activity-report` (onaylı öğretmen; kapsam takip ekranıyla aynı — kendi atamaları,
okul, atama penceresi; `GetWorksheetAssignmentsForTeacherAsync`'ten alınır). Analize yalnız tamamlanmış ve şu an yeniden çözülmeyen
oturumlar girer, cevaplar oturumun **son** hâlidir (ilk kontrol özeti değil) ve sunucuda kontrolcüyle yeniden değerlendirilir. Yanıt:
öğrenci × madde ısı haritası (doğru satır / toplam satır) ve madde başına öğe dağılımı — doğru oranı, boş oranı, en sık 3 yanlış cevap.
Öğe tanımı kontrolcünün `IInteractionGrader.Analyze`'ında: dragPlace = kart (cevap = konduğu hedef; "kemik → kaslar"), çeldirici kart
doğru ⇔ kullanılmamış; matrixChoice = satır → kolon; match = sol → sağ; slotFill = kutu → seçenek / yazılan metin (Türkçe küçük harf,
boşluk birleşik gruplanır); textSelect = hedef (+ seçilen hedef dışı token çeldirici olur), punctuation = gap → sembol;
dragDropLabeling = çözümdeki hedef → kart; MCQ = şık etiketi (`Tag`, yoksa A, B…). openText ve çözümsüz madde puanlanamaz (oran yok).
Analiz öğrenciye gitmez (çözümü ele verir). Takip tablosu (#573) bilerek yüzde göstermez; analiz ayrı ve istenince yüklenir.
Veli özeti (`/api/exam/parent/children/{id}/summary`) `activities` taşır: bu hafta / toplam tamamlanan etkinlik (`FirstCompletedAt`;
yeniden çözme ikinci kez saymaz) ve ilk kontrol doğru oranı ortalaması (`FirstCheck*`, kontrol yoksa null). Migration yok.

**Issue #590 — görselsiz etkinlik maddesi (dilim a).** Etkinlik sayfasında (öğretmen) **Görselsiz madde ekle** → `/activity-editor?testId=&gradeId=`
(`&questionId=` düzenleme; editör o etkinliğin formla düzenlenebilen maddelerini listeler). Türler: matris seçim (D/Y şablonu,
çoklu seçim, `shuffleRows`), sınıflandırma (`dragPlace` place, kutusuz kovalar; kovasız kart çeldirici, kova kapasitesi kart
sayısı — ipucu vermesin), sıralama (`dragPlace` order), eşleştirme (`match`, çeldirici sağ öğeler). Aynı tipler/kontrolcüler;
matris satırında `box` artık isteğe bağlı (kutusuz satırda boş olmayan `label` zorunlu, `cells` yasak). Yeni madde toplu kayıtla
(`questions/save`, `imageData` yok ya da tek etkileşimsiz görsel; kapsam kutusu 0×0 kırpılmaz), düzenleme tekli kayıtla. Yönerge düz
metin (#573). Seçilen konu kilitlenir (bkz. architecture.md); seçilmezse madde metinle sınıflandırılır. Migration yok; Aspire'da exam
API ve BadgeService'i yeniden başlat.

**Issue #590 dilim b — boşluk türleri (görselsiz düzen).** Plana isteğe bağlı `layout` geldi (`InteractionLayout`, TS
`interaction-layout.ts`): `{kind:"list"}` (her öğe etiketiyle satır), `{kind:"text",text}` (satır içi metin, `[[id]]` öğenin yeri),
`{kind:"table",header?,rows}` (hücreler düz metin, `[[id]]` içeren hücre cevap kutusu; satırlar eşit genişlikte, başlıkta yer tutucu
yok). Kayıtta her öğe düzende **tam bir kez** geçer, bilinmeyen id'li yer tutucu yok, düz metin süzgeci (`PlainTextAttribute` kuralı),
metin ≤ 4000, tablo ≤ 30 satır × 8 kolon, hücre ≤ 200. Düzende çözüm yoktur. `slotFill`: `layout` varsa kutularda `box` **yasak**
(yoksa eskisi gibi zorunlu), `list`'te her kutunun boş olmayan `label`'ı (soru) zorunlu. `dragPlace`: yalnız `text` düzeni, yalnız
`place`, hedefler kutusuz ve kapasite 1, kartlar kutusuz (boşluklu metin + kelime havuzu); kayıttaki kart karıştırma hedef id'lerine
dokunmaz, metin geçerli kalır. Editörde 4 yeni tür: **Boşluk doldurma** (metinde `[[cevap|alternatif]]`; "Seçili metni boşluk yap"),
**Soru + kısa cevap** (liste), **Tablo** (hücrede `[[cevap]]`), **Kelime havuzu** (`[[kelime]]` + çeldiriciler; aynı kelime birden
çok boşlukta tek kart + `reuse`). Tek cevap birimsiz sayıysa (`parseTrNumber`) sayı kutusu (kesir / tam sayılı kesir, eşdeğerlik açık),
değilse metin kutusu (#575 normalleştirmesi). Seçim/palet kutusu, birim ve sonek editörde yok (tuvalde var); öyle bir plan editörde
açılmaz. Oynatıcılar düzeni HTML olarak çizer (görsel varsa etkileşimsiz üstte kalır). Sınıflandırma metnine düzen metni yer
tutucular `____` olarak girer. Migration yok; Aspire'da exam API'yi yeniden başlat.

**Issue #585 — `mazeTree` (bileşen G, yol oyunları).** Tek motor, dört varyant: `tree` (D/Y dallanan ağaç), `rooms` (odalı
labirent, kapılar iki yönlü), `board` (ışıklı pano, satır başına bir doğru kutu, alttan üste), `stones` (ırmak taşları, öğrenci yalnız
basılır taşlara basarak geçer; yolsuz). Plan `{"version":1,"type":"mazeTree","variant","start?","nodes":[{id,label?,end?,row?,col?}],"edges":[{id,from,to,label?}]}`;
düğüm = ifade / oda / pano satırı / taş, kenar = seçenek (dal, kapı, kutu). Çözüm yol varyantlarında `{"choices":{"düğüm":"kenar"}}`
(rota = başlangıçtan seçimleri izleyip bir `end` düğümüne döngüsüz varan yol; ağaçta rota dışı ifadelerin doğrusu da saklanır),
taşlarda `{"marked":[...]}` (basılmaması gereken taşlar). Öğrenci payload'u `{"path":[kenarlar]}` (kesintisiz, bitişten sonra adım yok, ≤ 128) ya da taşlarda `{"stepped":[...]}`
(öğrencinin bastığı taşlar; taş doğru ⇔ basılır taşa basıldı ya da basılmaz taşa basılmadı).
Kayıt kuralları (`MazeTreeGrader.ValidateDefinition`): ağaç gerçek ağaç (2–4 dal, derinlik ≤ 5), pano zinciri (2–6 kutu, ≤ 12 satır,
satır adı isteğe bağlı, bitişten kenar çıkmaz), odalar tekil `row`/`col` (0–7), yalnız komşu (çapraz dahil) odalar arasında tek kapı ve bir
dörtlüde tek çapraz kapı, taşlarda en az bir basılmaz ve en az bir basılır taş, her düğüm başlangıçtan
erişilebilir, bir düğümün seçenek etiketleri tekil. Kontrol **ilk sapmada durur**: satırlar öğrencinin geçtiği karar düğümleri, toplam =
rota uzunluğu, geri bildirim `{"rows","status":"reached|deviated|incomplete","at"}` (rota ve doğru dal yazılmaz); taşlarda da satırlar
ilk yanlış taşta durur (taş ikili: hepsi çözümü verirdi), boş cevap hepsi yanlış. Oynatıcı yolu 128 adımda keser. Analiz (#584): öğe = rotadaki karar düğümü (cevap = seçilen dal; sapmadan sonrası cevapsız), taşlarda taş.
Yazarlık görselsiz editörde (`/activity-editor`) dört şablonla: tam ikili D/Y ağacı (1–4 düzey, ifadeler genişlik öncelikli), pano
satırları, R×C oda ızgarası (2–4; etiketsiz kapı = duvar, bir dörtlüde tek çapraz kapı), taşlar. Oynatıcı oyunu HTML olarak çizer (görsel
varsa etkileşimsiz üstte); Claude yapı çıkarımı yok (G bloğu tipsiz kalır). Testte de çözülebilir (puanlanır). Migration yok; Aspire'da
exam API'yi yeniden başlat (yeni kontrolcü).

**Issue #586 dilim a — `crossword` (bileşen G, çengel bulmaca).** Plan `{"version":1,"type":"crossword","rows","cols","clues":[{id,dir:"across"|"down",row,col,length,number?,text}]}`
(ızgara 2–20, 1–80 ipucu, uzunluk ≥ 2, ipucu metni zorunlu düz metin ≤ 300); beyaz hücreler ipuçlarının birleşimi, harfler planda yok.
Çözüm `{"answers":{"k1":"KALP"}}` (kayıtta tr-TR büyük harfe çevrilir; her ipucunun cevabı, uzunluk tutar, yalnız harf/rakam, kesişen
hücrede aynı harf). Kayıt kuralları (`CrosswordGrader.ValidateDefinition`): aynı yönde iki ipucu hücre paylaşmaz ve uç uca değmez, aynı
başlangıçtaki ipuçlarının numarası aynı. Öğrenci payload'u `{"grid":["KA  ",...]}` (satır başına `cols` karakter, boş hücre boşluk, siyah
hücrede harf yok; harfsiz ızgara = cevap yok). Kontrol ipucu başına (tr-TR büyük harfle karşılaştırma), geri bildirim `{"rows":[{id,correct}]}`
(harf / yanlış hücre yazılmaz). Analiz (#584): öğe = ipucu, cevap = yazılan kelime (`·` boş hücre). Yazarlık görselsiz editörde
(`/activity-editor`, tür **Çengel bulmaca**): varsayılan 10×10 ızgarada hücre seçilir, yön (→ / ↓) + soru + cevap yazılır, harfler hücrelere
düşer (çakışan harf kırmızı, sığmayan / aynı yönde üst üste binen kelime eklenmez; kelimenin ilk hücresine tık onu düzenler). Yer yetmezse
dört kenara satır / sütun eklenir (editörde en çok 30); kayıtta boş kenarlar kırpılır, kelimelerin alanı ≤ 20×20 (`crosswordAreaTooLarge`).
Form öğrenci ekranıyla aynı olduğundan ayrı önizleme yok. **Otomatik yerleştir** kelimeleri kesiştirir (40
tohumlu deneme, kesişemeyen kelime ayrı satıra). Oynatıcı ızgarayı HTML olarak çizer (yazınca ilerler, Backspace, ok
tuşları, boşluk / aynı hücreye ikinci tık yön değiştirir; görsel varsa etkileşimsiz üstte). Testte de çözülebilir (puanlanır). Claude ızgara
çıkarımı dilim e'de (aşağıda). Migration yok; Aspire'da exam API'yi yeniden başlat
(yeni kontrolcü).

**Issue #586 dilim b — `wordSearch` (bileşen G, kelime avı).** Plan `{"version":1,"type":"wordSearch","rows","cols","letters":["KALPX",...],"words":[{id,label}]}`
(ızgara 4–20, her hücre harf/rakam, 1–40 kelime, `label` listede görünen metin — kelime ya da ipucu, düz metin ≤ 100). Harf ızgarası
öğrenciye gider; çözüm `{"lines":{"w1":{"from":[r,c],"to":[r,c]}}}` (düz çizgi: yatay, dikey, 45° çapraz; kelimeler tekil çizgi).
Öğrenci payload'u `{"lines":[{from,to}]}` (kelimeye bağlı değil, en çok `max(10, 2 × kelime)` çizgi; çizgisiz = cevap yok). Kontrol
**harflere** bakar: kelime, öğrencinin bir çizgisinden iki yönden biriyle okunuyorsa bulundu (ızgarada başka yerde geçmesi de sayılır,
tr-TR büyük harf). Geri bildirim `{"rows":[{id,correct}],"lines":[bool]}` (çizgi başına kelime mi; çözüm çizgisi yazılmaz). Analiz: öğe =
kelime, cevap = bulundu. Yazarlık görselsiz editörde (tür **Kelime avı**): kelimeler + isteğe bağlı ipucu, boyut, çapraz / ters
seçenekleri; ızgara bunların ve bir tohumun saf fonksiyonu (**Karıştır** tohumu değiştirir, boş hücreler Türk alfabesinden), kayıtlı
madde içerik değişmedikçe kayıtlı ızgarayla açılır. Oynatıcıda sürükleyerek ya da iki dokunuşla (ilk harf → son harf) seçim. Migration
yok; Aspire'da exam API'yi yeniden başlat.

**Issue #586 dilim c — `logicGrid` (bileşen G, sözel mantık tablosu).** Plan
`{"version":1,"type":"logicGrid","subject":{label,items:[{id,label}]},"categories":[{id,label,items:[{id,label}]}],"clues":[{id,text}]}`
(2–8 konu öğesi = satırlar, 1–4 kategori, her kategoride konu öğesi kadar seçenek, plan içinde tüm id'ler tekil, etiket ≤ 60, en çok 20
ipucu ≤ 300). Çözüm `{"assign":{"p1":{"c1":"o2"}}}` (her satıra her kategoriden bir seçenek, kategoride birebir). Öğrenci payload'u
`{"cells":[{row,col,mark:"yes"|"no"}]}` (✗ öğrencinin notu, kontrolde yalnız ✓ sayılır; işaretsiz = cevap yok). Kontrol (satır, kategori)
çifti başına (`p1:c1`): o kategoride tek ✓ ve doğru seçenek. Geri bildirim `{"rows":[{id,correct}]}`. Analiz: öğe = çift, cevap = ✓ konan
seçenek. Oynatıcı kitaptaki **merdiven tablo**: üstte konu × bütün kategoriler, altında kategori × kategori karalama blokları (satır
grupları konu, sonra `Cn`..`C2`; `Ci`'nin sütunları `C1..C(i-1)`; tek kategoride yalnız üst blok), en altta üstteki ✓'lerden dolan salt
okunur **cevap tablosu**. Karalama blokları puanlanmaz; payload'da isteğe bağlı `notes:{"c2:c1":"y..n....."}` (satır kategorisi planda
sonra, satır öncelikli n × n karakter `.`/`y`/`n`; 16 KB payload sınırına sığsın diye kompakt); yalnız karalama içeren cevap da saklanır.
Hücre döngüsü boş → ✓ → ✗; "✓ koyunca satırı ve sütunu ✗ ile doldur" (varsayılan açık) ve teklik denetimi (iki ✓ ya da hepsi ✗ → kırmızı
çerçeve) her blokta. Yazarlık görselsiz editörde (tür **Mantık tablosu**), ayrı önizleme yok — öğretmen öğrencinin ekranında yazar: ipuçları,
çözümle dolu salt okunur merdiven tablo (oynatıcının `authoring` kipi) ve altta yazılabilir **cevap tablosu** (başlıkta satır grubu +
kategori adları, her satırda öğe + kategori başına cevabı); seçenekler ve çözüm bu tablodan çıkar. Öğrencinin gördüğü seçenek sırası
merdiven tablonun başlığında belirlenir (seçenek başlığında ‹ ›, kategori başlığında karıştır); sıra satırlarla aynıysa köşegen uyarısı. Plan / çözüm biçimi değişmedi. Migration yok; Aspire'da
exam API'yi yeniden başlat.

**Issue #586 dilim d — şifre katmanı (`slotFill` + `cipher`).** Yeni tip yok: `slotFill` planına isteğe bağlı
`cipher:{cells:["s2",null,"s1",...]}` (gizli kelimenin hücreleri: kutu id'si ya da `null` = boşluk; 2–40 hücre, en az 2 harf hücresi,
başta / sonda boşluk yok, aynı kutu birden çok hücrede geçebilir; görselli ve görselsiz planda). Harfler **çözümde**: şifrede geçen her
kutunun çözüm girdisinde `letter` (tek harf / rakam) zorunlu, şifresiz planda ya da şifre dışı kutuda yasak (`SlotFillGrader.ValidateDefinition`).
Kontrol değişmedi (kutu başına); geri bildirim ek olarak `cipher` dizisi taşır — hücre başına **doğru cevaplanmış** kutunun harfi (tr-TR büyük
harf), diğerleri `null`; yani yalnız öğrencinin bulduğu harfler açılır. Oynatıcı kutuların altında şifre şeridi çizer (hücre altında sorunun
numarası; örnek soruda istemci işareti, çözüm açıkken bütün kelime). Yazarlık görselsiz editörde **Soru + kısa cevap** formunda: satır başına
harf + gizli kelime; kelimenin her harfi o harfi taşıyan soruya bağlanır (aynı harf birden çok soruda varsa sırayla). Tuvalde (görselli) şifre
yazarlığı yok. Migration yok; Aspire'da exam API'yi yeniden başlat.

**Issue #586 dilim e — Claude ile bulmaca ızgarası çıkarma.** `activity-structure` (#579) yeni tip `crossword` taşır (talimat
`Prompt` penceresinde **Çengel bulmaca** sekmesinde, tip revizyonu 1). Claude **hücre** uzayında döner (piksel değil): ızgara
`rows`/`cols` ve kelime yerleri `{dir, row, col, length, number, clue}`; cevap / harf istenmez (#580). Sunucu (`ActivityDrafts.Map(CrosswordDraft)`)
20×20'ye sığmayan, yönü bilinmeyen, < 2 hücrelik ve aynı yön + başlangıçta yinelenen kelimeyi atar; ızgarayı Claude'un boyutu ile
kelimelerin bittiği hücrenin büyüğüne (2..20) getirir, aynı hücreden başlayanlara ilk numarayı verir, boş ipucunu `""` bırakır
(taslak geçersiz: `labeling.activity.invalid.clueText` / `noClues`). Kullanım görselsiz editörde (`/activity-editor`, tür
**Çengel bulmaca**): görsel eklenince **Görselden çıkar (Claude)** bütün görseli tek blok olarak gönderir, form ızgara + kelime yerleri
+ ipuçlarıyla dolar (cevaplar boş, her kelimenin uzunluğu cevap yazılana dek tutulur; farklı uzunlukta cevap uyarı gösterir), yönerge
boşsa basılı yönergeyle dolar; formda yazılı içerik varsa önce onay. Düğme yalnız Admin + QuestionTrainer'da ve `ai-models` boş
değilse görünür (uçların yetkisi, kota ve bütçe #579 ile aynı; kayıtlı sonuç aynı görsel + model için ücretsiz). Düzenlemede kayıtlı
görsel istemcide olmadığından yalnız yeni seçilen görsel gönderilir. Sayfa işaretlemede G bloğu tipsiz kalır (blok tipi değil).
Migration yok; Aspire'da exam API'yi yeniden başlat.

**Issue #587 dilim a — `regionPaint` (bileşen E, boyama; ızgara modu).** Plan
`{"version":1,"type":"regionPaint","mode":"grid","rows","cols","palette":[{id,color,label?}]}` (ızgara 2–20 × 2–20, 1–8 renk; `color`
sabit adlardan: red, orange, yellow, green, blue, purple, pink, brown, black, gray — istemcide `styles.scss` `--paint-<ad>` token'ı, serbest
CSS rengi alınmaz; renkler ve id'ler tekil, `label` isteğe bağlı düz metin ≤ 40). Çözüm `{"counts":{"p1":40,"p2":35}}` (her palet rengine
0..hücre sayısı; 0 = çeldirici renk, kullanılmamalı; toplam ≤ hücre, en az biri > 0); hücrelerin **yeri serbest**. Öğrenci payload'u
`{"grid":["..1..",...]}` (satır başına `cols` karakter, `.` boş, `1`..`8` palet sırası; boyasız = cevap yok). Kontrol renk başına sayı
(`RegionPaintGrader`), geri bildirim `{"rows":[{id,correct}]}` (hedef sayı yazılmaz); analiz: öğe = renk, cevap = boyanan kare sayısı.
Oynatıcı: palet + silgi, dokun / sürükle boyar, aynı renkli kareden başlayan sürükleme siler, renk başına sayaç canlı (hedef gösterilmez);
çözüm açıkken hedef sayılar satır öncelikli örnek dolguyla. Yazarlık görselsiz editörde (tür **Boyama (ızgara)**): boyut + renkler, hedef
kitaptaki gibi kesir (`2/5`), ondalık (`0,35`), yüzde (`%25`) ya da kare sayısı; tam kareye inmeyen hedef reddedilir (kayda yalnız sayı
gider). Testte de çözülebilir (puanlanır). Dilim c: numaralı boyama (aşağıda). Migration yok; Aspire'da
exam API'yi yeniden başlat (yeni kontrolcü).

**Issue #587 dilim b — `regionPaint` bölge modu (görsel üzerinde boyama / daire içine alma).** Plan
`{"version":1,"type":"regionPaint","mode":"regions","style":"fill"|"circle","palette":[...],"regions":[{id,points:[[x,y],...],label?}]}`
(palet ızgarayla aynı kural; 1–100 bölge, bölge = soru görseline göreli piksel çokgen, 3–64 köşe, koordinat 0..100000, alan ≥ 1; bölge
ve palet id'leri birlikte tekil; `style` yalnız görünüm, varsayılan `fill`). Çözüm `{"fills":{"r1":"p1"}}` (boyanacak bölge → renk, en az
bir; yazılmayan bölge boş kalmalı; kullanılmayan palet rengi çeldirici). Öğrenci payload'u `{"fills":{...}}` (boş = cevap yok). Kontrol
`textSelect` kalıbında: hedef doğru renkle boyalıysa bulundu, hedef dışı boyalı bölge fazla; `CorrectCount` = bulunan, `TotalCount` =
hedef + fazla. Geri bildirim yalnız **boyalı** bölgeler (`reason`: `extra` | `color`) ve `found`/`missing`/`extra` (boyanmamış hedef
yazılmaz; fazla > max(3, hedef) ise satırlar gizli, `tooMany`). Analiz: öğe = hedef bölge, cevap = boyandığı renk. Yazarlık **soru
tuvalinde** (tür **Boyama**, bileşen E): dikdörtgen / elips sürükleyerek ya da köşe köşe tıklayarak (ilk köşe ya da **Bitir** kapatır)
bölge çizilir; yeni bölge ve tuvalde bölgeye tık fırçadaki rengi çözüm yapar (fırça "Boş bırak" ise hedeften çıkarır); iç içe bölgede
tık en küçüğüne gider. Oynatıcı görselin üstünde SVG çokgen çizer (tek renkli palette renk seçici gizli; `circle`'da boyalı bölge
elipsle çevrilir); kontrol sonrası doğru yeşil, fazla kırmızı, yanlış renk turuncu + "Bulunan · Kalan · Fazla". Claude sayfa
işaretlemesi E bloğuna tip türetmez. Migration yok; Aspire'da exam API'yi yeniden başlat.

**Issue #587 dilim c — numaralı boyama (MCQ → resim boyama, mandala).** Yeni tip yok: bölge planına isteğe bağlı
`groups:[{id,label?}]` (1–40; grup = soru numarası, id'ler bölge / paletle birlikte tekil) ve her bölgede `group` (grup varken zorunlu,
yokken yasak; her grubun en az bir bölgesi olur). Çözüm `{"fills":{"g1":"p1"}}` — **her** grup → o sorunun doğru cevabının rengi (sorular ve
renkli şıklar görselde, öğrenci cevabı rengi seçerek verir). Öğrenci payload'u `{"fills":{"g1":"p1"}}` (anahtar grup). Kontrol grup başına
(MCQ gibi): satır = grup (plan sırası), boyanmamış grup yanlış, geri bildirim `{"rows":[{id,correct}]}` (beklenen renk yazılmaz, sayaç yok);
analiz: öğe = grup, cevap = renk. Oynatıcı: bölgeye dokunmak o numaralı **bütün** bölgeleri boyar; altta numara listesi (klavyeyle boyama,
kontrol sonrası grup başına ✓/✗). Etkinlikte boyanmamış numara "eksik" sayılır. Yazarlık soru tuvalinde **Boyama** → **Numaralı boyama**:
açınca var olan bölgeler 1. soruya girer; yeni bölge seçili soruya girer, tuvalde bölgeye tık onu seçili soruya taşır; soru başına numara
etiketi ve doğru renk; bölgesi olan soru silinmez. Pilot TT MatLab dosya 57 (8 soru, ~65 bölge, 8 renk). Migration yok; Aspire'da exam
API'yi yeniden başlat.

**Issue #588 dilim a — `numberLine` (bileşen F, sayı doğrusu).** Plan
`{"version":1,"type":"numberLine","divisions","labels":[{at,text}],"cards":[{id,label}],"tolerance?","hideTicks?"}` (bölme 1–100, çentikler
0..divisions; etiket çentik başına en çok bir, düz metin ≤ 30; 1–20 kart, etiket zorunlu ≤ 40; tolerans 0–10 çentik ve bölmeden küçük;
`hideTicks` yalnız görünüm — etiketsiz çentikler çizilmez). Değerler planda yok: öğrenci etiketlerden ve çentik sırasından bulur. Çözüm
`{"positions":{"k1":3,"k2":null}}` (her kart için çentik ya da `null` = çeldirici, doğruya konmamalı; en az bir hedefli kart). Öğrenci
payload'u `{"placed":{"k1":3}}` (bir çentikte birden çok kart olabilir; kartsız = cevap yok). Kontrol kart başına (`NumberLineGrader`):
hedefli kart tolerans içindeyse doğru, çeldirici konmamışsa doğru; geri bildirim `{"rows":[{id,correct}]}` (hedef yazılmaz). Analiz: öğe =
kart, cevap = çentik (etiketi ya da `3/8`), çeldirici işaretli. Oynatıcı: kart seç → çentiğe ya da doğruya dokun (en yakın çentik); doğrudaki
kart ← / → ile kayar, ikinci dokunuş / Delete havuza geri koyar. Yazarlık görselsiz editörde (tür **Sayı doğrusu**): bölme, tolerans,
çentik etiketleri, kart + hedef çentik (boş = çeldirici). Pilotlar: kesir kartları (0–1, 8 bölme), olasılık spektrumu (4 bölme, imkânsız …
kesin), tahmin (100 bölme + tolerans + gizli çentik). Testte de çözülebilir (puanlanır). Sonraki dilimler: grafik kurma, kareli zeminde
kural tabanlı çizim. Migration yok; Aspire'da exam API'yi yeniden başlat (yeni kontrolcü).

**Issue #588 dilim b — `barChart` (bileşen F, grafik kurma).** Plan
`{"version":1,"type":"barChart","style?":"bar"|"line","title?","xLabel?","yLabel?","max","step","unit?","categories":[{id,label}],"fixed?":{"c1":6}}`
(dikey eksen 0..max, max 1–1000; `step` etiketli çizgi aralığı, max'ı böler, ≤ 20 aralık; `unit` öğrencinin en küçük adımı, step'i böler,
≤ 100 adım, varsayılan step; 2–12 kategori, etiket ≤ 30, başlık ≤ 80, eksen adı ≤ 40, düz metin; `style` yalnız görünüm). `fixed` önceden
çizilmiş, değiştirilemeyen ve kontrol edilmeyen sütunlar ("grafiği tamamla"); en az bir kategori serbest. Veri tablosu planda yok — yönergede
ya da görselde. Çözüm `{"values":{"c2":4}}` (her serbest kategoriye unit katı 0..max; sabit kategori yazılmaz). Öğrenci payload'u
`{"values":{"c2":4}}` (yalnız serbest kategoriler; çizilmeyen sütun 0 sayılır; boş = cevap yok). Kontrol kategori başına eşitlik
(`BarChartGrader`), geri bildirim `{"rows":[{id,correct}]}` (hedef yazılmaz); analiz: öğe = serbest kategori, cevap = çizilen değer.
Oynatıcı: sütunun üstünde seviyeye dokun (en yakın unit katı), aynı seviyeye ikinci dokunuş siler; sütun klavyede kaydırıcı (↑/↓ unit,
PageUp/PageDown step, Home/End, Delete); çizgi görünümünde nokta + çizgi; sabit sütunlar gri. Yazarlık görselsiz editörde (tür **Grafik
kurma**): görünüm, başlık, eksen adları, eksen, kategori + değer + "Hazır". Daire grafiği ve kareli zeminde çizim sonraki dilimlerde. Testte de
çözülebilir (puanlanır). Migration yok; Aspire'da exam API'yi yeniden başlat (yeni kontrolcü).

**Issue #588 dilim c — `gridDraw` (bileşen F, kareli zeminde kural tabanlı çizim).** Plan
`{"version":1,"type":"gridDraw","rows","cols","grid?":"square"|"dot","shape?":"rectangle"|"square"}` (zemin 2–30 × 2–30 birim kare;
`grid` yalnız görünüm, `shape` `square`'de yalnız kare geçerli). Kural planda yok, **çözümde**: `{"area?","perimeter?","all"}` (alan
1..rows×cols, çevre 4..2×(rows+cols), en az biri; geçerli boyut = kenarları doğal sayı, koşulları sağlayan ve zemine — döndürülerek de —
sığan dikdörtgen; en az bir boyut, `all`'da en çok 12). Yönergede de yazılır ("çevresi 14 cm olan dikdörtgenlerin tamamını çiziniz").
Öğrenci payload'u `{"shapes":[{row,col,width,height}]}` (zemine sığan, ≤ 30; boş = cevap yok). Kontrol (`GridDrawGrader`) `textSelect`
kalıbında: gereken = `all` ? geçerli boyut sayısı : 1, bulunan = çizilen farklı geçerli boyut (en çok gereken), fazla = kurala uymayan çizim;
`CorrectCount` = bulunan, `TotalCount` = gereken + fazla; yer ve yön serbest (3×4 = 4×3), aynı boyutun tekrarı ceza değil (`duplicate`).
Geri bildirim `{"shapes":["ok"|"wrong"|"duplicate"],"found","missing","extra"}` (eksik boyutlar yazılmaz). Analiz: `all`'da öğe = boyut
("3 × 4"), değilse tek öğe (cevap = ilk geçerli çizimin boyutu). Oynatıcı: sürükleyerek ya da iki dokunuşla (köşe → karşı köşe) çizim,
çizime dokunmak siler, üst üste binme yok; klavyede ok + Enter / Delete / Escape; kontrol sonrası yeşil / kırmızı / turuncu (tekrar) +
"Bulunan · Kalan · Fazla"; çözümde kurala uyan boyutlar örnek yerleşimle. Yazarlık görselsiz editörde (tür **Kareli zeminde çizim**):
zemin, kareli / noktalı, dikdörtgen / kare, alan, çevre, "Hepsini çizsin"; uyan boyutlar formda listelenir. Pilotlar: TT MatLab dosya 87
(çevresi 14 cm dikdörtgenler, çevresi 20 cm kare), dosya 93 (alanı 20 m² dikdörtgenler). Testte de çözülebilir (puanlanır). Daire grafiği /
nokta grafiği ayrı dilim. Migration yok; Aspire'da exam API'yi yeniden başlat (yeni kontrolcü).

**Issue #588 dilim d — veri grafikleri (daire grafiği, nokta grafiği).** Yeni tip yok. **Daire grafiği** `regionPaint`'e
`mode:"pie"`: plan `{"version":1,"type":"regionPaint","mode":"pie","slices","palette"}` (2–36 eş dilim; palet ızgarayla aynı kural).
Tek satırlık ızgara gibi okunur: çözüm `{"counts":{"p1":4}}` (renk başına dilim sayısı, yer serbest), payload `{"grid":["..1.2..."]}`
(bir satır, `slices` karakter; dilim 1 tepeden saat yönünde), kontrol / geri bildirim / analiz ızgarayla aynı (`RegionPaintGrader`).
Oynatıcı daireyi SVG dilimlerle çizer (dokun / sürükle boyar, aynı renkten başlayan sürükleme siler, klavyede Enter / Space).
Yazarlık görselsiz editörde **Boyama (ızgara / daire)** → Şekil **Daire grafiği**: dilim sayısı + renk başına hedef (kesir `1/4`,
yüzde `%37,5`, ondalık ya da dilim sayısı; tam dilime inmeyen hedef reddedilir). **Nokta grafiği** `barChart`'a `style:"dot"`: her
`unit` bir nokta (birim aralığının ortasında), `max / unit` ≤ 20; dokunulan yüksekliğe kadar nokta, en üstteki noktaya dokunmak bir
nokta kaldırır (klavye ↑ / ↓ aynı). Kontrol değişmedi. Editörde **Grafik kurma** → Görünüm **Nokta grafiği**. Pilotlar: TT MatLab
dosya 162 (16 eş dilim), dosya 160 (nokta grafiği). Migration yok; Aspire'da exam API'yi yeniden başlat.

**Issue #632 — `balanceScale` (terazi dengeleme; karar #589).** Plan
`{"version":1,"type":"balanceScale","unit?","left?":[{id,value,label?}],"right?":[...],"cards":[{id,value,label?}],"hideTilt?"}`
(`left`/`right` kefelerdeki hazır, oynatılamaz yükler, kefe başına 0–6; `cards` öğrencinin ağırlıkları 1–12; `value` tam sayı 1–10000 ve
öğrenciye gider — kitapta ağırlığın üstünde yazar; `label` isteğe bağlı düz metin ≤ 30, yoksa değer + birim; `unit` ≤ 10; bütün id'ler
tekil; `hideTilt` yalnız görünüm — terazi kontrol edilene dek düz durur). Çözüm kuraldır: `{"useAll":bool}` — kefe toplamları eşitse denge
(birden çok geçerli yerleşim), `useAll`'da bütün kartlar kefede, değilse en az bir kart. Kayıtta kurala uyan bir yerleşim aranır
(`BalanceScaleGrader.ValidateDefinition`, ulaşılabilir farklar kümesi); yoksa reddedilir. Öğrenci payload'u `{"left":[kart],"right":[kart]}`
(kart en çok bir kefede; iki kefe boş = cevap yok). Kontrol tek satır (`balance`), geri bildirim `{"rows":[{id:"balance",correct}],"tilt":"level|left|right","unused"?}`
(`unused` yalnız `useAll`'da). Analiz: tek öğe, cevap = yerleşim ("5 kg + 14 kg | 9 kg + 10 kg"; hazır yük yoksa ayna yerleşim aynı cevap).
Oynatıcı: ağırlık seç → kefeye dokun (ya da "Sol/Sağ kefeye"), kefedeki ağırlığa ikinci dokunuş havuza geri koyar; terazi canlı eğilir,
toplamlar yazılmaz; çözüm açıkken kurala uyan bir yerleşim. Yazarlık görselsiz editörde (tür **Terazi dengeleme**): birim, ağırlıklar
(değer + etiket), hazır yükler, "Hepsini kullansın", "Terazi kontrol edilene dek düz dursun"; form örnek çözümü gösterir. Pilot Süper Combo
Mat s.203 2a/2b. "?" bilinmeyenli teraziler (1. soru) sayı cevabıdır (`slotFill`). Testte de çözülebilir (puanlanır). Migration yok; Aspire'da
exam API'yi yeniden başlat (yeni kontrolcü).

**Issue #634 — `clockSet` (saat kurma; analog saat, akrep + yelkovan).** Plan
`{"version":1,"type":"clockSet","step?":1|5|15,"numbers?","minuteTicks?","clocks":[{id,label?,fixed?}]}` (1–6 saat; `step` yelkovan adımı,
varsayılan 5; `numbers` / `minuteTicks` yalnız görünüm, varsayılan açık; `label` düz metin ≤ 40; `fixed` = `"HH:MM"` önceden kurulmuş,
oynatılamaz, kontrol edilmeyen saat — ör. süre sorusunun başlangıcı; en az bir saat serbest). Çözüm `{"times":{"c1":"03:45"}}` (her serbest
saate, dakika adımın katı; 12 saatlik kadran: 15:45 = 3:45). Öğrenci payload'u `{"clocks":{"c1":{"h":225,"m":45}}}` — `h` akrebin konumu
12 saatlik kadranda dakika cinsinden (0..719), `m` yelkovan (adımın katı); kurulmamış saat yazılmaz, saatsiz = cevap yok. Karar #634:
akrep yelkovana bağlı **değil**, öğrenci ikisini ayrı çevirir. Kontrol saat başına (`ClockSetGrader`): yelkovan hedef dakikada ve akrep
hedef konumun ±15 birimi (±7,5°, saat aralığının çeyreği) içinde; geri bildirim `{"rows":[{id,correct}]}` (hedef yazılmaz). Analiz: öğe =
serbest saat, cevap = gösterilen saat (`"3:45"`; saat yelkovana göre akrebin en yakın tam konumundan, kabul aralığı dışında `"?:45"`).
Oynatıcı: SVG kadran; Akrep / Yelkovan seçici, kadrana dokunmak / sürüklemek seçili eli çevirir (ele dokunmak onu seçer), akrep 2,5° adımla
oturur; her el klavyede kaydırıcı (← / → bir adım, PageUp / PageDown yelkovanda 15 dk, akrepte bir saat). Etkinlikte kurulmamış saat
"eksik" sayılır. Yazarlık görselsiz editörde (tür **Saat kurma**): adım, rakam / dakika çizgisi, saat başına etiket + saat (`3.45`,
`15:45`) + "Kurulu". Dijital gösterim ve saati okuma `slotFill` ile kalır. Testte de çözülebilir (puanlanır). Migration yok; Aspire'da exam
API'yi yeniden başlat (yeni kontrolcü).

**Issue #684 — etkinlik madde listesi, kaldırma başlamış oturumu etkilemez.** Etkinlik detayında (öğretmen, "Çalışma takibi"nin
üstünde) **Maddeler** kartı (`app-activity-item-list`): `GET api/questions/bytest/{id}` ile sıra no, küçük resim, tür rozeti, yönerge
önizlemesi; düzenleyebilene görselsiz maddede Düzenle (`/activity-editor?...&questionId=`), görselli maddede yalnız "Görselli,
düzenlenemez"; Kaldır = `DELETE api/questions/test/{testId}/question/{questionId}` (bağ soft-delete, soru havuzda kalır, yalnız sahip /
admin). Sıralama: #685 (aşağıda). `WorksheetQuestion`'ın soft-delete filtresi artık **adlı** (`WorksheetQuestionQueryFilter.SoftDeleteKey`,
#376 slot kalıbı): zorunlu `WorksheetInstanceQuestion.WorksheetQuestion` gezinmesi kaldırılan maddeyi başlamış / bitmiş oturumdan
düşürüyordu. Oturum satırından okuyan sorgular (oynatıcı, save-answer, kontrol, çözümler, sonuç, öğrenci / veli puanları, detay
istatistikleri, "yanlışlarım", #584 analiz, açık uçlu kuyruk, çalışma bağlantıları) `.WithRemovedWorksheetItems()` kullanır; sayfanın
güncel maddelerini okuyanlar (start-test kopyası, liste, numaralama, ekleme / bağlama) kullanmaz. Yeni bir oturum okuma yolu yazarken
aynısını yap, yoksa kaldırılan madde o yoldan kaybolur. Analizde kaldırılan madde yalnız onu çözmüş analiz oturumu varsa görünür.
Migration yok.

**Issue #685 — etkinlik maddelerini yeniden sıralama, oturum sırası sabit.** Madde listesinde (#684) düzenleyebilen
öğretmen maddeyi tutaçtan sürükler (`cdkDragHandle`; 480 px altında gizli) ya da ↑ / ↓ ile taşır (uçta pasif, `disabledInteractive`:
odak düğmede kalır; `aria-live` "Madde 2, 3. sıraya taşındı"). Liste iyimser güncellenir; `PUT api/questions/test/{testId}/order`
`{questionIds}` bütün sırayı yazar (`QuestionTestLinkService.ReorderAsync`: tek transaction, bağlamayla aynı test kilidi
`AcquireWorksheetQuestionLinkLockAsync`, `Order` = 1..n). Liste testin sorusu silinmemiş bütün maddeleriyle birebir eşleşmeli
(eksik / fazla / yinelenen → 400, `TestQuestionsMismatch`); sorusu silinmiş madde sona gider. Yetki: sahip / admin; görebilen ama
sahibi olmayan 403, göremeyen 404. Hata olursa eski sıra geri gelir, snackbar "Sıra kaydedilemedi" [Tekrar dene]; ilk değişimde
"yalnız bundan sonra başlayan öğrencilere" notu. **Oturum sırası:** `WorksheetInstanceQuestion.Order` start-test'te sayfanın o anki
`(Order, Id)` sırasından 1..n kopyalanır; oturum okuma yolları (oynatıcı, sonuç, çözümler, açık uçlu kuyruk `ItemNumber`, "yanlışlarım",
çalışma bağlantıları) `(tiq.Order, tiq.Id)` ile sıralar — yeni bir oturum okuma yolu yazarken sayfanın `WorksheetQuestion.Order`'ını
değil bunu kullan. #584 analizi madde kolonlarını sayfanın güncel sırasıyla gösterir (oturum başına değil). Migration
`AddTestInstanceQuestionOrder` mevcut oturumları o anki görüntü sırasıyla doldurur (silinmiş / kaldırılmış satırlar dahil);
exam API açılışında uygulanır (Aspire'da exam API'yi yeniden başlat).

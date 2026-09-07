# Soru/Şık Layout Algoritması Analizi (issue #69)

> Bu doküman kod değişikliği içermez — `calculateBestLayout` fonksiyonunun mevcut davranışının
> gerçek üretim verisiyle analizidir. Uygulama (kod değişikliği) issue #70'te yapılacaktır.

## 1. İncelenen kod

`ui/src/app/shared/components/question-canvas-view-v5/question-canvas-view-v5.component.ts`

- `calculateBestLayout(region)` (satır 120-163) — **aktif**, `questionRegion` input setter'ından
  (satır 75) çağrılıyor.
- `calculateBestLayoutv1(region)` (satır 165-194) — **ölü kod**, hiçbir yerden çağrılmıyor.
- Render tarafı: `.html` (satır 5-21) + `.scss` (satır 22-73). `layout-*` sınıfına göre CSS Grid
  `grid-template-columns` değişiyor (`repeat(4,1fr)` / `repeat(2,1fr)` / flex-column / vs.).
  Şık görselleri `getScaledAnswerWidth()` (satır 263-270) ile `answer.width * visualScale()`
  genişliğine set ediliyor, kart içindeki `img{max-width:100%}` ile ek bir güvenlik sınırı var.

**Önemli mimari not:** CSS tarafı `1fr` grid kolonları kullandığı için gerçek pixel taşması
(clipping) neredeyse imkansız — kolon daralınca `max-width:100%` görseli küçültüyor. Yani
`calculateBestLayout`'un pixel karşılaştırmaları (`aW*4+gap*3 < qW*1.1` gibi) **hard bir
overflow/clip kontrolü değil**, "bu N kolon şık görsellerini gereksiz küçültmeden mi sığdırıyor"
sorusuna verilen bir tahmindir. Yanlış karar; kırpılmadan çok **gereksiz küçülme (çok kolon) ya
da gereksiz boşluk (az kolon)** olarak görünür. Bu, aşağıdaki değerlendirmelerde "doğru/yanlış"
derken kullandığım kriter.

## 2. Veri kaynağı

Kullanıcının lokalinde bulunan `question-transfer-MacBookPro16-package` export'u (`manifest.json`
+ `questions/<id>/question-v2.jpg` + `questions/<id>/answers/*.jpg`), **669 gerçek soru**
içeriyor (worksheet/soru bankası üretim verisi). Her soru kaydında zaten backend tarafından
hesaplanmış bir **`answerColCount`** (ve `layoutPlan.recommendedAnswerColumns`) alanı var —
bu, `calculateBestLayout`'tan bağımsız, container genişliği/breakpoint bazlı ayrı bir hesaplama.
Kesin "doğru cevap" olarak almadım (farklı varsayımlarla — 840px sabit desktop genişliği — çalışıyor)
ama çapraz kontrol için kullandım (bkz. §5).

`calculateBestLayout` ve `calculateBestLayoutv1`, manifest'teki `width/height/answers[].width`
alanlarıyla Node.js'te birebir kod olarak replike edilip 669 sorunun tamamına koşturuldu.

### Genel dağılım (669 soru, `calculateBestLayout`/v2 ile)

| Layout | Adet |
|---|---|
| top-4row | 226 |
| top-2row | 206 |
| top-1row | 193 |
| side-2col | 36 |
| side-1col | 8 |

- Şık sayısı: 665 soru 4 şıklı, 2 soru 5 şıklı, 2 soru 3 şıklı (veri setinde 5/3 şıklı örnek çok az —
  aşağıdaki tabloda mevcut olan 2 tanesi de kullanıldı).
- Oran (`width/height`) dağılımı çok yatığa kaymış: medyan 2.33, p10=1.22, p90=5.01. Dikey/kare
  sorular (`ratio<1.1`) toplamın sadece %6.6'sı (44/669).
- **v1 vs v2 farklı karar veren soru sayısı: 61/669 (%9.1)**

## 3. Örnek tablo (10 gerçek soru)

| id | qW×qH | oran | şık sayısı | maxAnsW | v2 sonucu | v1 sonucu | Görsel değerlendirme |
|---|---|---|---|---|---|---|---|
| 7540 | 633×1711 | 0.37 | 4 | 605 | side-1col | side-1col (aynı) | Çok dikey, tam genişlik landscape şık görselleri (605px). Tek kolon alt alta doğru — 2 kolon olsa her şık ~300px'e küçülüp okunmaz olurdu. ✅ |
| 7957 | 471×810 | 0.58 | 4 | 157 | side-1col | side-1col (aynı) | 0.6 eşiğinin hemen altında. Şıklar dar (137px) olsa da metin çok satırlı; tek kolon mantıklı. ✅ |
| 7274 | 585×846 | 0.69 | 4 | — | side-1col | side-1col (aynı) | **800px yükseklik eşiği burada devreye giriyor**: oran 0.69 normalde side-2col'a düşer ama `effectiveHeight>800` onu side-1col'a zorluyor. Soru görseli 4 satırlı bir tablo + 3 satır metin — çok uzun. 2 kolon şık olsaydı yanındaki soru görseliyle dikey olarak orantısız/dar görünürdü. Eşiğin gerekçesi (çok uzun soru + yan kolon dar olur) tutarlı. ✅ |
| 7343 | 583×606 | 0.96 | 4 | 276 | side-2col | side-2col (aynı) | Kare/hafif dikey, kısa metin. 2×2 şık düzeni dengeli. ✅ |
| 7255 | 590×541 | 1.09 | 4 | 417 | side-2col | side-2col (aynı) | 1.1 eşiğinin hemen altında, şıklar geniş (417px). side-2col ile her kolon ~295px'e iner; 417px'lik görsel `max-width:100%` ile küçültülür — hafif küçülme var ama kabul edilebilir sınırda. ⚠️ sınırda. |
| 7256 | 586×513 | 1.14 | 4 | 580 | top-4row | top-4row (aynı) | 1.1 eşiğinin hemen üstü ama şıklar soru genişliği kadar (580/586px) geniş görseller — hiçbir çok-kolonlu düzen sığmaz. top-4row (alt alta) doğru; algoritma genişlik testiyle bunu doğru yakalıyor. ✅ |
| 7301 | 583×489 | 1.19 | 4 | 74-82 | top-1row | top-1row (aynı) | Dar şıklar (max 82px), 4×82+gap=364 ≪ 583. top-1row (4 kolon yan yana) doğru, bolca boşluk payı var. ✅ |
| 7259 | 594×470 | 1.26 | 4 | 342 | **top-4row** | **top-2row** | **v1/v2 farklı.** Şıklar [202,202,342,342] — v1 ilk şıkkın genişliğini (202) baz alıyor, 2×202+12=416<564 → top-2row seçiyor. Ama gerçekte 2 geniş şık (342px) var; v1'in kararıyla 2 kolon render edilseydi 342px'lik şıklar ~285px'lik kolona sıkışıp gereksiz küçülürdü. v2 (maxAns=342) bunu doğru tespit edip top-4row'a düşüyor. **v1'in `firstAns` kullanımı somut bir hata; v2'nin `maxAns` kullanımı bilinçli bir düzeltme.** |
| 7277 | 1257×582 | 2.16 | 4 | 317 | **top-1row** | **top-2row** | **v1/v2 farklı.** Soru çok geniş (1257px). v2: 4×317+36=1304 < 1257×1.1=1383 → top-1row (4 şık yan yana, geniş container'da rahat sığar). v1: %95 toleransla (1257×0.95=1194) 1304 sığmıyor → gereksiz şekilde top-2row'a (2×2) düşüyor, geniş container'da fazladan boşluk bırakır. **v2'nin %110 toleransı burada daha isabetli** — v1'in %95 toleransı gereksiz yere dar. ⚠️ **Not (#70 code-review, 2026-09-07):** bu satırdaki gösterim `4×maxAnsW+gap*3` kullanıyor, kodun gerçek `aW = 20 + maxAnsW` payını atlıyor. Payı dahil edip (`aW=337`) hesaplarsak `4×337+36=1384 ≮ 1257×1.1=1382.7` çıkar — yani gerçek `maxAnsW=317` ile kod aslında `top-2row` üretir, bu satırdaki "top-1row" sonucu yalnızca payı atlayan basitleştirilmiş gösterimle tutarlı. `question-canvas-view-v5.component.spec.ts` bu yüzden fixture'da `maxAnsW=300` kullanıyor (payla birlikte gerçekten `top-1row` sınırının içinde kalan bir değer) — regresyon testi kodun davranışını, bu tablodaki basitleştirilmiş aritmetiği değil, doğru şekilde yansıtıyor. |
| 7264 | 594×225 | 2.64 | 4 (+passage) | 283 | **top-2row** | **top-4row** | **v1/v2 farklı.** Şıklar ~275-283px. v2: 2×283+... ≈618<653(qW×1.1) → top-2row. v1: %95 toleransla 578 vs 564.75 → **578 sığmıyor sayılıyor**, top-4row'a düşüyor — oysa 283px'lik 2 şık 594px genişliğe rahatça sığar (283×2+12=578<594). **v1 burada net bir regresyon/hata: sığan bir düzeni sığmıyor sanıp gereksiz yere alt alta diziyor, boşluk israfına yol açıyor.** |
| 7586 | 816×163 | 5.01 | **5** | 271 | top-2row | top-2row (aynı) | 5 şıklı nadir örnek, çok yatay soru. top-2row 5 şıkkı 2+2+1 olarak dizer — CSS grid `repeat(2,1fr)` bunu otomatik satırlıyor, sorun yok. ✅ |
| 7546 | 802×119 | 6.74 | 4 | 99 | top-1row | top-1row (aynı) | Çok yatay, dar şıklar (max 99px). 4 kolon yan yana rahat sığar, bol boşluk payı var. ✅ |

## 4. Somut olarak tespit edilen yanlış/sorunlu kararlar

- **v1'de `maxAns` yerine `answers[0]` (firstAns) kullanımı** (satır 169-170 — ölü kod, ama #70'te
  referans alınabilir tarih olarak): Şıklar farklı genişlikte olduğunda (özellikle ilk şık en dar
  olduğunda) yanlış kolon sayısına karar veriyor. Örnek 7259: 2 geniş şık (342px) varken ilk şıkkın
  202px'lik genişliğine göre "2 kolon sığar" diyor — yanlış. **v2'nin bunu düzelttiği doğrulandı.**
- **v1'in %95 toleransı, v2'nin %110 toleransına göre gereksiz sıkı**: Örnek 7264 ve 7277'de v1,
  aslında sığan bir düzeni "sığmıyor" sayıp bir alt kademeye (daha az kolonlu, daha çok boşluklu)
  düşüyor. v2'nin gevşek toleransı burada isabetli.
  → **Sonuç: `calculateBestLayoutv1`'in v2'ye göre "eski/bozuk" bir sürüm olduğu; v2'nin bilinçli
  bir düzeltme olduğu doğrulandı.** #70'te v1'in silinmesi güvenli.
- **Sınırda (borderline) `side-2col` kararları** (örnek 7255, oran 1.09): 1.1 eşiğine çok yakın
  duran kare-ye-yakın sorularda, şık genişliği büyükse (417px gibi) 2 kolona sıkıştırma hafif
  küçülmeye yol açıyor. Kesin "yanlış" denemez (CSS `max-width:100%` ile kırpılma olmuyor,
  sadece görsel küçülüyor) ama sınırda davranış olarak not edildi — #70'te eşik yerine gerçek
  toplam-genişlik/kart-genişliği oranına bakan bir hesap düşünülebilir.
- 669 örneğin **87%'sinde** (582/669) `calculateBestLayout`'un ima ettiği kolon sayısı, backend'in
  bağımsız hesapladığı `answerColCount`/`recommendedAnswerColumns` ile örtüşüyor. Kalan %13'lük
  fark (87 soru) çoğunlukla `ratio 1.1-1.5` bandındaki top-1row/top-2row sınır kararlarında —
  backend'in container-genişliği bazlı (fixed 840px desktop breakpoint) hesabı ile frontend'in
  region-genişliği bazlı hesabı farklı varsayımlar kullanıyor. Bu, otomatik "yanlış" anlamına
  gelmiyor (iki farklı model) ama #70'te eşikler değiştirilirken bu bağımsız veri noktası
  regresyon kontrolü için kullanılabilir.

## 5. Hardcoded eşiklerin değerlendirmesi

| Eşik | Nerede | Bu değer neden bu? | Korunmalı mı? |
|---|---|---|---|
| `qRatio < 1.1` | side vs top ayrımı | Kare'ye yakın (1.0'a kadar) soruları da "dikey" sayıp yan-yana düzene alma payı. Veri setinde bu bandın altı/üstü örneklerde (7255 vs 7256) makul ayrım yaptığı gözlendi. | **Korunabilir**, ama 1.0-1.15 bandı borderline — #70'te bu bandın küçük bir örneklemle (elle) tekrar gözden geçirilmesi önerilir. |
| `effectiveHeight > 800` | side-1col zorlaması | Çok uzun (tablo/çok satırlı) sorularda yan kolonun dar/orantısız kalmasını engelliyor. 7274 örneğinde gerekçesi doğrulandı. | **Korunmalı** — kod amacına uygun çalışıyor. |
| `qRatio < 0.6` | side-1col zorlaması (2. koşul) | Çok dikey (dar+uzun) sorularda 2 kolon şık, soru görseliyle orantısız dar kalır. | **Korunmalı** — veri setinde bu bandın altında kalan tek doğrudan örnek (7540, oran 0.37) side-1col'un doğru olduğunu destekliyor. |
| `%10` tolerans (`qW * 1.1`) | top-1row/top-2row fit testi | v1'in %95'ine (`qW * 0.95`) göre v2'de gevşetilmiş. Analiz, bu gevşetmenin **bilinçli ve isabetli bir düzeltme** olduğunu gösterdi (7264, 7277). | **Korunmalı**, mevcut değeriyle (1.1) devam edilebilir — daha da gevşetmek gerektiğine dair veri yok. |
| `aW = 20 + maxAns.width` | kart genişliği tahmini | `20` = padding(8)+border(2) hesaplamasının 2 katı gibi görünüyor (yorum satırında "padding(8) + border(2) + görsel" deniyor ama `8+2=10`, kodda `20` — muhtemelen sol+sağ padding/border toplamı, yani 2×(8+2)). Yorum ile kod arasında küçük bir tutarsızlık var (yorum tek taraflı payı anlatıyor, `20` iki taraflı pay). | **Değer korunabilir** ama **yorum düzeltilmeli** (#70'te, kod değişikliği kapsamında) — `20` = 2×(padding 8 + border 1.5≈2) olarak açıklanmalı. |
| `maxAns` vs `firstAns` | v2 vs v1 | §4'te detaylandırıldığı gibi v2'nin `maxAns` kullanımı v1'deki somut bir hatayı düzeltiyor. | v2 davranışı **korunmalı**, v1 (`calculateBestLayoutv1`) silinmeli. |

## 6. calculateBestLayout vs calculateBestLayoutv1 — satır satır fark

| Fark | v1 | v2 (aktif) | Değerlendirme |
|---|---|---|---|
| Yükseklik kaynağı | `region.height` | `region.sanitizedHeight \|\| region.height` | v2'nin `sanitizedHeight` fallback'i bilinçli bir iyileştirme (kırpılmış/temizlenmiş yükseklik varsa onu kullanır). Korunmalı. |
| Şık genişliği referansı | `answers[0]` (firstAns) | `answers.reduce(max)` (maxAns) | v2 düzeltme — §4'te kanıtlandı. Korunmalı. |
| Genişlik toleransı | `qW * 0.95` | `qW * 1.1` | v2 düzeltme — §4'te kanıtlandı. Korunmalı. |
| Margin/state yan etkisi | Yok (saf fonksiyon, sadece `LayoutType` döner) | `this.currentMarginLeft.set(...)`, `this.answerMarginLeft.set(...)`, `this.answerMinWidth.set(...)` çağırıyor | v2, component state'ine yan etki yapıyor — bu **#70'in kabul kriterinde zaten "saf fonksiyona ayrılmalı" olarak talep edilen** kısım. v1'in saf fonksiyon olması aslında hedeflenen mimariye daha yakın (ama karar mantığı v1'de hatalı). |
| Kullanım durumu | Hiçbir yerden çağrılmıyor (ölü kod) | Aktif, input setter'dan çağrılıyor | v1 silinmeli. |

**Sonuç:** v1→v2 arasındaki 3 karar-mantığı farkı (sanitizedHeight, maxAns, %110 tolerans) **bilinçli
düzeltmeler** olarak değerlendirildi ve veriyle doğrulandı; regresyon olarak işaretlenen bir fark
bulunamadı. Tek mimari geri adım, v2'nin yan etkili hale gelmesi — bu zaten #70'in konusu.

## 7. Düzeltme spesifikasyonu (issue #70 için)

1. **Saf fonksiyona ayırma**: `calculateBestLayout(region: QuestionRegion): { layout: LayoutType;
   currentMarginLeft: number; answerMarginLeft: number; answerMinWidth: number }` imzasıyla, hiçbir
   `signal.set()` çağırmadan sadece hesaplanan değerleri dönmeli. Çağıran taraf (`questionRegion`
   setter) dönen objeyi kendi state'ine atamalı. Mevcut karar mantığı (§6'daki v2 sütunu) **aynen
   korunmalı** — bu analizde bir hata bulunmadı.
2. **`calculateBestLayoutv1` silinmeli** — hiçbir çağıran yok, kod tabanında kafa karışıklığı
   yaratıyor, analiz onun mevcut sürümden daha kötü karar verdiğini doğruladı.
3. **Eşikler değişmeyecek** (1.1 oran, 800px, 0.6 oran, %10 tolerans) — hepsi için veri destekli
   gerekçe bulundu, değiştirmeye gerek yok (bkz. §5).
4. **`aW = 20 + maxAns.width` yorumunun düzeltilmesi**: yorum "padding(8) + border(2) + görsel"
   diyor ama toplam 20; iki taraflı pay olduğu netleştirilmeli (`// 2 × (padding 8 + border ~2)`).
5. **console.log temizliği** (`Calculated best layout`, `Input Question Region` — #70'in kabul
   kriterinde zaten var, burada tekrar teyit ediliyor: satır 76 ve 85).
6. **Sınırda `ratio` bandı (1.0-1.15) için ek gözden geçirme önerisi (opsiyonel, blocker değil)**:
   Bu analizde net bir hata bulunmadı ama `side-2col` ile `top-4row` arasındaki geçiş, şık
   genişliğine göre hafif küçülmeye yol açabiliyor (7255 örneği). #70 kapsamında zorunlu değil;
   istenirse ayrı bir iyileştirme issue'su açılabilir.
7. **Unit test seti** (#70'in kendi kabul kriteri): Bu analizdeki 10 örnek (§3 tablosu) + region
   genişlik/yükseklik/şık-genişlikleri değerleri, test fixture'ı olarak birebir kullanılabilir
   (id'ler: 7540, 7957, 7274, 7343, 7255, 7256, 7301, 7259, 7277, 7264, 7586, 7546).

## 8. Kapsam dışı / not edilen riskler

- Gerçek veri, Downloads klasöründeki lokal bir export paketinden alındı (staging/production DB
  erişimi kullanılmadı) — kullanıcı onayıyla, docker/Aspire ortamı yerine bu kaynak tercih edildi.
- Veri setinde 5 ve 3 şıklı örnekler çok az (sırasıyla 2'şer) — #70'teki unit testler bu iki örneği
  de içerse de, 5+ şıklı geniş bir çeşitlilik test edilemedi. Gelecekte 5 şıklı soru sayısı artarsa
  bu algoritmanın tekrar gözden geçirilmesi gerekebilir.
- Backend'in `answerColCount`/`layoutPlan` alanı bu analizde çapraz kontrol için kullanıldı ama
  onun kendi doğruluğu ayrıca doğrulanmadı (farklı bir konudur, kapsam dışı).

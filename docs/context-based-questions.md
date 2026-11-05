# Bağlam temelli sorular ve beceri etiketleri (#703)

## Kaynak

**Türkiye Yüzyılı Maarif Modeli Bağlam Temelli Çoktan Seçmeli Soru Yazım Kılavuzu**, MEB, Mart 2026, ISBN 978-975-11-9562-3.
Duyuru: meb.gov.tr haber 40284 (1 Nisan 2026). PDF:
`https://www.meb.gov.tr/meb_iys_dosyalar/2026_04/01080317_TUYRKIYYE_YUYZYILI_MAARIYF_MODELIY_SORU_YAZIM_TEKNIYKLERIY17_MART.pdf`.
Kılavuz kaynak gösterilerek alıntılanabilir; çoğaltılamaz. Bu dosya özettir, kılavuzun metnini ya da örnek sorularını repoya koymayız.
Dayandığı araştırma raporu: `https://tymm.meb.gov.tr/upload/kilavuz/becerilerin-olculmesine-iliskin-arastirma-raporu.pdf` (okunmadı).

## Kılavuzun özeti

- **Amaç:** öğrenme çıktılarını ölçen soruları standartlaştırmak; soru ve ders kitabı yazarları ile öğretmenler için. 12 ilde pilot
  uygulama ve öğrencilerle sesli düşünme görüşmeleriyle hazırlanmış. Örnekler problem çözme, sorgulama ve her ders için seçilen bir alan
  becerisi üzerinedir; aynı yaklaşım diğer becerilere de uygulanabilir denir.
- **Bağlam temelli soru nedir:** öğrenci bir metin, problem durumu, veri seti, görsel ya da senaryo ile karşılaşır ve onu kullanarak
  karar verir, yorumlar, çözüm üretir ya da açıklar. Bu bir soru biçimi değil, bir tasarım yaklaşımıdır: çoktan seçmeli, doğru-yanlış,
  eşleştirme, kısa ya da uzun yanıtlı olabilir. Belirleyici olan bağlam ile soru arasındaki işlevsel ilişkidir.
- **İşlevsel bağlam testi:** soru bağlamı incelemeden, yalnız ön bilgiyle ya da seçeneklerden giderek çözülebiliyorsa bağlam işlevsizdir
  ve soru yeniden kurgulanır. Örnek hata: küresel ısınma metninin ardından "hangisi sera gazıdır?" diye sormak.
- **Yazım süreci:** (1) ölçülecek öğrenme çıktısı ve süreç bileşenini belirle, (2) günlük hayatla ilişkili bağlam kur, (3) süreç
  bileşenini ölçen soruları yaz, (4) güçlü çeldiriciler kur, (5) bağlamın işlevselliğini sına; çözülebiliyorsa başa dön.
- **Bağlam:** yaşa ve sınıfa uygun, herkesin erişebildiği (okul, park, alışveriş; golf ya da borsa değil), tarafsız. Gerçek veri
  kaynakları önerilir (TÜİK, MGM, AFAD, NASA, FAO, birincil tarih kaynakları, müze objeleri, etik ikilemler).
- **Kök ve seçenekler:** kök beceriyi harekete geçirir. Çeldiriciler kavram yanılgılarından seçilir. "Hepsi" ya da "hiçbiri"
  kullanılmaz. Seçenekler uzunluk ve biçimce benzer olur. Bir bağlama bağlı sorular birbirine ipucu vermez.
- **Bilişsel yük:** dekoratif görsel ve gereksiz karmaşık cümle olmaz. Soru okuma-anlamayı değil hedef beceriyi ölçer.
- **Kontrol listesi** başlıkları: bağlam seçimi, soru ve seçenekler, etik ve tarafsızlık, dil ve anlatım. **Soru yazım formu:** ders,
  sınıf, beceri, süreç bileşenleri ve gerekçesi, bağlam adı, yararlanılan kaynak, güçlük tahmini, yazar, her soru için süreç bileşeni
  ve doğru cevap.

## Beceri çerçevesi (TYMM kavramsal beceriler)

Kılavuz beceri listesini TYMM'den alır. Kodlar kılavuzdaki sıraya göredir; örnek sorularda geçen kodlarla (KB2.3, KB2.8, KB2.14,
KB3.2) doğrulandı.

- **KB1 temel beceriler:** kapalı bir liste verilmez, yalnız örnekler (saymak, okumak, ölçmek…). Etiket olarak alınmadı.
- **KB2 bütünleşik beceriler (20):** KB2.1 çelişki giderme, KB2.2 gözlemleme, KB2.3 özetleme, KB2.4 çözümleme, KB2.5 sınıflandırma,
  KB2.6 bilgi toplama, KB2.7 karşılaştırma, KB2.8 sorgulama, KB2.9 genelleme, KB2.10 çıkarım yapma, KB2.11 gözleme dayalı tahmin
  etme, KB2.12 mevcut bilgiye/veriye dayalı tahmin etme, KB2.13 yapılandırma, KB2.14 yorumlama, KB2.15 yansıtma, KB2.16 muhakeme
  (akıl yürütme), KB2.17 değerlendirme, KB2.18 tartışma, KB2.19 mantıksal denetleme, KB2.20 sentezleme.
- **KB3 üst düzey düşünme becerileri (3):** KB3.1 karar verme, KB3.2 problem çözme, KB3.3 eleştirel düşünme. Her biri en az iki
  bütünleşik beceriyi içerir.
- **Alan becerileri:** derse özgüdür (ör. Türkçe "okuma"), öğretim programlarında tanımlıdır, kılavuzda listelenmez. İlk sürümde yok;
  kazanım içe aktarımı (#692) gibi programlardan yüklenebilir.
- **Süreç bileşenleri** (ör. KB3.2.SB1 problemi yapılandırmak): beceri başına ayrıdır ve kılavuz yalnız örneklenen becerilerinkini
  verir. İlk sürümde etiket değil.

## Üründe

- Soru `IsContextBased` bayrağı ve `QuestionSkills` ile KB2 + KB3 becerilerini taşır. Etiket öğretmenin beyanıdır; ürün bir sorunun
  kılavuza uygun olduğunu doğrulamaz.
- **Ürün dili:** "Maarif Modeli'ne uygun" gibi bir uygunluk iddiası yazılmaz (MEB bu tür iddiaların bir kısmının doğru olmadığını
  duyurdu). Ekranda "bağlam temelli" ve beceri adları kullanılır.
- Uçlar ve kurallar `docs/feature-notes.md` #703.

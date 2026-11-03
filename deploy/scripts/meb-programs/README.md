# MEB öğretim programlarından katalog

Türkiye Yüzyılı Maarif Modeli programlarının kazanım, konu ve alt konularını Soru Aktarımı ekranından yüklenen katalog paketine çevirir.

Hazır paket `deploy/data/meb/maarif-catalog.json`, dışarıda kalanların listesi `deploy/data/meb/maarif-catalog-report.json`.

## Yeniden üretme

PDF'ler repoda yoktur (yaklaşık 540 MB): `mufredat.meb.gov.tr/Programlar.aspx` adresinden indirilir, `<PID>.pdf` adıyla kaydedilir. `programs.json` program listesidir. `pdftotext` ve `pip install pymupdf` gerekir.

```bash
python prepare_text.py <pdf-dizini> <text-dizini>
python extract_outcomes.py <text-dizini> programs.json programs-out.json
python build_catalog.py programs-out.json ../../data/meb/maarif-catalog.json ../../data/meb/maarif-catalog-report.json
```

## Kurallar

- **Kazanım:** "Öğrenme Çıktıları" bölümünün içinde okunur, kod biçimi programa göre değişir. Beceri çerçevesi kodları (`E1.1`, `SDB3.1`) kazanım değildir.
- **Sınıf:** koddan, program başlığından ya da metindeki sınıf başlığından okunur. Hiçbiri yoksa tahmin yazılmaz, kayıt raporun `noGrade` bölümüne gider.
- **Aynı kod birden çok programda:** yılı yeni olan, eşitse genel program (çoklu yabancı dil modeli değil), eşitse çok kazanımlı olan kazanır (`replaced`, `replacedAcrossSubjects`).
- **Temada tekrar eden kod:** Türk Dili ve Edebiyatı aynı kodu her temada ayrı metinle yazar. Ayrı kayıt olsun diye kod sınıf ve temayla nitelenir: `TDE2.1` → `TDE.9.1.2.1`. Bu biçim resmî değildir.
- **Şüpheli satır:** Türkçe açıklaması "-me/-ma" ile bitmeyen ya da çok uzun satır kesilmiş metin veya beceri tanımıdır, pakete girmez (`suspect`).
- **Özel eğitim kademeleri:** sınıf olarak eklenmedi, ürün kararı bekliyor (`specialEducation`).
- **Alt konu:** "İçerik Çerçevesi" listesinden gelir, kazanıma bağlanmaz. Dil programlarında liste sözcük ve kalıptan oluştuğu için alt konu üretilmez.

## Bilinen sınırlar

- PDF'te büyük "İ" bazen "I" olarak gelir. İnce ünlüden sonrası onarılır, ilk hecedeki kayıp adda kalabilir.
- Paket yalnız ekler ve günceller, silmez. Eski müfredat konuları yeni temaların yanında kalır.

## Elenen programlar

Aynı ders ve sınıflar için eski program bütünüyle elenir. Kodlarının en az beşte biri başka programda başka metinle geçen program da bütünüyle elenir: kod müfredat başına tek kayıt tutar, program yarım kalmasın. Genel program okul türüne özel programa (İmam Hatip, çoklu yabancı dil) üstün gelir. Liste raporun `droppedProgrammes` bölümündedir.

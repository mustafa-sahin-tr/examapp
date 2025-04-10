

DO $$
DECLARE
	v_test_name TEXT := 'Türkçe';
    v_book_name TEXT := 'Türkçe Yolculuğu 3.Sınıf';
    v_grade_id INT := 3;
    v_duration INT := 1200;

    -- Test isim ve açıklamaları bir array olarak
    v_test_names TEXT[] := ARRAY[
        'Harf ve Hece Bilgisi Test 1',
		'Harf ve Hece Bilgisi Test 2',
		'Harf ve Hece Bilgisi Test 3',
		'Harf ve Hece Bilgisi Yeni Nesi Sorular Test 1',
		'Sözcük Bilgisi Test 4',
		'Sözcük Bilgisi Test 5',
		'Sözcük Bilgisi Yeni Nesil Sorular Test 2',
		'Cümle Bilgisi Test 6',
		'Cümle Bilgisi Test 7',
		'Cümle Bilgisi Test 8',
		'Cümle Bilgisi Test 9',
		'Cümle Bilgisi Test 10',
		'Cümle Bilgisi Test 11',
		'Cümle Bilgisi Test 12',
		'Cümle Bilgisi Test 13',
		'Cümle Bilgisi Test 14',
		'Cümle Bilgisi Yeni Nesil Sorular Test 3',
		'Söz Varlığı Test 15',
		'Söz Varlığı Test 16',
		'Söz Varlığı Test 17',
		'Söz Varlığı Test 18',
		'Söz Varlığı Yeni Nesil Sorular Test 4',
		'Okuma Anlama Test 19',
		'Okuma Anlama Test 20',
		'Okuma Anlama Test 21',
		'Okuma Anlama Test 22',
		'Okuma Anlama Test 23',
		'Okuma Anlama Test 24',
		'Okuma Anlama Yeni Nesil Sotular Test 5',
		'Sözcük Türleri Test 25',
		'Sözcük Türleri Test 26',
		'Sözcük Türleri Test 27',
		'Sözcük Türleri Test 28',
		'Sözcük Türleri Test 29',
		'Sözcük Türleri Yeni Nesil Sorular Test 6',
		'Noktalama İşaretleri Test 30',
		'Noktalama İşaretleri Test 31',
		'Noktalama İşaretleri Yeni Nesil Sorular Test 7',
		'Yazım Kuralları Test 32',
		'Yazım Kuralları Test 33',
		'Yazım Kuralları Yeni Nesil Sorular Test 8'
    ];

    v_descriptions TEXT[] := ARRAY[
        'Harf Bilgisi - Alfabetik Sıralama - Sözcükte Eksik Bırakılan Harfi Bulma - Sesli(Ünlü) ve Sessiz(Ünsüz) Harfler',
		'Hece Bilgisi - Hece Sayısı - Satır Sonuna Sığmayan Sözcükler - Birleşik Sözcükleri Hecelerine Ayırmak - Harf Bilgisi - Alfabetik Sıralama - Sözcükte Eksik Bırakılan Harfi Bulma - Sesli(Ünlü) ve Sessiz(Ünsüz) Harfler',
		'Hece Bilgisi - Hece Sayısı - Satır Sonuna Sığmayan Sözcükler - Birleşik Sözcükleri Hecelerine Ayırmak - Harf Bilgisi - Alfabetik Sıralama - Sözcükte Eksik Bırakılan Harfi Bulma - Sesli(Ünlü) ve Sessiz(Ünsüz) Harfler',
		'Harf ve Hece Bilgisi Yeni Nesi Sorular',
		'Sözcük Bilgisi - Sözcükte Anlam - Tek Başına Anlamı Olmayan Sözcükler - Hecelerle Sözcük Oluşturma - Sözcük Dizilerini Uygun Sözcüklerle Tamamlama - Resimli Sözlük - Kavram Haritası - Yazım Yanlışı Yapılan Sözcükler - Türkçesini Kullanalım - Sözlükten Sözcük Bulma - Sözcüklerin Anlamını Değiştiren Ekler',
		'Sözcük Bilgisi - Sözcükte Anlam - Tek Başına Anlamı Olmayan Sözcükler - Hecelerle Sözcük Oluşturma - Sözcük Dizilerini Uygun Sözcüklerle Tamamlama - Resimli Sözlük - Kavram Haritası - Yazım Yanlışı Yapılan Sözcükler - Türkçesini Kullanalım - Sözlükten Sözcük Bulma - Sözcüklerin Anlamını Değiştiren Ekler',
		'Sözcük Bilgisi Yeni Nesil Sorular',
		'Cümle Bilgisi - Cümlenin Yazılışı - Anlamlı ve Kurallı Cümleler - Olumlu ve Olumsuz Cümle - Soru Cümleleri - Cümlede Anlam - Cümle Tamamlama - Cümlelerin Anlam Özellikleri - Cümlede Anlatım Bozuklukları',
		'Cümlede 5N 1K',
		'Sebep Sonuç Cümleleri',
		'Karşılaştırma Bildiren Cümleler',
		'Betimleme',
		'Gerçek ve Hayal Ürünü Cümleler',
		'Duygusal ve Abartılı Cümleler',
		'Cümle Bilgisi',
		'Cümle Bilgisi',
		'Cümle Bilgisi Yeni Nesil Sorular',
		'Eş Anlamlı (Anlamdaş) Sözcükler',
		'Zıt (Karşıt) Anlamlı Sözcükler',
		'Eş Sesli (Sesteş) Sözcükler',
		'Eş Anlamlı (Anlamdaş) Sözcükler - Zıt (Karşıt) Anlamlı Sözcükler - Eş Sesli (Sesteş) Sözcükler - Deyimler Sözlüğü - Atasözleri Sözlüğü',
		'Söz Varlığı Yeni Nesil Sorular',
		'Resim ve Fotoğraf Yorumlama - Beden Dili - Sosyal Olaylar - Karikatürde Verilen Mesajlar - Görsel Yorumlama - Görsel,Başlık İlişkisi',
		'Metinle İlgili Soruları Yanıtlama - Şiirle İlgili Soru Oluşturma - 5N 1K',
		'Öykü Unsurları - Öykü Haritası',
		'Olayların Oluş Sırası',
		'Metnin Konusu - Konu,Başlık İlişkisi - Metin ve Görsel İlişkisi - Metnin Ana Düşüncesi - Şiirde Ana Duygu - Paragraf Yazma - Öykü Tamamlama - Metin Türleri - Metinde Başlık - Şiirde Dize ve Kıta - İstiklal Marşı - Yönerg İzleme - Kişisel bilgiler Formu - Dijital Metinler',
		'Şekil, Sembol ve İşaretler - Trafik İşaretleri - Grafik Yorumlama - Tablo Yorumlama',
		'Okuma Anlama Yeni Nesil Sotular',
		'Adlar - Özel Adlar - Tür Adları - Soyut ve Somut Adlar - Tekil, Çoğul ve Topluluk Adları',
		'Varlıkların Özelliklerini Belirten Szöcükler - Varlıkların Özelliklerini Belirten Szöcükleri Bulma',
		'Adın Yerine Kullanılan Sözcükler',
		'Hareket Bildiren Sözcükler - Kim Yapmış? - Ne Zaman Yapmış?',
		'Sözcük Türleri',
		'Sözcük Türleri Yeni Nesil Sorular',
		'Nokta - Soru İşareti - Virgül - Ünlem İşareti - Kısa Çizgi - Uzun Çizgi - Kesme İşareti - İki Nokta - Eğik Çizgi',
		'Nokta - Soru İşareti - Virgül - Ünlem İşareti - Kısa Çizgi - Uzun Çizgi - Kesme İşareti - İki Nokta - Eğik Çizgi',
		'Noktalama İşaretleri Yeni Nesil Sorular',
		'Yazımı Karıştırılan Sözcükler - Cümle Büyük Harfle Başlar - Özel Adların Yazışılış - Ay, Gün ve Tarihlerin Yazılışı - Kısaltma ve Başlıkların Yazılışı - "-de" Eki ve "de" Sözcüğünün Yazılışı - "-ki" Eki ve "ki" Sözcüğünün Yazılışı - Soru Eki "mi" nin Yazılışı - Ek mi, Sözcük mü?',
		'Yazımı Karıştırılan Sözcükler - Cümle Büyük Harfle Başlar - Özel Adların Yazışılış - Ay, Gün ve Tarihlerin Yazılışı - Kısaltma ve Başlıkların Yazılışı - "-de" Eki ve "de" Sözcüğünün Yazılışı - "-ki" Eki ve "ki" Sözcüğünün Yazılışı - Soru Eki "mi" nin Yazılışı - Ek mi, Sözcük mü?',
		'Yazım Kuralları Yeni Nesil Sorular'
    ];

    i INT := 1;
    book_id INT;
    test_id INT;
BEGIN
    -- Book yoksa ekle
    INSERT INTO public."Books" ("Name")
    SELECT v_book_name
    WHERE NOT EXISTS (
        SELECT 1 FROM public."Books" WHERE "Name" = v_book_name
    );

    -- Kitap ID’sini al
    SELECT "Id" INTO book_id FROM public."Books" WHERE "Name" = v_book_name;

    -- Array içinde döngü ile test ve worksheet ekle
    FOR i IN 1..array_length(v_test_names, 1) LOOP

        -- Önce BookTest ekle (yoksa)
        INSERT INTO public."BookTests" ("Name", "BookId")
        SELECT v_test_names[i], book_id
        WHERE NOT EXISTS (
            SELECT 1 FROM public."BookTests" 
            WHERE "Name" = v_test_names[i] AND "BookId" = book_id
        );

        -- BookTest ID’sini al
        SELECT "Id" INTO test_id FROM public."BookTests"
        WHERE "Name" = v_test_names[i] AND "BookId" = book_id;

        -- Worksheet ekle (yoksa)
        INSERT INTO public."Worksheets" (
            "Name", 
            "BookTestId", 
            "Description", 
            "GradeId", 
            "MaxDurationSeconds", 
            "Subtitle",
            "IsPracticeTest"
        )
        SELECT 
            v_test_name,
            test_id,
            v_descriptions[i],
            v_grade_id,
            v_duration,
            v_test_names[i], -- Subtitle test adıyla aynı
            FALSE
        WHERE NOT EXISTS (
            SELECT 1 FROM public."Worksheets"
            WHERE "BookTestId" = test_id AND "Subtitle" = v_test_names[i]
        );

    END LOOP;
END $$;


	 
DO $$
DECLARE
	v_test_name TEXT := 'Fen Bilimleri';
    v_book_name TEXT := 'Fen Bilimleri Yolculuğu 3.Sınıf';
    v_grade_id INT := 3;
    v_duration INT := 1200;

    -- Test isim ve açıklamaları bir array olarak
    v_test_names TEXT[] := ARRAY[
        'Gezegenimizi Tanıyalım Test 1',
		'Gezegenimizi Tanıyalım Test 2',
		'Gezegenimizi Tanıyalım Yeni Nesil Sorular Test 1',
		'Beş Duyu Organımız Test 3',
		'Beş Duyu Organımız Test 4',
		'Beş Duyu Organımız Yeni Nesil Sorular Test 2',
		'Kuvveti Tanıyalım Test 5',
		'Kuvveti Tanıyalım Test 6',
		'Kuvveti Tanıyalım Test 7',
		'Kuvveti Tanıyalım Yeni Nesil Sorular Test 3',
		'Maddemizi Tanıyalım Test 8',
		'Maddemizi Tanıyalım Test 9',
		'Maddemizi Tanıyalım Test 10',
		'Maddemizi Tanıyalım Test 11',
		'Maddemizi Tanıyalım Yeni Nesil Sorular Test 4',
		'Çevremizdeki Işık ve Sesler Test 12',
		'Çevremizdeki Işık ve Sesler Test 13',
		'Çevremizdeki Işık ve Sesler Test 14',
		'Çevremizdeki Işık ve Sesler Yeni Nesil Sorular Test 5',
		'Canlılar Dünyasına Yolculuk Test 15',
		  'Canlılar Dünyasına Yolculuk Test 16',
		  'Canlılar Dünyasına Yolculuk Test 17',
		  'Canlılar Dünyasına Yolculuk Test 18',
		  'Canlılar Dünyasına Yolculuk Test 19',
		  'Canlılar Dünyasına Yolculuk Yeni Nesil Sorular Test 6',
		  'Elektrikli Araçlar Test 20',
  'Elektrikli Araçlar Test 21',
  'Elektrikli Araçlar Test 22',
  'Elektrikli Araçlar Yeni Nesil Sorular Test 7'
    ];

    v_descriptions TEXT[] := ARRAY[
        'Dünyanın şekli - Dünya Modeli - Dünyamızın Katmanları - Dünyamızı Saran Hava Katmanı - Canlıların Yaşadığı Katmanlar - Dünya Yüzeyindeki Kara ve Suların Kapladığı Alan',
		'Dünyanın şekli - Dünya Modeli - Dünyamızın Katmanları - Dünyamızı Saran Hava Katmanı - Canlıların Yaşadığı Katmanlar - Dünya Yüzeyindeki Kara ve Suların Kapladığı Alan',
		'Gezegenimizi Tanıyalım Yeni Nesil Sorular',
		'Duyu Organlarımızın Önemi - Duyu Organlarımız - Duyu Oranlarının Sağlığı',
		'Duyu Organlarımızın Önemi - Duyu Organlarımız - Duyu Oranlarının Sağlığı',
		'Beş Duyu Organımız Yeni Nesil Sorular Test 2',
		'Hareket Eden Varlıklar ve Özellikleri - Varlıkların Hareket Özellikleri - Kuvvetin Etkisi',
		'İtme ve Çekme Kuvveti - İtme,Çekme Kuvvetinin Etkisi - Hareketli Cisimlerin Sebep Olabileceği Tehlikeler- Hareketi Durdurma - Hareketin Neden Olduğu Tehlikeler',
		'Hareket Eden Varlıklar ve Özellikleri - Varlıkların Hareket Özellikleri - Kuvvetin Etkisi - İtme ve Çekme Kuvveti - İtme,Çekme Kuvvetinin Etkisi - Hareketli Cisimlerin Sebep Olabileceği Tehlikeler- Hareketi Durdurma - Hareketin Neden Olduğu Tehlikeler',
		'Kuvveti Tanıyalım Yeni Nesil Sorular',
		'Maddeyi Niteleyen Temel Özellikler - İnsan Sağlığı İçin Tehlikeli Olan Maddeler - Maddelerle Çalışırken Güvenlik Tedbirlerini Alma',
		'Maddenin Halleri',
		'Maddeyi Niteleyen Temel Özellikler - İnsan Sağlığı İçin Tehlikeli Olan Maddeler - Maddelerle Çalışırken Güvenlik Tedbirlerini Alma - Maddenin Halleri',
		'Maddeyi Niteleyen Temel Özellikler - İnsan Sağlığı İçin Tehlikeli Olan Maddeler - Maddelerle Çalışırken Güvenlik Tedbirlerini Alma - Maddenin Halleri',
		'Maddemizi Tanıyalım Yeni Nesil Sorular',
		'Işığın Görmedeki Rolü - Doğal ve Yapay Işık Kaynakları',
		'Her Sesin Bir Kaynağı Vardır - Ses Her Yöne Dağılır - Ses Kaynaklarının Yeri - Doğal ve Yapay Ses Kaynakları - Sesin İşitmedeki Rolü - Ses Şiddeti ve Uzaklık Arasındaki İlişki - Şiddetli Sesler ve Zararları',
		'Işığın Görmedeki Rolü - Doğal ve Yapay Işık Kaynakları - Her Sesin Bir Kaynağı Vardır - Ses Her Yöne Dağılır - Ses Kaynaklarının Yeri - Doğal ve Yapay Ses Kaynakları - Sesin İşitmedeki Rolü - Ses Şiddeti ve Uzaklık Arasındaki İlişki - Şiddetli Sesler ve Zararları',
		'Çevremizdeki Işık ve Sesler Yeni Nesil Sorular',
		'Canlı ve Cansız Varlıklar - Bitkiler ve Hayvanlar - Bitkilerin Gelişimi',
	  'Ben ve Çevrem - Çevremizin Temizliği',
	  'Doğal ve Yapay Çevre - Yapay Çevre Tasarlayalım - Doğal Çevrenin Önemi - Doğal Çevrenin Korunması',
	  'Canlı ve Cansız Varlıklar - Bitkiler ve Hayvanlar - Bitkilerin Gelişimi - Ben ve Çevrem - Çevremizin Temizliği - Doğal ve Yapay Çevre - Yapay Çevre Tasarlayalım - Doğal Çevrenin Önemi - Doğal Çevrenin Korunması',
	  'Canlılar Dünyasına Yolculuk Konuları',
	  'Canlılar Dünyasına Yolculuk Yeni Nesil Sorular',
	  'Elektrikli Araç-Gereçler - Elektrikli Araç-Gereçler ve Elektrik Kaynakları - Pil Atıkları ve Çevre Kirliliği - Elektriğin Güvenli Kullanımı',
  'Elektrikli Araç-Gereçler - Elektrikli Araç-Gereçler ve Elektrik Kaynakları - Pil Atıkları ve Çevre Kirliliği - Elektriğin Güvenli Kullanımı',
  'Elektrikli Araç-Gereçler - Elektrikli Araç-Gereçler ve Elektrik Kaynakları - Pil Atıkları ve Çevre Kirliliği - Elektriğin Güvenli Kullanımı',
  'Elektrikli Araçlar Yeni Nesil Sorular'
    ];

    i INT := 1;
    book_id INT;
    test_id INT;
BEGIN
    -- Book yoksa ekle
    INSERT INTO public."Books" ("Name")
    SELECT v_book_name
    WHERE NOT EXISTS (
        SELECT 1 FROM public."Books" WHERE "Name" = v_book_name
    );

    -- Kitap ID’sini al
    SELECT "Id" INTO book_id FROM public."Books" WHERE "Name" = v_book_name;

    -- Array içinde döngü ile test ve worksheet ekle
    FOR i IN 1..array_length(v_test_names, 1) LOOP

        -- Önce BookTest ekle (yoksa)
        INSERT INTO public."BookTests" ("Name", "BookId")
        SELECT v_test_names[i], book_id
        WHERE NOT EXISTS (
            SELECT 1 FROM public."BookTests" 
            WHERE "Name" = v_test_names[i] AND "BookId" = book_id
        );

        -- BookTest ID’sini al
        SELECT "Id" INTO test_id FROM public."BookTests"
        WHERE "Name" = v_test_names[i] AND "BookId" = book_id;

        -- Worksheet ekle (yoksa)
        INSERT INTO public."Worksheets" (
            "Name", 
            "BookTestId", 
            "Description", 
            "GradeId", 
            "MaxDurationSeconds", 
            "Subtitle",
            "IsPracticeTest"
        )
        SELECT 
            v_test_name,
            test_id,
            v_descriptions[i],
            v_grade_id,
            v_duration,
            v_test_names[i], -- Subtitle test adıyla aynı
            FALSE
        WHERE NOT EXISTS (
            SELECT 1 FROM public."Worksheets"
            WHERE "BookTestId" = test_id AND "Subtitle" = v_test_names[i]
        );

    END LOOP;
END $$;


	 
DO $$
DECLARE
	v_test_name TEXT := 'Türkçe';
    v_book_name TEXT := 'F.Ü.Z.E Türkçe 3.Sınıf';
    v_grade_id INT := 3;
    v_duration INT := 1200;

    -- Test isim ve açıklamaları bir array olarak
    v_test_names TEXT[] := ARRAY[
   'Harf ve Hece Bilgisi Test 1',
  'Harf ve Hece Bilgisi Test 2',
  'Harf ve Hece Bilgisi Test 3',
  'Harf ve Hece Bilgisi Yeni Nesil Sorular Test 1',
  'Söz Varlığı Test 4',
  'Söz Varlığı Test 5',
  'Söz Varlığı Test 6',
  'Söz Varlığı Test 7',
  'Söz Varlığı Yeni Nesil Sorular Test 2',
  'Cümle Bilgisi Test 8',
  'Cümle Bilgisi Yeni Nesil Sorular Test 3',
  'Yazım Kuralları Test 9',
  'Yazım Kuralları Test 10',
  'Yazım Kuralları Test 11',
  'Noktalama İşaretleri Test 12',
  'Noktalama İşaretleri Test 13',
  'Sözcük Türleri Test 14',
  'Sözcük Türleri Yeni Nesil Sorular Test 4',
  'Sözcük Türleri Test 15',
  'Sözcük Türleri Test 16',
  'Okuma Anlama Test 17',
  'Okuma Anlama Yeni Nesil Sorular Test 5',
  'Okuma Anlama Test 18',
  'Okuma Anlama Test 19',
  'Okuma Anlama Test 20',
  'Okuma Anlama Test 21',
  'Okuma Anlama Yeni Nesil Sorular Test 6',
  'Söz Varlığı Test 22',
  'Söz Varlığı Test 23',
  'Söz Varlığı Test 24',
  'Söz Varlığı Test 25',
  'Söz Varlığı Test 26',
  'Söz Varlığı Test 27',
  'Söz Varlığı Yeni Nesil Sorular Test 7',
  'Metin Türleri Test 28',
  'Metin Türleri Test 29',
  'Metin Türleri Test 30',
  'Metin Türleri Test 31',
  'Metin Türleri Test 32',
  'Metin Türleri Yeni Nesil Sorular Test 8'
    ];

    v_descriptions TEXT[] := ARRAY[
  'Ses (Harf) Bilgisi - Ünlü ve Ünsüz Harfler',
  'Sözlük Sırası',
  'Hece Bilgisi - Satır Sonuna Sığmayan Sözcükler',
  'Harf ve Hece Bilgisi Yeni Nesil Sorular',
  'Okuduğunu Anlama - Sözcük (Kelime) Bilgisi',
  'Zıt (Karşıt) Anlamlı Sözcükler',
  'Eş Anlamlı Sözcükler',
  'Eş Sesli (Sesteş) Sözcükler',
  'Söz Varlığı Yeni Nesil Sorular',
  'Olumlu Olumsuz Cümle',
  'Cümle Bilgisi Yeni Nesil Sorular',
  'Yazım (İmlâ) Kuralları',
  'De ve ki Ekinin Yazımı',
  'Soru Ekinin Yazımı',
  'Noktalama İşaretleri 1. Bölüm',
  'Noktalama İşaretleri 2. Bölüm',
  'Özel Adlar - Tür Adı - Özel Ad - Tür Adı',
  'Sözcük Türleri Yeni Nesil Sorular',
  'Tekil ve Çoğul Adlar',
  'Topluluk Adı',
  '5N 1K',
  'Okuma Anlama Yeni Nesil Sorular',
  'Sebep - Sonuç İlişkisi',
  'Gerçek ve Hayal Ürünü İfadeler - Okuduğunu Anlama',
  'Duygusal ve Abartılı İfadeler - Karşılaştırmalar',
  'Betimleme - Okuduğunu Anlama',
  'Okuma Anlama Yeni Nesil Sorular',
  'Varlıkların Özelliklerini Belirten Sözcükler',
  'İş, Oluş, Hareket Bildiren Sözcükler',
  'Hikâye Unsurları',
  'Konu - Ana Fikir',
  'Şiirde Ana Duygu',
  'Okuduğunu Anlama - Atasözleri - Deyimler',
  'Söz Varlığı Yeni Nesil Sorular',
  'Mektup - Anı (Hatıra) - Günlük (Günce)',
  'Tablo Okuma - Grafik Okuma',
  'İşaret ve Levhalar',
  'Beden Dili',
  'Metin Türleri Genel Tekrar',
  'Metin Türleri Yeni Nesil Sorular'
    ];

    i INT := 1;
    book_id INT;
    test_id INT;
BEGIN
    -- Book yoksa ekle
    INSERT INTO public."Books" ("Name")
    SELECT v_book_name
    WHERE NOT EXISTS (
        SELECT 1 FROM public."Books" WHERE "Name" = v_book_name and "IsDeleted" = FALSE
    );

    -- Kitap ID’sini al
    SELECT "Id" INTO book_id FROM public."Books" WHERE "Name" = v_book_name;

    -- Array içinde döngü ile test ve worksheet ekle
    FOR i IN 1..array_length(v_test_names, 1) LOOP

        -- Önce BookTest ekle (yoksa)
        INSERT INTO public."BookTests" ("Name", "BookId")
        SELECT v_test_names[i], book_id
        WHERE NOT EXISTS (
            SELECT 1 FROM public."BookTests" 
            WHERE "Name" = v_test_names[i] AND "BookId" = book_id
			  and "IsDeleted" = FALSE
        );

        -- BookTest ID’sini al
        SELECT "Id" INTO test_id FROM public."BookTests"
        WHERE "Name" = v_test_names[i] AND "BookId" = book_id;

        -- Worksheet ekle (yoksa)
        INSERT INTO public."Worksheets" (
            "Name", 
            "BookTestId", 
            "Description", 
            "GradeId", 
            "MaxDurationSeconds", 
            "Subtitle",
            "IsPracticeTest"
        )
        SELECT 
            v_test_name,
            test_id,
            v_descriptions[i],
            v_grade_id,
            v_duration,
            v_test_names[i], -- Subtitle test adıyla aynı
            FALSE
        WHERE NOT EXISTS (
            SELECT 1 FROM public."Worksheets"
            WHERE "BookTestId" = test_id AND "Subtitle" = v_test_names[i] and "IsDeleted" = FALSE
        );

    END LOOP;
END $$;


	 














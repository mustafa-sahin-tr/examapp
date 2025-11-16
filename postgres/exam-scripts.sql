select tq."Order", q."Id", q."CorrectAnswerId", CASE WHEN q."CorrectAnswerId" = a."Id" THEN 'Correct' ELSE NULL END,
	 a.* from "TestQuestions" tq 
   join "Questions" q on tq."QuestionId" = q."Id"
   join "Answers" a on a."QuestionId" = q."Id"
	where tq."TestId" = 27
	-- and q."CorrectAnswerId" = a."Id"
	order by tq."Order", q."Id", a."Id"

/*doğru cevapları listeleme*/
select tq."Order", q."Id", q."CorrectAnswerId", a."Id", a.* from "Worksheets" w
 join "TestQuestions" tq on tq."TestId" = w."Id"
 join "Questions" q on q."Id" = tq."QuestionId" 
 join "Answers" a on a."QuestionId" = q."Id" and q."CorrectAnswerId" = a."Id"
 where w."Subtitle" LIKE '%9%'  
 order by tq."Order", q."Id", a."Id"



DO $$
DECLARE
	v_test_name TEXT := 'Matematik';
    v_book_name TEXT := 'Matematik Yolculuğu 3.Sınıf';
    v_grade_id INT := 3;
    v_duration INT := 1200;

    -- Test isim ve açıklamaları bir array olarak
    v_test_names TEXT[] := ARRAY[
        'Doğal Sayılar Test 1',
        'Doğal Sayılar Test 2',
        'Doğal Sayılar Test 3',
        'Doğal Sayılar Test 4',
        'Doğal Sayılar Test 5',
		'Doğal Sayılarla Toplama İşlemi Test 6',
		'Doğal Sayılarla Çıkarma İşlemi Test 7',		
		'Doğal Sayılar Yeni Nesil Sorular Test 1',
		'Doğal Sayılarla Toplama İşlemi Test 8',
		'Doğal Sayılarla Çıkarma İşlemi Test 9',
		'Doğal Sayılarla Çıkarma İşlemi Test 10',
		'Veri Toplama ve Değerlendirme Test 11',
		'Veri Toplama ve Değerlendirme Test 12',
		'Doğal Sayılarla Toplmama/Çıkarma İşlemi Yeni Nesil Sorular Test 2',
		'Doğal Sayılarla Çarpma İşlemi Test 13',
		'Doğal Sayılarla Bölme İşlemi Test 14',
		'Doğal Sayılarla Bölme İşlemi Test 15',
		'Doğal Sayılarla Çarpma ve Bölme İşlemi Test 16',
		'Doğal Sayılarla Çarpma ve Bölme İşlemi Yeni Nesi Sorular Test 3',
		'Kesiler Test 17',
		'Kesiler Test 18',
		'Zaman Ölçme Test 19',
		'Paralarımız Test 20',
		'Tartma Test 21',
		'Kesirler - Zaman Ölçme - Paralarımız - Tartma Test 22',
		'Kesirler - Zaman Ölçme - Paralarımız - Tartma Yeni Nesil Sorular Test 4',
		'Geometrik Şekiller ve Cisimler Test 23',
		'Geometrik Şekiller ve Cisimler Test 24',
		'Geometrik Örüntüler Test 25',
		'Uzamsal İlişkiler Test 26',
		'Geometrik Şekiller ve Cisimler - Geometrik Örüntüler - Geometride Temel Kavramlar - Uzamsal İlişkiler Test 27',
		'Geometrik Şekiller ve Cisimler - Geometrik Örüntüler - Geometride Temel Kavramlar - Uzamsal İlişkiler Yeni Nesil Sorular Test 5',
		'Uzunluk Ölçme Test 28',
		'Çevre Ölçümü Test 29',
		'Alan Ölçme Test 30',
		'Sıvı Öçlme Test 31',
		'Uzunluk Ölçme - Çevre  Ölçme - Alan  Ölçme - Sıvı  Ölçme Test 32',
		'Uzunluk Ölçme - Çevre  Ölçme - Alan  Ölçme - Sıvı  Ölçme Yeni Nesil Sorular Test 6'
    ];

    v_descriptions TEXT[] := ARRAY[
        'Üç Basamaklı Doğal Sayılar - Birer Ritmik Sayma - Onar Ritmik Sayma - Yüzer Ritmik Sayma - Basamak Adları - Basamak Değerleri - Yüzlük, Onluk ve Birlik',
        'Doğal Sayıları En Yakın Onluğa Yuvarlama - Doğal Sayıları En Yakın Yüzlüğe Yuvarlama - Doğal Sayıları Karşılaştırma - Doğal Sayıları Sıralama',
        'Altışar, Yedişer, Sekizer, Dokuzar Ritmik Sayma',
        'Sayı Örüntüleri',
        'Tek ve Çİft Doğal Sayılar - Toplamı Tek mi Çift mi? - Romen Rakamları',
		'Eldesiz Toplama İşlemleri - Eldeli Toplama İşlemleri - Üç Toplananlı İşlemler - Toplananaların Yerini Değiştirme',
		'Onluk ve Yüzlük Bozmadan Çıkarma İşlemi - Onluk Bozarak Çıkarma İşlemi - Onluk ve Yüzlük Bozarak Çıkarma İşlemi - İki Basamaklı Sayılarla Zihinden Çıkarma İşmlemi - 10 un Katı Olan Sayılarala Zihinden Çıkarma İşlemi',
		'Doğal Sayılar Yeni Nesil Sorular',
		'İki Sayıın Toplamını Tahmin Etme - Toplamı Bulma Yöntemleri - Zihinden toplama İşlemi - Verilmeyen Toplananı Bulma - Problem Çözme - Problem Kurma',
		'Çıkarma İşleminin Sonucunu Tahmin Etme - Çıkarma İşlemninde Verilmeyen Çıkanı Bulma - Çıkarma İşlemninde Verilmeyen Eksileni Bulma - Çıkarma İşleminin Basamaklarında Verilmeyen Rakamı Bulma',
		'Problem Çözme - Problem Kurma',
		'Şekil Grafiği - Şekil Grafiği Oluşturma - Şekil Grafiği Yorumlama - Çetele Tablosu Oluşturma - Sıklık Tablosu Oluşturma',
		'Şekil Grafiği - Şekil Grafiği Oluşturma - Şekil Grafiği Yorumlama - Çetele Tablosu Oluşturma - Sıklık Tablosu Oluşturma',
		'Doğal Sayılarla Toplmama/Çıkarma İşlemi Yeni Nesil Sorular',
		'Aynı Sayıların Toplamı ve Katları - Çarpım Tablosu - 6 ile, 7 ile, 8 İle, 9 ile Çarpma - İki Basamaklı Sayıları Bir Basamaklı Sayılarla Çarpma İşlemi - İki Basamaklı Sayıları İki Basamaklı Sayılarla Çarpma İşlemi - Üç Basamaklı Sayıları Bir Basamaklı Sayılarla Çarpma İşlemi - Eldeli ve Eldesiz Çarpma - 10 ve 10''un katları ile Çarpma İşlemi - 100 ile Kısa Yoldan Çarpma İşlemi - Çarpanları Artıralım, Azaltalım - Problem Kurma - Problem Çözme',
		'Nesneleri Paylaştıralım - Bölme İşlemini Sayı Doğrusu Üzerinde Gösterelim - Kalan Bölenden Küçüktür',
		'Kısa Yoldan Bölme İşlemi - Nesneleri Paylaştıralım - Kalanlı Bölme İşlemi - Bölme İşleminin Doğruluğunu Kontrol Etme - Verilmeyen Bölüneni Bulma - Verilmeyen Böleni Bulma - Kalanlı Bölme İşleminde Verilmeyen Böleni Bulma - Problem Çözme - Problem Kurma',
		'Doğal Sayılarla Çarpma ve Bölme İşlemi',
		'Doğal Sayılarla Çarpma ve Bölme İşlemi Yeni Nesi Sorular',
		'Bütün, Yarım, Çeyrek - Yarımm ve Çeyreği Kesirle Gösterme - Pay, Payda ve Kesir Çizgisi - Yarım, Çeyrek - Kesrin Birimi - Pay ve Payda Arasındaki İlişki - Paydası 10 Olan birim Kesirler - Paydası 100 Olan birim kesirler - Birim Kesir Kadarı Kaç Tane? - Bir Çokluğun Belirtilen birim Kesir Kadarını Bulma - Kesir Elde Etme',
		'Paydaları Eşit Olan Kesirleri Karşılaştırma - Payları Eşit Olan Kesirleri Karşılaştırma - Paydaları Eşit Olan Kesirleri Sıralama - Payları Eşit Olan Kesirleri Sıralama',
		'Saati Okuma - Öğleden Önce, Öğleden Sonra, Zamanı Farklı Birimlerle İfade Etme - Yıl, Hafta, Gün - Dakika, Saniye - Zaman Farkı - Bir Günüm Nasıl Geçti - Problem Çözme',
		'Kaç Lira Var? - Kaç Kuruş Var? - Lira ve Kuruş Arasındaki İlişki - Problem Kurma - Problem Çözme',
		'Kilogram (kg) ve Gram (g) - Tartılarının Bulalım - Tahmin Edelim - Problem Çözme - Problem Kurma',
		'Kesirler - Zaman Ölçme - Paralarımız - Tartma',
		'Kesirler - Zaman Ölçme - Paralarımız - Tartma Yeni Nesil Sorular',
		'Geometrik Cisimlerin Yüzleri ve Yüzeyleri - Geometrik Cisimlerin Yüzleri - Açınımları Verilen Geometrik Cisimler - Geometrik Cisimlerin Benzer ve Farklı Yönleri',
		'Kare, Dikdörtgen, Üçgen - Kare ve Dikdörtgen Çizelim - Ğçgen Çizelim - Geometrik Şekilleri Sınıflandırma',
		'Örüntü ve Süslemeler - Nokta - Doğru ve Işın - Açı Modelleri - Doğru Parçası - Doğru, Işın, Doğru Parçası, Açı, Yatay, Eğik ve Dikey Doğru Parçaları',
		'Simetrik Şekiller - Doğruya Göre Simetriği Çizme',
		'Geometrik Şekiller ve Cisimler - Geometrik Örüntüler - Geometride Temel Kavramlar - Uzamsal İlişkiler',
		'Geometrik Şekiller ve Cisimler - Geometrik Örüntüler - Geometride Temel Kavramlar - Uzamsal İlişkiler Yeni Nesil Sorular',
		'Standart Olmayan Ölçme Araçları - Metre ve Santimetre Arasındaki İlişki - Metre ve Santimetre Arasındaki Dönüşümler - Doğru Parçası Çizme ve Ölçme - Kilometre ve Metre',
		'Nesnelerin Çevrelerini Belirleme - Düzlemsel Şekilllerin Çevre Uzunluğu - Karenin Çevre Uzunluğu - Dikdörtgenin Çevre Uzunluğu - Dikdörtgenin Verilemeyen Kenar Uzunluğunu Hesaplama - Üçgenlerin Çevre Uzunluğu - Düzlemsel Şekillerin Çevre Uzunluğunu Hesaplama - Problem Çözme',
		'Nesne ve Şekillerin Alanlarını Ölçelim - Şekillerin Alanını Tahmin Etme',
		'Litre ve Yarım Litre - Prpblem Çözme',
		'Uzunluk Ölçme - Çevre  Ölçme - Alan  Ölçme - Sıvı  Ölçme',
		'Uzunluk Ölçme - Çevre  Ölçme - Alan  Ölçme - Sıvı  Ölçme Yeni Nesil Sorular'
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


	 






/*

-- override ekleme örneği
UPDATE  public."Questions" 
SET "LayoutPlan" =
  jsonb_set(
    jsonb_set(
      COALESCE(NULLIF("LayoutPlan", '')::jsonb, '{}'::jsonb),
      '{overrides}',
      jsonb_build_object(
        'initialScale', 1.1,
        'answerScale', 1.2,
        'question', jsonb_build_object('maxHeight', 780, 'maxWidth', 850),
        'answers',  jsonb_build_object('maxHeight', 40, 'maxWidth', 150)
      )
    ),
    '{answerColumns}',
    '1'::jsonb,
    true
  )::text
WHERE "Id" = 6668 -- target question_region row
*/
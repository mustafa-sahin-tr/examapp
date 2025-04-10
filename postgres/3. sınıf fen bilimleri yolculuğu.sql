
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


	 





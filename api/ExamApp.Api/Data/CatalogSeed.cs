// Data/SeedData.cs
using Microsoft.EntityFrameworkCore;
using System.Linq;

// ExamApp.Api.Data namespace'i altında olduğunuzu varsayıyorum veya using ekleyin
// using ExamApp.Api.Models; // Eğer modelleriniz ayrı bir namespace'te ise

namespace ExamApp.Api.Data // Kendi namespace'iniz
{
    public static class CatalogSeed // CatalogSeed sınıfı, veritabanını başlatmak için kullanılır
    {
        public static void Initialize(IServiceProvider serviceProvider)
        {
            using (var context = new AppDbContext( // DbContext isminiz AppDbContext olarak değişti
                serviceProvider.GetRequiredService<
                    DbContextOptions<AppDbContext>>()))
            {
                // Look for any existing data.
                // Eğer Subjects veya Grades zaten varsa, seed etme.
                // Bu kontrolü, sadece ilk çalıştırmada seed edilmesi için tutuyoruz.
                if (context.Subjects.Any() || context.Grades.Any())
                {
                    return;   // DB has been seeded
                }

                // Ders (Subject) - Matematik
                var matematikSubject = new Subject { Name = "Matematik" };
                context.Subjects.Add(matematikSubject);
                context.SaveChanges();

                // Sınıf (Grade) - 3. Sınıf
                var grade3 = new Grade { Name = "3. Sınıf" };
                context.Grades.Add(grade3);
                context.SaveChanges();

                // GradeSubject İlişkisi
                var grade3Matematik = new GradeSubject { Grade = grade3, Subject = matematikSubject };
                context.GradeSubjects.Add(grade3Matematik);
                context.SaveChanges();

                // --- 3. Sınıf Konuları (Topics) ve Alt Konuları (SubTopics) ---

                // Öğrenme Alanı: Sayılar ve İşlemler (Bu artık bir Topic)
                var topicSayilarIslemler = new Topic { Name = "Sayılar ve İşlemler", Subject = matematikSubject, Grade = grade3 };
                context.Topics.Add(topicSayilarIslemler);
                context.SaveChanges();

                // Alt Konular (SubTopics) ve Kazanımlar (Learning Outcomes)
                var subTopicDogalSayilar = new SubTopic { Name = "Doğal Sayılar", Topic = topicSayilarIslemler };
                var subTopicToplamaCikarma = new SubTopic { Name = "Toplama İşlemi ve Çıkarma İşlemi", Topic = topicSayilarIslemler };
                var subTopicCarpmaBolme = new SubTopic { Name = "Çarpma ve Bölme İşlemleri", Topic = topicSayilarIslemler };
                var subTopicKesirler = new SubTopic { Name = "Kesirler", Topic = topicSayilarIslemler };
                var subTopicParalarimiz = new SubTopic { Name = "Paralarımız", Topic = topicSayilarIslemler };
                context.SubTopics.AddRange(subTopicDogalSayilar, subTopicToplamaCikarma, subTopicCarpmaBolme, subTopicKesirler, subTopicParalarimiz);
                context.SaveChanges();

                // Kazanımlar - Doğal Sayılar (3. Sınıf)
                var k3111 = new LearningOutcome { Code = "M.3.1.1.1.", Description = "Üç basamaklı doğal sayıları okur ve yazar.", SubTopic = subTopicDogalSayilar };
                var k3112 = new LearningOutcome { Code = "M.3.1.1.2.", Description = "Üç basamaklı doğal sayıların basamaklarını ve basamak değerlerini belirtir.", SubTopic = subTopicDogalSayilar };
                var k3113 = new LearningOutcome { Code = "M.3.1.1.3.", Description = "Bir doğal sayıyı en yakın onluğa veya yüzlüğe yuvarlar.", SubTopic = subTopicDogalSayilar };
                var k3114 = new LearningOutcome { Code = "M.3.1.1.4.", Description = "1000 içinde herhangi bir doğal sayının basamak değerini bulur.", SubTopic = subTopicDogalSayilar };
                var k3115 = new LearningOutcome { Code = "M.3.1.1.5.", Description = "1000’den küçük doğal sayıları sıralar.", SubTopic = subTopicDogalSayilar };
                var k3116 = new LearningOutcome { Code = "M.3.1.1.6.", Description = "Bir örüntüde verilmeyen ögeleri bulur.", SubTopic = subTopicDogalSayilar };
                context.LearningOutcomes.AddRange(k3111, k3112, k3113, k3114, k3115, k3116);
                context.SaveChanges();

                // Detaylar - Doğal Sayılar
                context.LearningOutcomeDetails.AddRange(
                    new LearningOutcomeDetail { DetailText = "1000’den küçük doğal sayılar üzerinde çalışılır.", LearningOutcome = k3111 },
                    new LearningOutcomeDetail { DetailText = "Basamak, basamak değeri, sayı değeri kavramlarına değinilir. 1000 içinde çalışılır.", LearningOutcome = k3112 },
                    new LearningOutcomeDetail { DetailText = "Doğal sayıları en yakın onluğa ve yüzlüğe yuvarlama ile ilgili çalışmalar yapılır.", LearningOutcome = k3113 },
                    new LearningOutcomeDetail { DetailText = "1000’den küçük doğal sayıları küçükten büyüğe veya büyükten küçüğe doğru sıralama çalışmaları yapılır.", LearningOutcome = k3115 },
                    new LearningOutcomeDetail { DetailText = "Kuralı verilen ve bir elemanı eksik olan örüntülerde, eksik olan ögeleri bulmaya yönelik çalışmalar yapılır.", LearningOutcome = k3116 }
                );
                context.SaveChanges();

                // Kazanımlar - Toplama ve Çıkarma İşlemi (3. Sınıf)
                var k3117 = new LearningOutcome { Code = "M.3.1.1.7.", Description = "Doğal sayılarla toplama işlemini yapar.", SubTopic = subTopicToplamaCikarma };
                var k3118 = new LearningOutcome { Code = "M.3.1.1.8.", Description = "Doğal sayılarla çıkarma işlemini yapar.", SubTopic = subTopicToplamaCikarma };
                var k3119 = new LearningOutcome { Code = "M.3.1.1.9.", Description = "İki doğal sayının farkını tahmin eder.", SubTopic = subTopicToplamaCikarma };
                context.LearningOutcomes.AddRange(k3117, k3118, k3119);
                context.SaveChanges();

                // Detaylar - Toplama ve Çıkarma İşlemi
                context.LearningOutcomeDetails.AddRange(
                    new LearningOutcomeDetail { DetailText = "Doğal sayılarla eldesiz ve eldeli toplama işlemleri yapılır. En çok dört basamaklı sayılarla iki toplananlı toplama işlemi yapılır. Üç ve daha fazla toplananlı toplama işlemi, iki toplananlının toplamı üzerinden açıklanır.", LearningOutcome = k3117 },
                    new LearningOutcomeDetail { DetailText = "Doğal sayılarla onluk bozmayı gerektirmeyen ve gerektiren çıkarma işlemleri yapılır. En çok dört basamaklı doğal sayılarla çıkarma işlemi yapılır.", LearningOutcome = k3118 },
                    new LearningOutcomeDetail { DetailText = "İki sayının farkını tahmin ederken stratejiler kullanmasına yönelik çalışmalar yapılır.", LearningOutcome = k3119 }
                );
                context.SaveChanges();
                
                // Kazanımlar - Çarpma ve Bölme İşlemleri (3. Sınıf)
                var k3120 = new LearningOutcome { Code = "M.3.1.2.1.", Description = "Doğal sayılarla çarpma işlemini yapar.", SubTopic = subTopicCarpmaBolme };
                var k3121 = new LearningOutcome { Code = "M.3.1.2.2.", Description = "Doğal sayılarla bölme işlemini yapar.", SubTopic = subTopicCarpmaBolme };
                var k3122 = new LearningOutcome { Code = "M.3.1.2.3.", Description = "Çarpma ve bölme işlemleri arasındaki ilişkiyi açıklar.", SubTopic = subTopicCarpmaBolme };
                context.LearningOutcomes.AddRange(k3120, k3121, k3122);
                context.SaveChanges();

                // Detaylar - Çarpma ve Bölme İşlemleri
                context.LearningOutcomeDetails.AddRange(
                    new LearningOutcomeDetail { DetailText = "Doğal sayılarla eldesiz ve eldeli çarpma işlemleri yapılır. En çok üç basamaklı bir doğal sayıyı, en çok iki basamaklı bir doğal sayıyla çarpma çalışmaları yapılır.", LearningOutcome = k3120 },
                    new LearningOutcomeDetail { DetailText = "Doğal sayılarla kalansız ve kalanlı bölme işlemleri yapılır. En çok üç basamaklı bir doğal sayıyı, en çok iki basamaklı bir doğal sayıya bölme çalışmaları yapılır.", LearningOutcome = k3121 },
                    new LearningOutcomeDetail { DetailText = "Çarpma ve bölme işlemlerinin birbirinin tersi olduğu, bölünenin bölen ile bölümün çarpımına kalanın eklenmesiyle bulunduğu gibi ilişkilere yönelik çalışmalar yapılır.", LearningOutcome = k3122 }
                );
                context.SaveChanges();

                // Kazanımlar - Kesirler (3. Sınıf)
                var k3123 = new LearningOutcome { Code = "M.3.1.3.1.", Description = "Bütün, yarım ve çeyrek arasındaki ilişkiyi açıklar.", SubTopic = subTopicKesirler };
                var k3124 = new LearningOutcome { Code = "M.3.1.3.2.", Description = "Bir bütünü eş parçalara ayırarak birim kesirleri adlandırır.", SubTopic = subTopicKesirler };
                context.LearningOutcomes.AddRange(k3123, k3124);
                context.SaveChanges();

                // Detaylar - Kesirler
                context.LearningOutcomeDetails.AddRange(
                    new LearningOutcomeDetail { DetailText = "Bütün, yarım ve çeyrek kavramları somut nesneler ve şekiller üzerinde gösterilir.", LearningOutcome = k3123 },
                    new LearningOutcomeDetail { DetailText = "Bir bütünün eş parçalarını birim kesirle adlandırmaya yönelik çalışmalar yapılır.", LearningOutcome = k3124 }
                );
                context.SaveChanges();

                // Kazanımlar - Paralarımız (3. Sınıf)
                var k3125 = new LearningOutcome { Code = "M.3.1.4.1.", Description = "Türk Lirası ile ilgili problemleri çözer.", SubTopic = subTopicParalarimiz };
                context.LearningOutcomes.Add(k3125);
                context.SaveChanges();

                // Detaylar - Paralarımız
                context.LearningOutcomeDetails.Add(
                    new LearningOutcomeDetail { DetailText = "Türk Lirası ile ilgili günlük hayatta karşılaşılan durumları içeren problemler çözülür.", LearningOutcome = k3125 }
                );
                context.SaveChanges();

                // --- Geometri ve Ölçme Konusu (Topic) ---
                var topicGeometriOlcme = new Topic { Name = "Geometri ve Ölçme", Subject = matematikSubject, Grade = grade3 };
                context.Topics.Add(topicGeometriOlcme);
                context.SaveChanges();

                var subTopicGeometrikCisimlerSekiller = new SubTopic { Name = "Geometrik Cisimler ve Şekiller", Topic = topicGeometriOlcme };
                var subTopicKonumYon = new SubTopic { Name = "Konum ve Yön", Topic = topicGeometriOlcme };
                var subTopicUzunlukOlcme = new SubTopic { Name = "Uzunluk Ölçme", Topic = topicGeometriOlcme };
                var subTopicCevreAlan = new SubTopic { Name = "Çevre ve Alan", Topic = topicGeometriOlcme };
                var subTopicZamanOlcme = new SubTopic { Name = "Zaman Ölçme", Topic = topicGeometriOlcme };
                var subTopicSiviOlcme = new SubTopic { Name = "Sıvı Ölçme", Topic = topicGeometriOlcme };
                context.SubTopics.AddRange(subTopicGeometrikCisimlerSekiller, subTopicKonumYon, subTopicUzunlukOlcme, subTopicCevreAlan, subTopicZamanOlcme, subTopicSiviOlcme);
                context.SaveChanges();

                // Kazanımlar - Geometrik Cisimler ve Şekiller (3. Sınıf)
                var k3211 = new LearningOutcome { Code = "M.3.2.1.1.", Description = "Kare, dikdörtgen, üçgen ve çemberi tanır ve özelliklerini belirler.", SubTopic = subTopicGeometrikCisimlerSekiller };
                var k3212 = new LearningOutcome { Code = "M.3.2.1.2.", Description = "Geometrik cisimlerin yüzeylerini oluşturan düzlemsel şekilleri tanır.", SubTopic = subTopicGeometrikCisimlerSekiller };
                var k3213 = new LearningOutcome { Code = "M.3.2.1.3.", Description = "Bir doğru parçasını, doğruyu ve ışını tanır.", SubTopic = subTopicGeometrikCisimlerSekiller };
                context.LearningOutcomes.AddRange(k3211, k3212, k3213);
                context.SaveChanges();

                // Detaylar - Geometrik Cisimler ve Şekiller
                context.LearningOutcomeDetails.AddRange(
                    new LearningOutcomeDetail { DetailText = "Kare, dikdörtgen, üçgen ve çemberin köşe, kenar ve yüzey gibi temel özelliklerine değinilir.", LearningOutcome = k3211 },
                    new LearningOutcomeDetail { DetailText = "Küp, kare prizma, dikdörtgen prizma, üçgen prizma, silindir, koni ve kürenin yüzeylerini oluşturan düzlemsel şekiller belirlenir.", LearningOutcome = k3212 },
                    new LearningOutcomeDetail { DetailText = "Bir doğru parçasını, doğruyu ve ışını somut modeller üzerinde gösterir ve adlandırır.", LearningOutcome = k3213 }
                );
                context.SaveChanges();

                // Kazanımlar - Konum ve Yön (3. Sınıf)
                var k3221 = new LearningOutcome { Code = "M.3.2.2.1.", Description = "Bir nesnenin konumunu belirler ve yön bildirir.", SubTopic = subTopicKonumYon };
                context.LearningOutcomes.Add(k3221);
                context.SaveChanges();

                // Detaylar - Konum ve Yön
                context.LearningOutcomeDetails.Add(
                    new LearningOutcomeDetail { DetailText = "Bir nesnenin konumunu belirlerken yön (sağ, sol, ön, arka, üst, alt) ve mesafe kavramları kullanılır.", LearningOutcome = k3221 }
                );
                context.SaveChanges();

                // Kazanımlar - Uzunluk Ölçme (3. Sınıf)
                var k3231 = new LearningOutcome { Code = "M.3.2.3.1.", Description = "Standart uzunluk ölçme birimlerini tanır ve kullanır.", SubTopic = subTopicUzunlukOlcme };
                var k3232 = new LearningOutcome { Code = "M.3.2.3.2.", Description = "Uzunlukları tahmin eder ve tahminini ölçme yaparak kontrol eder.", SubTopic = subTopicUzunlukOlcme };
                context.LearningOutcomes.AddRange(k3231, k3232);
                context.SaveChanges();

                // Detaylar - Uzunluk Ölçme
                context.LearningOutcomeDetails.AddRange(
                    new LearningOutcomeDetail { DetailText = "Standart uzunluk ölçme birimleri (metre, santimetre, milimetre) arasındaki ilişkiler açıklanır ve problem çözümlerinde kullanılır.", LearningOutcome = k3231 },
                    new LearningOutcomeDetail { DetailText = "Günlük hayatta karşılaşılan durumları içeren uzunluk tahminleri yapılır ve ölçme araçlarıyla kontrol edilir.", LearningOutcome = k3232 }
                );
                context.SaveChanges();

                // Kazanımlar - Çevre ve Alan (3. Sınıf)
                var k3241 = new LearningOutcome { Code = "M.3.2.4.1.", Description = "Karesel, dikdörtgensel ve üçgensel bölgelerin çevrelerini hesaplar.", SubTopic = subTopicCevreAlan };
                var k3242 = new LearningOutcome { Code = "M.3.2.4.2.", Description = "Bir şeklin alanını birim karelerle ölçer.", SubTopic = subTopicCevreAlan };
                context.LearningOutcomes.AddRange(k3241, k3242);
                context.SaveChanges();

                // Detaylar - Çevre ve Alan
                context.LearningOutcomeDetails.AddRange(
                    new LearningOutcomeDetail { DetailText = "Karesel, dikdörtgensel ve üçgensel bölgelerin çevrelerini, kenar uzunlukları kullanılarak hesaplar.", LearningOutcome = k3241 },
                    new LearningOutcomeDetail { DetailText = "Bir şeklin alanını birim karelerle kaplama yaparak ölçer.", LearningOutcome = k3242 }
                );
                context.SaveChanges();

                // Kazanımlar - Zaman Ölçme (3. Sınıf)
                var k3251 = new LearningOutcome { Code = "M.3.2.5.1.", Description = "Zamanı ölçme birimleri arasındaki ilişkileri açıklar.", SubTopic = subTopicZamanOlcme };
                var k3252 = new LearningOutcome { Code = "M.3.2.5.2.", Description = "Zaman ölçü birimlerini kullanarak problemleri çözer.", SubTopic = subTopicZamanOlcme };
                context.LearningOutcomes.AddRange(k3251, k3252);
                context.SaveChanges();

                // Detaylar - Zaman Ölçme
                context.LearningOutcomeDetails.AddRange(
                    new LearningOutcomeDetail { DetailText = "Zaman ölçme birimleri (saat, dakika, saniye, gün, hafta, ay, yıl) arasındaki ilişkiler örneklerle açıklanır.", LearningOutcome = k3251 },
                    new LearningOutcomeDetail { DetailText = "Günlük hayattan zaman ölçme ile ilgili problemler çözülür.", LearningOutcome = k3252 }
                );
                context.SaveChanges();

                // Kazanımlar - Sıvı Ölçme (3. Sınıf)
                var k3261 = new LearningOutcome { Code = "M.3.2.6.1.", Description = "Sıvı ölçme birimlerini tanır ve kullanır.", SubTopic = subTopicSiviOlcme };
                var k3262 = new LearningOutcome { Code = "M.3.2.6.2.", Description = "Sıvı miktarlarını tahmin eder ve tahminini ölçme yaparak kontrol eder.", SubTopic = subTopicSiviOlcme };
                context.LearningOutcomes.AddRange(k3261, k3262);
                context.SaveChanges();

                // Detaylar - Sıvı Ölçme
                context.LearningOutcomeDetails.AddRange(
                    new LearningOutcomeDetail { DetailText = "Sıvı ölçme birimleri (litre, yarım litre, çeyrek litre) arasındaki ilişkiler açıklanır ve kullanılır.", LearningOutcome = k3261 },
                    new LearningOutcomeDetail { DetailText = "Günlük hayatta karşılaşılan sıvı miktarlarını tahmin eder ve ölçme araçlarıyla kontrol eder.", LearningOutcome = k3262 }
                );
                context.SaveChanges();

                // --- Veri İşleme Konusu (Topic) ---
                var topicVeriIsleme = new Topic { Name = "Veri İşleme", Subject = matematikSubject, Grade = grade3 };
                context.Topics.Add(topicVeriIsleme);
                context.SaveChanges();

                var subTopicTabloGrafik = new SubTopic { Name = "Tablo ve Grafik", Topic = topicVeriIsleme };
                context.SubTopics.Add(subTopicTabloGrafik);
                context.SaveChanges();

                // Kazanımlar - Tablo ve Grafik (3. Sınıf)
                var k3311 = new LearningOutcome { Code = "M.3.3.1.1.", Description = "Verileri tablo ve çetele ile gösterir.", SubTopic = subTopicTabloGrafik };
                var k3312 = new LearningOutcome { Code = "M.3.3.1.2.", Description = "Sıklık tablosu ve sütun grafiği oluşturur.", SubTopic = subTopicTabloGrafik };
                var k3313 = new LearningOutcome { Code = "M.3.3.1.3.", Description = "Tablo ve grafiklerdeki bilgileri kullanarak problem çözer.", SubTopic = subTopicTabloGrafik };
                context.LearningOutcomes.AddRange(k3311, k3312, k3313);
                context.SaveChanges();

                // Detaylar - Tablo ve Grafik
                context.LearningOutcomeDetails.AddRange(
                    new LearningOutcomeDetail { DetailText = "İki farklı kategoriye ait verileri tablo ve çetele kullanarak gösterir.", LearningOutcome = k3311 },
                    new LearningOutcomeDetail { DetailText = "İki farklı kategoriye ait verilerle sıklık tablosu ve sütun grafiği oluşturur. Grafiğin başlığı, eksen başlıkları ve birim aralıkları gibi ögelerine dikkat edilir.", LearningOutcome = k3312 },
                    new LearningOutcomeDetail { DetailText = "Tablo ve grafiklerdeki bilgileri kullanarak basit problemler çözer.", LearningOutcome = k3313 }
                );
                context.SaveChanges();

                // Tüm veriler başarıyla eklendi.
            }
        }
    }
}
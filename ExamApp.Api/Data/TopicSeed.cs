using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;

public static class TopicSeed
{

    public static void SeedData(ModelBuilder modelBuilder){
        //SeedGrade1(modelBuilder);
        //SeedGrade2(modelBuilder);
        SeedGrade3(modelBuilder);
        SeedGrade4(modelBuilder);
    }
    public static void SeedGrade3(ModelBuilder modelBuilder) {
        SeedGrade3Math(modelBuilder);
        SeedGrade3Turkce(modelBuilder);
        SeedGrade3FenBilimleri(modelBuilder);
        SeedGrade3HayatBilgisi(modelBuilder);
    }

    public static void SeedGrade4(ModelBuilder modelBuilder)
    {
        SeedGrade4Turkce(modelBuilder);
        SeedGrade4Matematik(modelBuilder);
        SeedGrade4FenBilimleri(modelBuilder);
        SeedGrade4SosyalBilgiler(modelBuilder);
        SeedGrade4Ingilizce(modelBuilder);
    }

    public static void SeedGrade4Ingilizce(ModelBuilder modelBuilder)
    {
        var gradeId = 4; // 4. sınıf
        var ingilizceSubjectId = 5;

        var grade4IngilizceTopics = new List<Topic>
        {
            new Topic { Id = 202, GradeId = gradeId, SubjectId = ingilizceSubjectId, Name = "Greetings" },
            new Topic { Id = 203, GradeId = gradeId, SubjectId = ingilizceSubjectId, Name = "Classroom Rules" },
            new Topic { Id = 204, GradeId = gradeId, SubjectId = ingilizceSubjectId, Name = "Numbers and Counting" },
            new Topic { Id = 205, GradeId = gradeId, SubjectId = ingilizceSubjectId, Name = "Colors and Shapes" },
            new Topic { Id = 206, GradeId = gradeId, SubjectId = ingilizceSubjectId, Name = "My Family" },
            new Topic { Id = 207, GradeId = gradeId, SubjectId = ingilizceSubjectId, Name = "Daily Routines" },
            new Topic { Id = 208, GradeId = gradeId, SubjectId = ingilizceSubjectId, Name = "My House" },
            new Topic { Id = 209, GradeId = gradeId, SubjectId = ingilizceSubjectId, Name = "Food and Drinks" },
            new Topic { Id = 210, GradeId = gradeId, SubjectId = ingilizceSubjectId, Name = "Animals" }
        };

        var grade4IngilizceSubTopics = new List<SubTopic>
        {
            new SubTopic { Id = 211, TopicId = 202, Name = "Introducing Yourself" },
            new SubTopic { Id = 212, TopicId = 202, Name = "Common Greetings and Responses" },

            new SubTopic { Id = 213, TopicId = 203, Name = "Classroom Instructions" },
            new SubTopic { Id = 214, TopicId = 203, Name = "Polite Expressions" },

            new SubTopic { Id = 215, TopicId = 204, Name = "Counting to 100" },
            new SubTopic { Id = 216, TopicId = 204, Name = "Ordinal Numbers" },

            new SubTopic { Id = 217, TopicId = 205, Name = "Basic Colors" },
            new SubTopic { Id = 218, TopicId = 205, Name = "Shapes and Their Properties" },

            new SubTopic { Id = 219, TopicId = 206, Name = "Family Members" },
            new SubTopic { Id = 220, TopicId = 206, Name = "Describing My Family" },

            new SubTopic { Id = 221, TopicId = 207, Name = "Daily Activities" },
            new SubTopic { Id = 222, TopicId = 207, Name = "Telling the Time" },

            new SubTopic { Id = 223, TopicId = 208, Name = "Rooms in the House" },
            new SubTopic { Id = 224, TopicId = 208, Name = "Furniture and Objects" },

            new SubTopic { Id = 225, TopicId = 209, Name = "Common Foods" },
            new SubTopic { Id = 226, TopicId = 209, Name = "Healthy Eating" },

            new SubTopic { Id = 227, TopicId = 210, Name = "Domestic Animals" },
            new SubTopic { Id = 228, TopicId = 210, Name = "Wild Animals" }
        };

        // Verileri tekrar eklemeyi önlemek için HasData kullanıyoruz
        modelBuilder.Entity<Topic>().HasData(grade4IngilizceTopics);
        modelBuilder.Entity<SubTopic>().HasData(grade4IngilizceSubTopics);
    }


    public static void SeedGrade4SosyalBilgiler(ModelBuilder modelBuilder)
{
    var gradeId = 4; // 4. sınıf
    var sosyalBilgilerSubjectId = 4;

    var grade4SosyalBilgilerTopics = new List<Topic>
    {
        new Topic { Id = 181, GradeId = gradeId, SubjectId = sosyalBilgilerSubjectId, Name = "Birey ve Toplum" },
        new Topic { Id = 182, GradeId = gradeId, SubjectId = sosyalBilgilerSubjectId, Name = "Kültür ve Miras" },
        new Topic { Id = 183, GradeId = gradeId, SubjectId = sosyalBilgilerSubjectId, Name = "İnsanlar, Yerler ve Çevreler" },
        new Topic { Id = 184, GradeId = gradeId, SubjectId = sosyalBilgilerSubjectId, Name = "Bilim, Teknoloji ve Toplum" },
        new Topic { Id = 185, GradeId = gradeId, SubjectId = sosyalBilgilerSubjectId, Name = "Üretim, Dağıtım ve Tüketim" },
        new Topic { Id = 186, GradeId = gradeId, SubjectId = sosyalBilgilerSubjectId, Name = "Etkin Vatandaşlık" },
        new Topic { Id = 187, GradeId = gradeId, SubjectId = sosyalBilgilerSubjectId, Name = "Küresel Bağlantılar" }
    };

    var grade4SosyalBilgilerSubTopics = new List<SubTopic>
    {
        new SubTopic { Id = 188, TopicId = 181, Name = "Birey ve Toplum 1" },
        new SubTopic { Id = 189, TopicId = 181, Name = "Birey ve Toplum 2" },

        new SubTopic { Id = 190, TopicId = 182, Name = "Kültür ve Miras 1" },
        new SubTopic { Id = 191, TopicId = 182, Name = "Kültür ve Miras 2" },

        new SubTopic { Id = 192, TopicId = 183, Name = "İnsanlar, Yerler ve Çevre 1" },
        new SubTopic { Id = 193, TopicId = 183, Name = "İnsanlar, Yerler ve Çevre 2" },

        new SubTopic { Id = 194, TopicId = 184, Name = "Bilim, Teknoloji ve Toplum 1" },
        new SubTopic { Id = 195, TopicId = 184, Name = "Bilim, Teknoloji ve Toplum 2" },

        new SubTopic { Id = 196, TopicId = 185, Name = "Üretim, Dağıtım ve Tüketim 1" },
        new SubTopic { Id = 197, TopicId = 185, Name = "Üretim, Dağıtım ve Tüketim 2" },

        new SubTopic { Id = 198, TopicId = 186, Name = "Etkin Vatandaşlık 1" },
        new SubTopic { Id = 199, TopicId = 186, Name = "Etkin Vatandaşlık 2" },

        new SubTopic { Id = 200, TopicId = 187, Name = "Küresel Bağlantılar 1" },
        new SubTopic { Id = 201, TopicId = 187, Name = "Küresel Bağlantılar 2" }
    };

    // Verileri tekrar eklemeyi önlemek için HasData kullanıyoruz
    modelBuilder.Entity<Topic>().HasData(grade4SosyalBilgilerTopics);
    modelBuilder.Entity<SubTopic>().HasData(grade4SosyalBilgilerSubTopics);
}


    public static void SeedGrade4FenBilimleri(ModelBuilder modelBuilder)
{
    var gradeId = 4; // 4. sınıf
    var fenBilimleriSubjectId = 4;

    var grade4FenBilimleriTopics = new List<Topic>
    {
        new Topic { Id = 158, GradeId = gradeId, SubjectId = fenBilimleriSubjectId, Name = "Yer Kabuğu ve Dünyamızın Hareketleri" },
        new Topic { Id = 159, GradeId = gradeId, SubjectId = fenBilimleriSubjectId, Name = "Besinlerimiz" },
        new Topic { Id = 160, GradeId = gradeId, SubjectId = fenBilimleriSubjectId, Name = "Kuvvetin Etkileri" },
        new Topic { Id = 161, GradeId = gradeId, SubjectId = fenBilimleriSubjectId, Name = "Maddenin Özellikleri" },
        new Topic { Id = 162, GradeId = gradeId, SubjectId = fenBilimleriSubjectId, Name = "Aydınlatma ve Ses Teknolojileri" },
        new Topic { Id = 163, GradeId = gradeId, SubjectId = fenBilimleriSubjectId, Name = "İnsan ve Çevre / Canlılar ve Yaşam" },
        new Topic { Id = 164, GradeId = gradeId, SubjectId = fenBilimleriSubjectId, Name = "Basit Elektrik Devreleri" }
    };

    var grade4FenBilimleriSubTopics = new List<SubTopic>
    {
        new SubTopic { Id = 165, TopicId = 158, Name = "Yer Kabuğunun Yapısı" },
        new SubTopic { Id = 166, TopicId = 158, Name = "Dünyamızın Hareketleri" },

        new SubTopic { Id = 167, TopicId = 159, Name = "Besinler ve Özellikleri" },
        new SubTopic { Id = 168, TopicId = 159, Name = "İnsan Sağlığı ve Zararlı Maddeler" },

        new SubTopic { Id = 169, TopicId = 160, Name = "Kuvvetin Cisimler Üzerindeki Etkileri" },
        new SubTopic { Id = 170, TopicId = 160, Name = "Mıknatısın Çekme Kuvveti" },

        new SubTopic { Id = 171, TopicId = 161, Name = "Maddeyi Niteleyen Özellikler" },
        new SubTopic { Id = 172, TopicId = 161, Name = "Maddenin Ölçülebilir Özellikleri" },
        new SubTopic { Id = 173, TopicId = 161, Name = "Maddenin Halleri" },
        new SubTopic { Id = 174, TopicId = 161, Name = "Maddenin Isı Etkisiyle Değişimi" },

        new SubTopic { Id = 175, TopicId = 162, Name = "Aydınlatma Teknolojileri" },
        new SubTopic { Id = 176, TopicId = 162, Name = "Uygun Aydınlatma - Işık Kirliliği" },
        new SubTopic { Id = 177, TopicId = 162, Name = "Geçmişten Günümüze Ses Teknolojileri" },

        new SubTopic { Id = 178, TopicId = 163, Name = "Bilinçli Tüketici - Tasarruf" },
        new SubTopic { Id = 179, TopicId = 163, Name = "Geri Dönüşümün Önemi" },

        new SubTopic { Id = 180, TopicId = 164, Name = "Basit Elektrik Devreleri" }
    };

    // Verileri tekrar eklemeyi önlemek için HasData kullanıyoruz
    modelBuilder.Entity<Topic>().HasData(grade4FenBilimleriTopics);
    modelBuilder.Entity<SubTopic>().HasData(grade4FenBilimleriSubTopics);
}

    public static void SeedGrade4Matematik(ModelBuilder modelBuilder){
        var gradeId = 4; // 4. sınıf
        var matematikSubjectId = 2;

        var grade4MatematikTopics = new List<Topic>
        {
            new Topic { Id = 132, GradeId = gradeId, SubjectId = matematikSubjectId, Name = "Doğal Sayılar ve İşlemler" },
            new Topic { Id = 133, GradeId = gradeId, SubjectId = matematikSubjectId, Name = "Kesirler ve Kesirlerle İşlemler" },
            new Topic { Id = 134, GradeId = gradeId, SubjectId = matematikSubjectId, Name = "Ondalık Gösterim ve Yüzdeler" },
            new Topic { Id = 135, GradeId = gradeId, SubjectId = matematikSubjectId, Name = "Temel Geometrik Kavramlar ve Çizimler" },
            new Topic { Id = 136, GradeId = gradeId, SubjectId = matematikSubjectId, Name = "Veri Toplama ve Değerlendirme / Uzunluk ve Zaman Ölçme" },
            new Topic { Id = 137, GradeId = gradeId, SubjectId = matematikSubjectId, Name = "Alan Ölçme ve Geometrik Cisimler" }
        };

        var grade4MatematikSubTopics = new List<SubTopic>
        {
            new SubTopic { Id = 138, TopicId = 132, Name = "Milyonlar" },
            new SubTopic { Id = 139, TopicId = 132, Name = "Sayı ve Şekil Örüntüleri" },
            new SubTopic { Id = 140, TopicId = 132, Name = "Doğal Sayılarla Toplama ve Çıkarma İşlemleri" },
            new SubTopic { Id = 141, TopicId = 132, Name = "Doğal Sayılarla Çarpma ve Bölme İşlemleri" },
            new SubTopic { Id = 142, TopicId = 132, Name = "Bir Doğal Sayının Karesi ve Küpü" },

            new SubTopic { Id = 143, TopicId = 133, Name = "Birim Kesirler" },
            new SubTopic { Id = 144, TopicId = 133, Name = "Denk Kesirler" },
            new SubTopic { Id = 145, TopicId = 133, Name = "Kesirlerde Sıralama" },
            new SubTopic { Id = 146, TopicId = 133, Name = "Basit Kesirlerde İşlemler" },
            new SubTopic { Id = 147, TopicId = 133, Name = "Kesirlerde Problemler" },

            new SubTopic { Id = 148, TopicId = 134, Name = "Ondalık Gösterim" },
            new SubTopic { Id = 149, TopicId = 134, Name = "Yüzdeler" },

            new SubTopic { Id = 150, TopicId = 135, Name = "Temel Geometrik Kavramlar ve Çizimler" },
            new SubTopic { Id = 151, TopicId = 135, Name = "Üçgenler ve Dörtgenler" },

            new SubTopic { Id = 152, TopicId = 136, Name = "Veri Toplama ve Değerlendirme" },
            new SubTopic { Id = 153, TopicId = 136, Name = "Uzunluk Ölçüleri" },
            new SubTopic { Id = 154, TopicId = 136, Name = "Çevre Uzunluğu" },
            new SubTopic { Id = 155, TopicId = 136, Name = "Zaman Ölçüleri" },

            new SubTopic { Id = 156, TopicId = 137, Name = "Alan Ölçme" },
            new SubTopic { Id = 157, TopicId = 137, Name = "Geometrik Cisimler" }
        };

        // Verileri tekrar eklemeyi önlemek için HasData kullanıyoruz
        modelBuilder.Entity<Topic>().HasData(grade4MatematikTopics);
        modelBuilder.Entity<SubTopic>().HasData(grade4MatematikSubTopics);

    }

    public static void SeedGrade4Turkce(ModelBuilder modelBuilder){
        var gradeId = 4; // 4. sınıf
        var turkceSubjectId = 1;

        var grade4TurkceTopics = new List<Topic>
        {
            new Topic { Id = 99, GradeId = gradeId, SubjectId = turkceSubjectId, Name = "Harf, Hece ve Sözcük Bilgisi" },
            new Topic { Id = 100, GradeId = gradeId, SubjectId = turkceSubjectId, Name = "Cümle Bilgisi" },
            new Topic { Id = 101, GradeId = gradeId, SubjectId = turkceSubjectId, Name = "Okuma Anlama" },
            new Topic { Id = 102, GradeId = gradeId, SubjectId = turkceSubjectId, Name = "Sözcük Türleri" },
            new Topic { Id = 103, GradeId = gradeId, SubjectId = turkceSubjectId, Name = "Noktalama İşaretleri" },
            new Topic { Id = 104, GradeId = gradeId, SubjectId = turkceSubjectId, Name = "Yazım Kuralları" }
        };

        var grade4TurkceSubTopics = new List<SubTopic>
        {
            new SubTopic { Id = 105, TopicId = 99, Name = "Harf, Hece, Sözcük" },
            new SubTopic { Id = 106, TopicId = 99, Name = "Eş Anlamlı (Anlamdaş) Sözcükler" },
            new SubTopic { Id = 107, TopicId = 99, Name = "Zıt (Karşıt) Anlamlı Sözcükler" },
            new SubTopic { Id = 108, TopicId = 99, Name = "Eş Sesli (Sesteş) Sözcükler" },
            new SubTopic { Id = 109, TopicId = 99, Name = "Gerçek, Mecaz ve Terim Anlam" },
            new SubTopic { Id = 110, TopicId = 99, Name = "Sözcük Türetme - Ekler" },
            new SubTopic { Id = 111, TopicId = 99, Name = "Sözcükte Anlam" },

            new SubTopic { Id = 112, TopicId = 100, Name = "Cümle" },
            new SubTopic { Id = 113, TopicId = 100, Name = "Sebep - Sonuç İlişkileri" },
            new SubTopic { Id = 114, TopicId = 100, Name = "Karşılaştırmalar" },
            new SubTopic { Id = 115, TopicId = 100, Name = "Öznel - Nesnel Yargılar" },
            new SubTopic { Id = 116, TopicId = 100, Name = "Duygusal ve Abartılı İfadeler" },
            new SubTopic { Id = 117, TopicId = 100, Name = "Atasözü, Deyim, Özdeyiş" },
            new SubTopic { Id = 118, TopicId = 100, Name = "Dil, İfade ve Bilgi Yanlışları" },

            new SubTopic { Id = 119, TopicId = 101, Name = "5N 1K" },
            new SubTopic { Id = 120, TopicId = 101, Name = "Öykü Unsurları" },
            new SubTopic { Id = 121, TopicId = 101, Name = "Oluş Sırası" },
            new SubTopic { Id = 122, TopicId = 101, Name = "Başlık - Konu İlişkisi" },
            new SubTopic { Id = 123, TopicId = 101, Name = "Ana Düşünce" },
            new SubTopic { Id = 124, TopicId = 101, Name = "Okuma Anlama" },
            new SubTopic { Id = 125, TopicId = 101, Name = "Paragrafta Anlam" },

            new SubTopic { Id = 126, TopicId = 102, Name = "Adlar" },
            new SubTopic { Id = 127, TopicId = 102, Name = "Varlıkların Özelliklerini Belirten Sözcükler" },
            new SubTopic { Id = 128, TopicId = 102, Name = "Adın Yerine Kullanılan Sözcükler" },
            new SubTopic { Id = 129, TopicId = 102, Name = "Eylem Bildiren Sözcükler" },

            new SubTopic { Id = 130, TopicId = 103, Name = "Noktalama İşaretleri" },
            
            new SubTopic { Id = 131, TopicId = 104, Name = "Yazım Kuralları" }
        };

        // Verileri tekrar eklemeyi önlemek için HasData kullanıyoruz
        modelBuilder.Entity<Topic>().HasData(grade4TurkceTopics);
        modelBuilder.Entity<SubTopic>().HasData(grade4TurkceSubTopics);

    }

    public static void SeedGrade3HayatBilgisi(ModelBuilder modelBuilder) {
        var gradeId = 3; // 3. sınıf
        var hayatBilgisiSubjectId = 3;

        var grade1HayatBilgisiTopics = new List<Topic>
        {
            new Topic { Id = 78, GradeId = gradeId, SubjectId = hayatBilgisiSubjectId, Name = "Okulumuzda Hayat" },
            new Topic { Id = 79, GradeId = gradeId, SubjectId = hayatBilgisiSubjectId, Name = "Evimizde Hayat" },
            new Topic { Id = 80, GradeId = gradeId, SubjectId = hayatBilgisiSubjectId, Name = "Sağlıklı Hayat" },
            new Topic { Id = 81, GradeId = gradeId, SubjectId = hayatBilgisiSubjectId, Name = "Güvenli Hayat" },
            new Topic { Id = 82, GradeId = gradeId, SubjectId = hayatBilgisiSubjectId, Name = "Ülkemizde Hayat" },
            new Topic { Id = 83, GradeId = gradeId, SubjectId = hayatBilgisiSubjectId, Name = "Doğada Hayat" }
        };

        var grade1HayatBilgisiSubTopics = new List<SubTopic>
        {
            new SubTopic { Id = 84, TopicId = 78, Name = "Okulumuzda Hayat 1" },
            new SubTopic { Id = 85, TopicId = 78, Name = "Okulumuzda Hayat 2" },
            new SubTopic { Id = 86, TopicId = 78, Name = "Okulumuzda Hayat 3" },

            new SubTopic { Id = 87, TopicId = 79, Name = "Evimizde Hayat 1" },
            new SubTopic { Id = 88, TopicId = 79, Name = "Evimizde Hayat 2" },
            new SubTopic { Id = 89, TopicId = 79, Name = "Evimizde Hayat 3" },

            new SubTopic { Id = 90, TopicId = 80, Name = "Sağlıklı Hayat 1" },
            new SubTopic { Id = 91, TopicId = 80, Name = "Sağlıklı Hayat 2" },

            new SubTopic { Id = 92, TopicId = 81, Name = "Güvenli Hayat 1" },
            new SubTopic { Id = 93, TopicId = 81, Name = "Güvenli Hayat 2" },

            new SubTopic { Id = 94, TopicId = 82, Name = "Ülkemizde Hayat 1" },
            new SubTopic { Id = 95, TopicId = 82, Name = "Ülkemizde Hayat 2" },
            new SubTopic { Id = 96, TopicId = 82, Name = "Ülkemizde Hayat 3" },

            new SubTopic { Id = 97, TopicId = 83, Name = "Doğada Hayat 1" },
            new SubTopic { Id = 98, TopicId = 83, Name = "Doğada Hayat 2" }
        };

        // Verileri tekrar eklemeyi önlemek için HasData kullanıyoruz
        modelBuilder.Entity<Topic>().HasData(grade1HayatBilgisiTopics);
        modelBuilder.Entity<SubTopic>().HasData(grade1HayatBilgisiSubTopics);

    }

    public static void SeedGrade3FenBilimleri(ModelBuilder modelBuilder){
        var gradeId = 3; // 3. sınıf
        var fenBilimleriSubjectId = 4;

        var grade1FenBilimleriTopics = new List<Topic>
        {
            new Topic { Id = 50, GradeId = gradeId, SubjectId = fenBilimleriSubjectId, Name = "Gezegenimizi Tanıyalım" },
            new Topic { Id = 51, GradeId = gradeId, SubjectId = fenBilimleriSubjectId, Name = "Beş Duyumuz" },
            new Topic { Id = 52, GradeId = gradeId, SubjectId = fenBilimleriSubjectId, Name = "Kuvveti Tanıyalım" },
            new Topic { Id = 53, GradeId = gradeId, SubjectId = fenBilimleriSubjectId, Name = "Maddeyi Tanıyalım" },
            new Topic { Id = 54, GradeId = gradeId, SubjectId = fenBilimleriSubjectId, Name = "Çevremizdeki Işık ve Sesler" },
            new Topic { Id = 55, GradeId = gradeId, SubjectId = fenBilimleriSubjectId, Name = "Canlılar Dünyasına Yolculuk" },
            new Topic { Id = 56, GradeId = gradeId, SubjectId = fenBilimleriSubjectId, Name = "Elektrikli Araçlar" }
        };

        var grade1FenBilimleriSubTopics = new List<SubTopic>
        {
            new SubTopic { Id = 57, TopicId = 50, Name = "Dünya’nın Şekli" },
            new SubTopic { Id = 58, TopicId = 50, Name = "Dünya’nın Yapısı" },

            new SubTopic { Id = 59, TopicId = 51, Name = "Duyu Organları ve Önemi" },
            new SubTopic { Id = 60, TopicId = 51, Name = "Duyu Organlarının Temel Görevleri" },
            new SubTopic { Id = 61, TopicId = 51, Name = "Duyu Organlarının Sağlığı" },

            new SubTopic { Id = 62, TopicId = 52, Name = "Varlıkların Hareket Özellikleri" },
            new SubTopic { Id = 63, TopicId = 52, Name = "Cisimleri Hareket Ettirme ve Durdurma" },
            new SubTopic { Id = 64, TopicId = 52, Name = "Hareketli Cisimlerin Sebep Olabileceği Tehlikeler" },

            new SubTopic { Id = 65, TopicId = 53, Name = "Maddeyi Niteleyen Özellikler 1" },
            new SubTopic { Id = 66, TopicId = 53, Name = "Maddeyi Niteleyen Özellikler 2" },
            new SubTopic { Id = 67, TopicId = 53, Name = "Maddenin Halleri" },

            new SubTopic { Id = 68, TopicId = 54, Name = "Işığın Görmedeki Rolü" },
            new SubTopic { Id = 69, TopicId = 54, Name = "Işık Kaynakları" },
            new SubTopic { Id = 70, TopicId = 54, Name = "Sesin İşitmedeki Rolü" },
            new SubTopic { Id = 71, TopicId = 54, Name = "Çevremizdeki Sesler" },

            new SubTopic { Id = 72, TopicId = 55, Name = "Çevremizdeki Varlıkları Tanıyalım" },
            new SubTopic { Id = 73, TopicId = 55, Name = "Ben ve Çevrem" },
            new SubTopic { Id = 74, TopicId = 55, Name = "Doğal ve Yapay Çevre" },

            new SubTopic { Id = 75, TopicId = 56, Name = "Elektrikli Araç-Gereçler" },
            new SubTopic { Id = 76, TopicId = 56, Name = "Elektrik Kaynakları" },
            new SubTopic { Id = 77, TopicId = 56, Name = "Elektriğin Güvenli Kullanımı" }
        };

        // Verileri tekrar eklemeyi önlemek için HasData kullanıyoruz
        modelBuilder.Entity<Topic>().HasData(grade1FenBilimleriTopics);
        modelBuilder.Entity<SubTopic>().HasData(grade1FenBilimleriSubTopics);

    }
    
    public static void SeedGrade3Math(ModelBuilder modelBuilder) 
    {
        var gradeId = 3; // 3. sınıf
        var matematikSubjectId = 2;

        var grade1MatematikTopics = new List<Topic>
        {
            new Topic { Id = 7, GradeId = gradeId, SubjectId = matematikSubjectId, Name = "Doğal Sayılar ve Ritmik Saymalar" },
            new Topic { Id = 8, GradeId = gradeId, SubjectId = matematikSubjectId, Name = "Toplama ve Çıkarma İşlemi" },
            new Topic { Id = 9, GradeId = gradeId, SubjectId = matematikSubjectId, Name = "Çarpma ve Bölme İşlemi" },
            new Topic { Id = 10, GradeId = gradeId, SubjectId = matematikSubjectId, Name = "Kesirler" },
            new Topic { Id = 11, GradeId = gradeId, SubjectId = matematikSubjectId, Name = "Zaman Ölçme" },
            new Topic { Id = 12, GradeId = gradeId, SubjectId = matematikSubjectId, Name = "Geometrik Cisimler ve Örüntüler" },
            new Topic { Id = 13, GradeId = gradeId, SubjectId = matematikSubjectId, Name = "Uzunluk, Alan ve Çevre Ölçme" }
        };

        var grade1MatematikSubTopics = new List<SubTopic>
        {
            new SubTopic { Id = 24, TopicId = 7, Name = "Üç Basamaklı Doğal Sayılar" },
            new SubTopic { Id = 25, TopicId = 7, Name = "Birer, Onar ve Yüzer Ritmik Sayma" },
            new SubTopic { Id = 26, TopicId = 7, Name = "Basamak Adları ve Basamak Değerleri" },
            new SubTopic { Id = 27, TopicId = 7, Name = "En Yakın Onluk ve Yüzlük" },
            new SubTopic { Id = 28, TopicId = 7, Name = "Doğal Sayıları Karşılaştırma ve Sıralama" },
            new SubTopic { Id = 29, TopicId = 7, Name = "Ritmik Saymalar" },
            new SubTopic { Id = 30, TopicId = 7, Name = "Sayı Örüntüleri" },

            new SubTopic { Id = 31, TopicId = 8, Name = "Eldeli ve Eldesiz Toplama İşlemi" },
            new SubTopic { Id = 32, TopicId = 8, Name = "Toplananların Yer Değiştirmesi" },
            new SubTopic { Id = 33, TopicId = 8, Name = "Onluk Bozmadan ve Bozarak Çıkarma İşlemi" },
            new SubTopic { Id = 34, TopicId = 8, Name = "10’un ve 100’ün Katlarıyla Zihinden Çıkarma İşlemi" },

            new SubTopic { Id = 35, TopicId = 9, Name = "Çarpım Tablosu" },
            new SubTopic { Id = 36, TopicId = 9, Name = "Çarpma İşlemi" },
            new SubTopic { Id = 37, TopicId = 9, Name = "10 ve 100 ile Kısa Yoldan Çarpma İşlemi" },
            new SubTopic { Id = 38, TopicId = 9, Name = "Çarpma İşlemi ile İlgili Problemler" },
            new SubTopic { Id = 39, TopicId = 9, Name = "İki Basamaklı Doğal Sayılarla Bölme İşlemi" },
            new SubTopic { Id = 40, TopicId = 9, Name = "Bölme İşlemi ile İlgili Problemler" },

            new SubTopic { Id = 41, TopicId = 10, Name = "Kesirler" },
            new SubTopic { Id = 42, TopicId = 10, Name = "Bir Çokluğun Belirtilen Kesir Kadarı" },

            new SubTopic { Id = 43, TopicId = 11, Name = "Zaman Ölçme" },
            new SubTopic { Id = 44, TopicId = 11, Name = "Zaman Ölçme ile İlgili Problemler" },

            new SubTopic { Id = 45, TopicId = 12, Name = "Geometrik Cisimler" },
            new SubTopic { Id = 46, TopicId = 12, Name = "Örüntüler" },

            new SubTopic { Id = 47, TopicId = 13, Name = "Uzunluk Ölçme" },
            new SubTopic { Id = 48, TopicId = 13, Name = "Alan Ölçme" },
            new SubTopic { Id = 49, TopicId = 13, Name = "Çevre Ölçme" }
        };

        // Verileri tekrar eklemeyi önlemek için HasData kullanıyoruz
        modelBuilder.Entity<Topic>().HasData(grade1MatematikTopics);
        modelBuilder.Entity<SubTopic>().HasData(grade1MatematikSubTopics);

    }
    public static void SeedGrade3Turkce(ModelBuilder modelBuilder)
    {
        var gradeId = 3; // 3. sınıf
        var turkcesubjectId = 1;

        var grade1TurkceTopics = new List<Topic>
        {
            new Topic { Id = 1, GradeId = gradeId, SubjectId = turkcesubjectId, Name = "Harf, Hece, Sözcük ve Cümle Bilgisi" },
            new Topic { Id = 2, GradeId = gradeId, SubjectId = turkcesubjectId, Name = "Söz Varlığını Geliştirme" },
            new Topic { Id = 3, GradeId = gradeId, SubjectId = turkcesubjectId, Name = "Okuduğunu Anlama" },
            new Topic { Id = 4, GradeId = gradeId, SubjectId = turkcesubjectId, Name = "Sözcük Türleri" },
            new Topic { Id = 5, GradeId = gradeId, SubjectId = turkcesubjectId, Name = "Noktalama İşaretleri" },
            new Topic { Id = 6, GradeId = gradeId, SubjectId = turkcesubjectId, Name = "Yazım Kuralları" }
        };

        var grade1TurkceSubTopics = new List<SubTopic>
        {
            new SubTopic { Id = 1, TopicId = 1, Name = "Harf, Hece, Sözcük 1" },
            new SubTopic { Id = 2, TopicId = 1, Name = "Harf, Hece, Sözcük 2" },
            new SubTopic { Id = 3, TopicId = 1, Name = "Eş Anlamlı (Anlamdaş) Sözcükler" },
            new SubTopic { Id = 4, TopicId = 1, Name = "Zıt (Karşıt) Anlamlı Sözcükler" },
            new SubTopic { Id = 5, TopicId = 1, Name = "Eş Sesli (Sesteş) Sözcükler" },
            new SubTopic { Id = 6, TopicId = 1, Name = "Sözcük Türetme - Ekler" },
            new SubTopic { Id = 7, TopicId = 1, Name = "Sözcükte Anlam" },
            new SubTopic { Id = 8, TopicId = 1, Name = "Cümle Türleri" },
            new SubTopic { Id = 9, TopicId = 1, Name = "Cümlede Anlam" },

            new SubTopic { Id = 10, TopicId = 2, Name = "Sebep-Sonuç İlişkileri" },
            new SubTopic { Id = 11, TopicId = 2, Name = "Karşılaştırmalar" },
            new SubTopic { Id = 12, TopicId = 2, Name = "Betimlemeler" },

            new SubTopic { Id = 13, TopicId = 3, Name = "5N 1K" },
            new SubTopic { Id = 14, TopicId = 3, Name = "Olayların Oluş Sırası" },
            new SubTopic { Id = 15, TopicId = 3, Name = "Öykü Unsurları" },
            new SubTopic { Id = 16, TopicId = 3, Name = "Başlık Konu İlişkisi" },
            new SubTopic { Id = 17, TopicId = 3, Name = "Ana Duygu" },
            new SubTopic { Id = 18, TopicId = 3, Name = "Ana Düşünce" },
            new SubTopic { Id = 19, TopicId = 3, Name = "Okuma Anlama 1" },
            new SubTopic { Id = 20, TopicId = 3, Name = "Okuma Anlama 2" },
            new SubTopic { Id = 21, TopicId = 3, Name = "Okuma Anlama 3" },
            new SubTopic { Id = 22, TopicId = 3, Name = "Görsel Yorumlama 1" },
            new SubTopic { Id = 23, TopicId = 3, Name = "Görsel Yorumlama 2" }
        };

        // Verileri tekrar eklemeyi önlemek için HasData kullanıyoruz
        modelBuilder.Entity<Topic>().HasData(grade1TurkceTopics);
        modelBuilder.Entity<SubTopic>().HasData(grade1TurkceSubTopics);
    }
}

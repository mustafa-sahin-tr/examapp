using System.Collections.Generic;

namespace ExamApp.Api.Services.Teachers.Seed;

/// <summary>
/// Uydurma öğretmen ad/soyad havuzu (issue #217). Yaygın Türkçe adlar; gerçek kişilere referans yok.
/// Seçim <see cref="TeacherSeedPlan.PickName"/> ile deterministiktir — listeyi değiştirmek (sıra dahil)
/// yeniden koşuda farklı ad üretir; mevcut hesaplar e-posta ile eşleştiği için kopya açılmaz ama
/// FullName tutarsızlaşır. Ekleme yapılacaksa sona ekle.
/// </summary>
public static class TeacherSeedNamePool
{
    public static readonly IReadOnlyList<string> FirstNames =
    [
        "Ahmet", "Ayşe", "Mehmet", "Fatma", "Mustafa", "Emine", "Ali", "Hatice", "Hüseyin", "Zeynep",
        "Hasan", "Elif", "İbrahim", "Meryem", "İsmail", "Şerife", "Osman", "Sultan", "Yusuf", "Hanife",
        "Murat", "Merve", "Ömer", "Havva", "Ramazan", "Zehra", "Halil", "Esra", "Süleyman", "Rabia",
        "Abdullah", "Özlem", "Mahmut", "Yasemin", "Recep", "Hülya", "Salih", "Sevim", "Kadir", "Gülsüm",
        "Emre", "Derya", "Burak", "Büşra", "Serkan", "Sevgi", "Yakup", "Kübra", "Cemal", "Nurcan",
        "Erdem", "Seda", "Volkan", "Tuğba", "Tolga", "Gamze", "Onur", "Pınar", "Cengiz", "Nesrin",
        "Furkan", "Dilek", "Enes", "Gizem", "Kerem", "Sibel", "Barış", "Ebru", "Umut", "Nazlı",
        "Selim", "Melek", "Selçuk", "Aylin", "Cem", "Hacer", "Deniz", "Serpil", "Taner", "Filiz",
        "Levent", "Gül", "Orhan", "Selin", "Adem", "Nur", "Kemal", "Betül", "Ferhat", "Şule",
        "Bülent", "Canan", "Halim", "Damla", "Erol", "İrem", "Nihat", "Ceren", "Turgut", "Songül",
        "Bora", "Eda", "Alper", "Neslihan", "Metin", "Nilüfer", "Sinan", "Semra", "Necati", "Hande"
    ];

    public static readonly IReadOnlyList<string> LastNames =
    [
        "Yılmaz", "Kaya", "Demir", "Şahin", "Çelik", "Yıldız", "Yıldırım", "Öztürk", "Aydın", "Özdemir",
        "Arslan", "Doğan", "Kılıç", "Aslan", "Çetin", "Kara", "Koç", "Kurt", "Özkan", "Şimşek",
        "Polat", "Korkmaz", "Çakır", "Erdoğan", "Yavuz", "Güneş", "Aktaş", "Bulut", "Keskin", "Taş",
        "Turan", "Karaca", "Acar", "Ateş", "Işık", "Duran", "Tekin", "Sönmez", "Tunç", "Ünal",
        "Aksoy", "Bozkurt", "Özer", "Karataş", "Yalçın", "Kaplan", "Avcı", "Uçar", "Sarı", "Ergin",
        "Bayram", "Ekinci", "Toprak", "Güler", "Sezer", "Ayhan", "Erdem", "Dinç", "Uysal", "Bal",
        "Coşkun", "Yüksel", "Gündüz", "Yücel", "Çolak", "Öz", "Akın", "Karakaya", "Eren", "Türk",
        "Altın", "Altun", "Gül", "Kalkan", "Ulu", "Demirci", "Çiftçi", "Orhan", "Özcan", "Türkmen",
        "Kurtuluş", "Karadağ", "Sağlam", "Aygün", "Gürbüz", "Bilgin", "Çevik", "Tosun", "Kutlu", "Akbaş",
        "Balcı", "Yaman", "Özbek", "Şen", "Mutlu", "Ercan", "Efe", "Soylu", "Canpolat", "Gökçe",
        "Akgün", "Kocaman", "Erkan", "Dağ", "Boz", "Küçük", "Ada", "Bakır", "Yörük", "Alkan"
    ];
}

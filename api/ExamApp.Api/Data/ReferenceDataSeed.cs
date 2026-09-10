using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.Data;

/// <summary>
/// Türkiye il / ilçe referans verisi (issue #91). <see cref="CatalogSeed"/> ile aynı desen:
/// uygulama açılışında çağrılır, tablolar doluysa hiçbir şey yapmaz (idempotent).
///
/// Liste alfabetik il sırasında, il başına bir blok. Küçük sapmalar olabilir; bu bir
/// referans tablodur, ileride admin araçlarıyla veya düzeltme migration'ıyla güncellenebilir.
/// </summary>
public static class ReferenceDataSeed
{
    public static void Initialize(IServiceProvider serviceProvider)
    {
        using var context = new AppDbContext(
            serviceProvider.GetRequiredService<DbContextOptions<AppDbContext>>());

        if (context.Provinces.Any() || context.Districts.Any())
        {
            return; // zaten seed edilmiş
        }

        foreach (var (provinceName, districtNames) in Turkey)
        {
            var province = new Province { Name = provinceName };
            foreach (var districtName in districtNames)
            {
                province.Districts.Add(new District { Name = districtName, Province = province });
            }
            context.Provinces.Add(province);
        }

        context.SaveChanges();
    }

    /// <summary>81 il ve ilçeleri. (İl adı, ilçe adları) çiftleri.</summary>
    private static readonly (string Province, string[] Districts)[] Turkey =
    {
        ("Adana", new[]
        {
            "Aladağ", "Ceyhan", "Çukurova", "Feke", "İmamoğlu", "Karaisalı", "Karataş", "Kozan",
            "Pozantı", "Saimbeyli", "Sarıçam", "Seyhan", "Tufanbeyli", "Yumurtalık", "Yüreğir"
        }),
        ("Adıyaman", new[]
        {
            "Besni", "Çelikhan", "Gerger", "Gölbaşı", "Kahta", "Merkez", "Samsat", "Sincik", "Tut"
        }),
        ("Afyonkarahisar", new[]
        {
            "Başmakçı", "Bayat", "Bolvadin", "Çay", "Çobanlar", "Dazkırı", "Dinar", "Emirdağ", "Evciler",
            "Hocalar", "İhsaniye", "İscehisar", "Kızılören", "Merkez", "Sandıklı", "Sinanpaşa", "Sultandağı", "Şuhut"
        }),
        ("Ağrı", new[]
        {
            "Diyadin", "Doğubayazıt", "Eleşkirt", "Hamur", "Merkez", "Patnos", "Taşlıçay", "Tutak"
        }),
        ("Aksaray", new[]
        {
            "Ağaçören", "Eskil", "Gülağaç", "Güzelyurt", "Merkez", "Ortaköy", "Sarıyahşi", "Sultanhanı"
        }),
        ("Amasya", new[]
        {
            "Göynücek", "Gümüşhacıköy", "Hamamözü", "Merkez", "Merzifon", "Suluova", "Taşova"
        }),
        ("Ankara", new[]
        {
            "Akyurt", "Altındağ", "Ayaş", "Bala", "Beypazarı", "Çamlıdere", "Çankaya", "Çubuk", "Elmadağ",
            "Etimesgut", "Evren", "Gölbaşı", "Güdül", "Haymana", "Kahramankazan", "Kalecik", "Keçiören",
            "Kızılcahamam", "Mamak", "Nallıhan", "Polatlı", "Pursaklar", "Sincan", "Şereflikoçhisar", "Yenimahalle"
        }),
        ("Antalya", new[]
        {
            "Akseki", "Aksu", "Alanya", "Demre", "Döşemealtı", "Elmalı", "Finike", "Gazipaşa", "Gündoğmuş",
            "İbradı", "Kaş", "Kemer", "Kepez", "Konyaaltı", "Korkuteli", "Kumluca", "Manavgat", "Muratpaşa", "Serik"
        }),
        ("Ardahan", new[]
        {
            "Çıldır", "Damal", "Göle", "Hanak", "Merkez", "Posof"
        }),
        ("Artvin", new[]
        {
            "Ardanuç", "Arhavi", "Borçka", "Hopa", "Kemalpaşa", "Merkez", "Murgul", "Şavşat", "Yusufeli"
        }),
        ("Aydın", new[]
        {
            "Bozdoğan", "Buharkent", "Çine", "Didim", "Efeler", "Germencik", "İncirliova", "Karacasu",
            "Karpuzlu", "Koçarlı", "Köşk", "Kuşadası", "Kuyucak", "Nazilli", "Söke", "Sultanhisar", "Yenipazar"
        }),
        ("Balıkesir", new[]
        {
            "Altıeylül", "Ayvalık", "Balya", "Bandırma", "Bigadiç", "Burhaniye", "Dursunbey", "Edremit", "Erdek",
            "Gömeç", "Gönen", "Havran", "İvrindi", "Karesi", "Kepsut", "Manyas", "Marmara", "Savaştepe",
            "Sındırgı", "Susurluk"
        }),
        ("Bartın", new[]
        {
            "Amasra", "Kurucaşile", "Merkez", "Ulus"
        }),
        ("Batman", new[]
        {
            "Beşiri", "Gercüş", "Hasankeyf", "Kozluk", "Merkez", "Sason"
        }),
        ("Bayburt", new[]
        {
            "Aydıntepe", "Demirözü", "Merkez"
        }),
        ("Bilecik", new[]
        {
            "Bozüyük", "Gölpazarı", "İnhisar", "Merkez", "Osmaneli", "Pazaryeri", "Söğüt", "Yenipazar"
        }),
        ("Bingöl", new[]
        {
            "Adaklı", "Genç", "Karlıova", "Kiğı", "Merkez", "Solhan", "Yayladere", "Yedisu"
        }),
        ("Bitlis", new[]
        {
            "Adilcevaz", "Ahlat", "Güroymak", "Hizan", "Merkez", "Mutki", "Tatvan"
        }),
        ("Bolu", new[]
        {
            "Dörtdivan", "Gerede", "Göynük", "Kıbrıscık", "Mengen", "Merkez", "Mudurnu", "Seben", "Yeniçağa"
        }),
        ("Burdur", new[]
        {
            "Ağlasun", "Altınyayla", "Bucak", "Çavdır", "Çeltikçi", "Gölhisar", "Karamanlı", "Kemer",
            "Merkez", "Tefenni", "Yeşilova"
        }),
        ("Bursa", new[]
        {
            "Büyükorhan", "Gemlik", "Gürsu", "Harmancık", "İnegöl", "İznik", "Karacabey", "Keles", "Kestel",
            "Mudanya", "Mustafakemalpaşa", "Nilüfer", "Orhaneli", "Orhangazi", "Osmangazi", "Yenişehir", "Yıldırım"
        }),
        ("Çanakkale", new[]
        {
            "Ayvacık", "Bayramiç", "Biga", "Bozcaada", "Çan", "Eceabat", "Ezine", "Gelibolu", "Gökçeada",
            "Lapseki", "Merkez", "Yenice"
        }),
        ("Çankırı", new[]
        {
            "Atkaracalar", "Bayramören", "Çerkeş", "Eldivan", "Ilgaz", "Kızılırmak", "Korgun", "Kurşunlu",
            "Merkez", "Orta", "Şabanözü", "Yapraklı"
        }),
        ("Çorum", new[]
        {
            "Alaca", "Bayat", "Boğazkale", "Dodurga", "İskilip", "Kargı", "Laçin", "Mecitözü", "Merkez",
            "Oğuzlar", "Ortaköy", "Osmancık", "Sungurlu", "Uğurludağ"
        }),
        ("Denizli", new[]
        {
            "Acıpayam", "Babadağ", "Baklan", "Bekilli", "Beyağaç", "Bozkurt", "Buldan", "Çal", "Çameli",
            "Çardak", "Çivril", "Güney", "Honaz", "Kale", "Merkezefendi", "Pamukkale", "Sarayköy",
            "Serinhisar", "Tavas"
        }),
        ("Diyarbakır", new[]
        {
            "Bağlar", "Bismil", "Çermik", "Çınar", "Çüngüş", "Dicle", "Eğil", "Ergani", "Hani", "Hazro",
            "Kayapınar", "Kocaköy", "Kulp", "Lice", "Silvan", "Sur", "Yenişehir"
        }),
        ("Düzce", new[]
        {
            "Akçakoca", "Cumayeri", "Çilimli", "Gölyaka", "Gümüşova", "Kaynaşlı", "Merkez", "Yığılca"
        }),
        ("Edirne", new[]
        {
            "Enez", "Havsa", "İpsala", "Keşan", "Lalapaşa", "Meriç", "Merkez", "Süloğlu", "Uzunköprü"
        }),
        ("Elazığ", new[]
        {
            "Ağın", "Alacakaya", "Arıcak", "Baskil", "Karakoçan", "Keban", "Kovancılar", "Maden", "Merkez",
            "Palu", "Sivrice"
        }),
        ("Erzincan", new[]
        {
            "Çayırlı", "İliç", "Kemah", "Kemaliye", "Merkez", "Otlukbeli", "Refahiye", "Tercan", "Üzümlü"
        }),
        ("Erzurum", new[]
        {
            "Aşkale", "Aziziye", "Çat", "Hınıs", "Horasan", "İspir", "Karaçoban", "Karayazı", "Köprüköy",
            "Narman", "Oltu", "Olur", "Palandöken", "Pasinler", "Pazaryolu", "Şenkaya", "Tekman", "Tortum",
            "Uzundere", "Yakutiye"
        }),
        ("Eskişehir", new[]
        {
            "Alpu", "Beylikova", "Çifteler", "Günyüzü", "Han", "İnönü", "Mahmudiye", "Mihalgazi", "Mihalıççık",
            "Odunpazarı", "Sarıcakaya", "Seyitgazi", "Sivrihisar", "Tepebaşı"
        }),
        ("Gaziantep", new[]
        {
            "Araban", "İslahiye", "Karkamış", "Nizip", "Nurdağı", "Oğuzeli", "Şahinbey", "Şehitkamil", "Yavuzeli"
        }),
        ("Giresun", new[]
        {
            "Alucra", "Bulancak", "Çamoluk", "Çanakçı", "Dereli", "Doğankent", "Espiye", "Eynesil", "Görele",
            "Güce", "Keşap", "Merkez", "Piraziz", "Şebinkarahisar", "Tirebolu", "Yağlıdere"
        }),
        ("Gümüşhane", new[]
        {
            "Kelkit", "Köse", "Kürtün", "Merkez", "Şiran", "Torul"
        }),
        ("Hakkari", new[]
        {
            "Çukurca", "Derecik", "Merkez", "Şemdinli", "Yüksekova"
        }),
        ("Hatay", new[]
        {
            "Altınözü", "Antakya", "Arsuz", "Belen", "Defne", "Dörtyol", "Erzin", "Hassa", "İskenderun",
            "Kırıkhan", "Kumlu", "Payas", "Reyhanlı", "Samandağ", "Yayladağı"
        }),
        ("Iğdır", new[]
        {
            "Aralık", "Karakoyunlu", "Merkez", "Tuzluca"
        }),
        ("Isparta", new[]
        {
            "Aksu", "Atabey", "Eğirdir", "Gelendost", "Gönen", "Keçiborlu", "Merkez", "Senirkent", "Sütçüler",
            "Şarkikaraağaç", "Uluborlu", "Yalvaç", "Yenişarbademli"
        }),
        ("İstanbul", new[]
        {
            "Adalar", "Arnavutköy", "Ataşehir", "Avcılar", "Bağcılar", "Bahçelievler", "Bakırköy", "Başakşehir",
            "Bayrampaşa", "Beşiktaş", "Beykoz", "Beylikdüzü", "Beyoğlu", "Büyükçekmece", "Çatalca", "Çekmeköy",
            "Esenler", "Esenyurt", "Eyüpsultan", "Fatih", "Gaziosmanpaşa", "Güngören", "Kadıköy", "Kağıthane",
            "Kartal", "Küçükçekmece", "Maltepe", "Pendik", "Sancaktepe", "Sarıyer", "Silivri", "Sultanbeyli",
            "Sultangazi", "Şile", "Şişli", "Tuzla", "Ümraniye", "Üsküdar", "Zeytinburnu"
        }),
        ("İzmir", new[]
        {
            "Aliağa", "Balçova", "Bayındır", "Bayraklı", "Bergama", "Beydağ", "Bornova", "Buca", "Çeşme", "Çiğli",
            "Dikili", "Foça", "Gaziemir", "Güzelbahçe", "Karabağlar", "Karaburun", "Karşıyaka", "Kemalpaşa",
            "Kınık", "Kiraz", "Konak", "Menderes", "Menemen", "Narlıdere", "Ödemiş", "Seferihisar", "Selçuk",
            "Tire", "Torbalı", "Urla"
        }),
        ("Kahramanmaraş", new[]
        {
            "Afşin", "Andırın", "Çağlayancerit", "Dulkadiroğlu", "Ekinözü", "Elbistan", "Göksun", "Nurhak",
            "Onikişubat", "Pazarcık", "Türkoğlu"
        }),
        ("Karabük", new[]
        {
            "Eflani", "Eskipazar", "Merkez", "Ovacık", "Safranbolu", "Yenice"
        }),
        ("Karaman", new[]
        {
            "Ayrancı", "Başyayla", "Ermenek", "Kazımkarabekir", "Merkez", "Sarıveliler"
        }),
        ("Kars", new[]
        {
            "Akyaka", "Arpaçay", "Digor", "Kağızman", "Merkez", "Sarıkamış", "Selim", "Susuz"
        }),
        ("Kastamonu", new[]
        {
            "Abana", "Ağlı", "Araç", "Azdavay", "Bozkurt", "Cide", "Çatalzeytin", "Daday", "Devrekani",
            "Doğanyurt", "Hanönü", "İhsangazi", "İnebolu", "Küre", "Merkez", "Pınarbaşı", "Seydiler",
            "Şenpazar", "Taşköprü", "Tosya"
        }),
        ("Kayseri", new[]
        {
            "Akkışla", "Bünyan", "Develi", "Felahiye", "Hacılar", "İncesu", "Kocasinan", "Melikgazi", "Özvatan",
            "Pınarbaşı", "Sarıoğlan", "Sarız", "Talas", "Tomarza", "Yahyalı", "Yeşilhisar"
        }),
        ("Kırıkkale", new[]
        {
            "Bahşılı", "Balışeyh", "Çelebi", "Delice", "Karakeçili", "Keskin", "Merkez", "Sulakyurt", "Yahşihan"
        }),
        ("Kırklareli", new[]
        {
            "Babaeski", "Demirköy", "Kofçaz", "Lüleburgaz", "Merkez", "Pehlivanköy", "Pınarhisar", "Vize"
        }),
        ("Kırşehir", new[]
        {
            "Akçakent", "Akpınar", "Boztepe", "Çiçekdağı", "Kaman", "Merkez", "Mucur"
        }),
        ("Kilis", new[]
        {
            "Elbeyli", "Merkez", "Musabeyli", "Polateli"
        }),
        ("Kocaeli", new[]
        {
            "Başiskele", "Çayırova", "Darıca", "Derince", "Dilovası", "Gebze", "Gölcük", "İzmit", "Kandıra",
            "Karamürsel", "Kartepe", "Körfez"
        }),
        ("Konya", new[]
        {
            "Ahırlı", "Akören", "Akşehir", "Altınekin", "Beyşehir", "Bozkır", "Cihanbeyli", "Çeltik", "Çumra",
            "Derbent", "Derebucak", "Doğanhisar", "Emirgazi", "Ereğli", "Güneysınır", "Hadim", "Halkapınar",
            "Hüyük", "Ilgın", "Kadınhanı", "Karapınar", "Karatay", "Kulu", "Meram", "Sarayönü", "Selçuklu",
            "Seydişehir", "Taşkent", "Tuzlukçu", "Yalıhüyük", "Yunak"
        }),
        ("Kütahya", new[]
        {
            "Altıntaş", "Aslanapa", "Çavdarhisar", "Domaniç", "Dumlupınar", "Emet", "Gediz", "Hisarcık",
            "Merkez", "Pazarlar", "Simav", "Şaphane", "Tavşanlı"
        }),
        ("Malatya", new[]
        {
            "Akçadağ", "Arapgir", "Arguvan", "Battalgazi", "Darende", "Doğanşehir", "Doğanyol", "Hekimhan",
            "Kale", "Kuluncak", "Pütürge", "Yazıhan", "Yeşilyurt"
        }),
        ("Manisa", new[]
        {
            "Ahmetli", "Akhisar", "Alaşehir", "Demirci", "Gölmarmara", "Gördes", "Kırkağaç", "Köprübaşı", "Kula",
            "Salihli", "Sarıgöl", "Saruhanlı", "Selendi", "Soma", "Şehzadeler", "Turgutlu", "Yunusemre"
        }),
        ("Mardin", new[]
        {
            "Artuklu", "Dargeçit", "Derik", "Kızıltepe", "Mazıdağı", "Midyat", "Nusaybin", "Ömerli", "Savur", "Yeşilli"
        }),
        ("Mersin", new[]
        {
            "Akdeniz", "Anamur", "Aydıncık", "Bozyazı", "Çamlıyayla", "Erdemli", "Gülnar", "Mezitli", "Mut",
            "Silifke", "Tarsus", "Toroslar", "Yenişehir"
        }),
        ("Muğla", new[]
        {
            "Bodrum", "Dalaman", "Datça", "Fethiye", "Kavaklıdere", "Köyceğiz", "Marmaris", "Menteşe", "Milas",
            "Ortaca", "Seydikemer", "Ula", "Yatağan"
        }),
        ("Muş", new[]
        {
            "Bulanık", "Hasköy", "Korkut", "Malazgirt", "Merkez", "Varto"
        }),
        ("Nevşehir", new[]
        {
            "Acıgöl", "Avanos", "Derinkuyu", "Gülşehir", "Hacıbektaş", "Kozaklı", "Merkez", "Ürgüp"
        }),
        ("Niğde", new[]
        {
            "Altunhisar", "Bor", "Çamardı", "Çiftlik", "Merkez", "Ulukışla"
        }),
        ("Ordu", new[]
        {
            "Akkuş", "Altınordu", "Aybastı", "Çamaş", "Çatalpınar", "Çaybaşı", "Fatsa", "Gölköy", "Gülyalı",
            "Gürgentepe", "İkizce", "Kabadüz", "Kabataş", "Korgan", "Kumru", "Mesudiye", "Perşembe", "Ulubey", "Ünye"
        }),
        ("Osmaniye", new[]
        {
            "Bahçe", "Düziçi", "Hasanbeyli", "Kadirli", "Merkez", "Sumbas", "Toprakkale"
        }),
        ("Rize", new[]
        {
            "Ardeşen", "Çamlıhemşin", "Çayeli", "Derepazarı", "Fındıklı", "Güneysu", "Hemşin", "İkizdere",
            "İyidere", "Kalkandere", "Merkez", "Pazar"
        }),
        ("Sakarya", new[]
        {
            "Adapazarı", "Akyazı", "Arifiye", "Erenler", "Ferizli", "Geyve", "Hendek", "Karapürçek", "Karasu",
            "Kaynarca", "Kocaali", "Pamukova", "Sapanca", "Serdivan", "Söğütlü", "Taraklı"
        }),
        ("Samsun", new[]
        {
            "Alaçam", "Asarcık", "Atakum", "Ayvacık", "Bafra", "Canik", "Çarşamba", "Havza", "İlkadım", "Kavak",
            "Ladik", "Ondokuzmayıs", "Salıpazarı", "Tekkeköy", "Terme", "Vezirköprü", "Yakakent"
        }),
        ("Siirt", new[]
        {
            "Baykan", "Eruh", "Kurtalan", "Merkez", "Pervari", "Şirvan", "Tillo"
        }),
        ("Sinop", new[]
        {
            "Ayancık", "Boyabat", "Dikmen", "Durağan", "Erfelek", "Gerze", "Merkez", "Saraydüzü", "Türkeli"
        }),
        ("Sivas", new[]
        {
            "Akıncılar", "Altınyayla", "Divriği", "Doğanşar", "Gemerek", "Gölova", "Gürün", "Hafik", "İmranlı",
            "Kangal", "Koyulhisar", "Merkez", "Suşehri", "Şarkışla", "Ulaş", "Yıldızeli", "Zara"
        }),
        ("Şanlıurfa", new[]
        {
            "Akçakale", "Birecik", "Bozova", "Ceylanpınar", "Eyyübiye", "Halfeti", "Haliliye", "Harran", "Hilvan",
            "Karaköprü", "Siverek", "Suruç", "Viranşehir"
        }),
        ("Şırnak", new[]
        {
            "Beytüşşebap", "Cizre", "Güçlükonak", "İdil", "Merkez", "Silopi", "Uludere"
        }),
        ("Tekirdağ", new[]
        {
            "Çerkezköy", "Çorlu", "Ergene", "Hayrabolu", "Kapaklı", "Malkara", "Marmaraereğlisi", "Muratlı",
            "Saray", "Süleymanpaşa", "Şarköy"
        }),
        ("Tokat", new[]
        {
            "Almus", "Artova", "Başçiftlik", "Erbaa", "Merkez", "Niksar", "Pazar", "Reşadiye", "Sulusaray",
            "Turhal", "Yeşilyurt", "Zile"
        }),
        ("Trabzon", new[]
        {
            "Akçaabat", "Araklı", "Arsin", "Beşikdüzü", "Çarşıbaşı", "Çaykara", "Dernekpazarı", "Düzköy", "Hayrat",
            "Köprübaşı", "Maçka", "Of", "Ortahisar", "Sürmene", "Şalpazarı", "Tonya", "Vakfıkebir", "Yomra"
        }),
        ("Tunceli", new[]
        {
            "Çemişgezek", "Hozat", "Mazgirt", "Merkez", "Nazımiye", "Ovacık", "Pertek", "Pülümür"
        }),
        ("Uşak", new[]
        {
            "Banaz", "Eşme", "Karahallı", "Merkez", "Sivaslı", "Ulubey"
        }),
        ("Van", new[]
        {
            "Bahçesaray", "Başkale", "Çaldıran", "Çatak", "Edremit", "Erciş", "Gevaş", "Gürpınar", "İpekyolu",
            "Muradiye", "Özalp", "Saray", "Tuşba"
        }),
        ("Yalova", new[]
        {
            "Altınova", "Armutlu", "Çınarcık", "Çiftlikköy", "Merkez", "Termal"
        }),
        ("Yozgat", new[]
        {
            "Akdağmadeni", "Aydıncık", "Boğazlıyan", "Çandır", "Çayıralan", "Çekerek", "Kadışehri", "Merkez",
            "Saraykent", "Sarıkaya", "Sorgun", "Şefaatli", "Yenifakılı", "Yerköy"
        }),
        ("Zonguldak", new[]
        {
            "Alaplı", "Çaycuma", "Devrek", "Ereğli", "Gökçebey", "Kilimli", "Kozlu", "Merkez"
        }),
    };
}

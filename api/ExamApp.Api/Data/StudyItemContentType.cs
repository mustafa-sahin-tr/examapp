namespace ExamApp.Api.Data;

/// <summary>
/// Çalışma etkinliğinin (StudyItem) içerik tipi. Sıralama/değerler DB'de int olarak saklanır,
/// mevcut kayıtlar migration ile <see cref="Image"/> (0) alır — sıralamayı değiştirme.
/// </summary>
public enum StudyItemContentType
{
    /// <summary>Bir veya daha fazla resim (yaprak test/kitap sayfası görseli).</summary>
    Image = 0,

    /// <summary>Harici bağlantı (EBA, YouTube vb.).</summary>
    Link = 1,

    /// <summary>Var olan bir kitap/testin sayfa aralığı.</summary>
    BookPageRange = 2
}

/// <summary>Link tipi etkinliklerde bağlantının hangi platforma ait olduğu.</summary>
public enum StudyItemLinkPlatform
{
    Other = 0,
    Eba = 1,
    YouTube = 2
}

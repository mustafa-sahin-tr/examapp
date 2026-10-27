namespace ExamApp.Api.Data;

// Issue #577: kitap kaydı — sayfa ofsetleri, cevap anahtarı aralıkları ve içindekiler eşlemesi. Sayfa numaraları DOSYA
// sayfasıdır (PDF/tarama sırası, 1'den başlar); bastırılmış sayfa = dosya sayfası + Offset. Kitap kaydı güncellenirken
// bu kümeler bütün olarak değiştirilir (satır kimlikleri kalıcı değildir), bu yüzden BaseEntity / soft-delete yok.

/// <summary>Dosya aralığı [FileFrom, FileTo] için bastırılmış sayfa = dosya sayfası + Offset (negatif olabilir).</summary>
public class BookPageOffset
{
    public int Id { get; set; }

    public int BookId { get; set; }

    public int FileFrom { get; set; }

    public int FileTo { get; set; }

    public int Offset { get; set; }
}

/// <summary>Cevap anahtarı sayfaları (dosya aralığı, uçlar dahil).</summary>
public class BookAnswerKeyRange
{
    public int Id { get; set; }

    public int BookId { get; set; }

    public int FileFrom { get; set; }

    public int FileTo { get; set; }
}

/// <summary>İçindekiler satırı: dosya aralığı → sınıf / ders / (konu) / (alt konu). Aralıklar çakışmaz.</summary>
public class BookTocEntry
{
    public int Id { get; set; }

    public int BookId { get; set; }

    public int FileFrom { get; set; }

    public int FileTo { get; set; }

    public int GradeId { get; set; }

    public int SubjectId { get; set; }

    public int? TopicId { get; set; }

    public int? SubTopicId { get; set; }
}

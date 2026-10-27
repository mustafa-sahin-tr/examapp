namespace ExamApp.Api.Data;

/// <summary>
/// Issue #580: etkileşim çözümünün kaynağı. <see cref="None"/> = çözüm yok (MCQ ya da çözümsüz etkileşim). <see cref="Key"/> = kitabın
/// cevap anahtarından (Claude okudu), <see cref="AI"/> = anahtarsız maddede Claude'un önerisi, <see cref="Human"/> = öğretmen yazdı ya da
/// düzeltti. Veritabanında ad olarak (string) saklanır.
/// </summary>
public enum SolutionSource
{
    None,
    Human,
    Key,
    AI,
}

/// <summary>
/// Issue #580: çözümün onay durumu. <see cref="Pending"/> = Key/AI çözümü henüz Admin ya da QuestionTrainer onaylamadı — böyle bir
/// soru içeren etkinlik sayfası öğrenciye atanamaz, başlatılamaz ve keşfette görünmez. Çözümsüz ve insan çözümlü sorular
/// <see cref="Approved"/>. Veritabanında ad olarak saklanır.
/// </summary>
public enum SolutionStatus
{
    Approved,
    Pending,
}

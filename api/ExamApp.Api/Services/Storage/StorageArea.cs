using System;
using System.Collections.Generic;

namespace ExamApp.Api.Services.Storage;

/// <summary>
/// issue #365 (S2): tarayıcıya imzalı URL olarak verilebilen MinIO alanları. Her alanın bucket/prefix allowlist'i
/// <see cref="StorageAreaPolicy"/>'dedir. <c>question-transfer/*</c> (soru bankası export/import paketleri) HİÇBİR
/// alanda yoktur ve hiçbir zaman imzalanmaz.
/// </summary>
public enum StorageArea
{
    /// <summary>Soru, şık ve paragraf görselleri: varsayılan bucket'ta <c>questions/</c>, <c>answers/</c>, <c>passages/</c>.</summary>
    QuestionImage = 1,

    /// <summary>Worksheet kapak/arka plan görselleri: <c>worksheets</c> ve <c>exams</c> bucket'ları.</summary>
    WorksheetCover = 2,

    /// <summary>Çalışma etkinliği (study item) sayfa görselleri: <c>study-pages</c> bucket'ında <c>books/</c>, <c>pages/</c>.</summary>
    StudyPage = 3,
}

/// <summary>
/// issue #365 (S2): response DTO'sundaki <c>/img/{bucket}/{key}</c> değerli string alan. MVC JSON çıktısı yazılırken
/// (yalnız <c>AddControllers().AddJsonOptions</c> seçenekleri — SignalR, Redis, outbox serileştirmesi etkilenmez)
/// değer verilen alanlardan birinin allowlist'ine uyuyorsa kısa ömürlü imzalı göreli URL'ye çevrilir; uymuyorsa
/// olduğu gibi (imzasız) yazılır. Bellekteki DTO değeri değişmez.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class StorageUrlAttribute : Attribute
{
    public StorageUrlAttribute(params StorageArea[] areas)
    {
        if (areas is null || areas.Length == 0)
            throw new ArgumentException("At least one storage area is required.", nameof(areas));
        Areas = areas;
    }

    public IReadOnlyList<StorageArea> Areas { get; }
}

/// <summary>
/// issue #365 (S2): adı <c>*Url</c> ile biten ama MinIO görseli OLMAYAN (ya da bilerek imzalanmayan) alan. Mimari test
/// (<c>StorageUrlArchitectureTests</c>) her response DTO <c>*Url</c> alanının ya <see cref="StorageUrlAttribute"/> ya da
/// bunu taşımasını ister — yeni bir görsel alanı sessizce imzasız kalmasın.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class NotStorageUrlAttribute : Attribute
{
    public NotStorageUrlAttribute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required.", nameof(reason));
        Reason = reason;
    }

    public string Reason { get; }
}

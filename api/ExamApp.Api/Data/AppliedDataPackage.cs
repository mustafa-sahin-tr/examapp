using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// Records the last version of a data package shipped with the API (e.g. the MEB Maarif catalog)
/// that was applied to this database. Startup compares the file's SHA-256 with this row and
/// imports again only when the package changed.
///
/// Not a <see cref="BaseEntity"/> — it is configuration, not soft-deletable domain data.
/// </summary>
public class AppliedDataPackage
{
    [Key]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Sha256 { get; set; } = string.Empty;

    public DateTime AppliedAt { get; set; }
}

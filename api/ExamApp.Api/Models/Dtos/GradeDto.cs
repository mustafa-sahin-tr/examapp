namespace ExamApp.Api.Models.Dtos;

/// <summary>
/// `GET api/worksheet/grades` yanıt şekli (issue #249). Audit alanları (CreateUserId vb.) dışarı sızmasın diye
/// ham <see cref="Data.Grade"/> entity'si yerine yalnız id/ad döner.
/// </summary>
public class GradeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

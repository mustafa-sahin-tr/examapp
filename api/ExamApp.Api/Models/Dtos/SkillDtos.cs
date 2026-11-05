using System.ComponentModel.DataAnnotations;
using ExamApp.Api.Data;

namespace ExamApp.Api.Models.Dtos;

/// <summary>Issue #703: etiketlenebilir TYMM becerisi. Ekran adı koddan çevirir; <see cref="Name"/> kılavuzdaki Türkçe addır.</summary>
public class SkillDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public SkillCategory Category { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>Issue #703: sorunun bağlam temelli bayrağı ve beceri etiketleri.</summary>
public class QuestionContextTagsDto
{
    public bool IsContextBased { get; set; }
    public List<SkillDto> Skills { get; set; } = new();
}

/// <summary>PUT api/questions/{id}/context-tags: bayrağı ve becerileri verilen değerlerle değiştirir.</summary>
public class SetQuestionContextTagsDto
{
    public const int MaxSkills = 10;

    public bool IsContextBased { get; set; }

    [Required, MaxLength(MaxSkills)]
    public List<int> SkillIds { get; set; } = new();
}

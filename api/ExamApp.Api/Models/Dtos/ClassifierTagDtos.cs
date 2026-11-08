namespace ExamApp.Api.Models.Dtos;

/// <summary>
/// Issue #1047: the closed lists the classifier (BadgeService) may pick a question's learning outcomes and skills from.
/// A list is empty when the question already has links of that kind (set by a human or an earlier run) or, for outcomes,
/// when its grade + subject are unknown or have no outcomes.
/// </summary>
public class ClassifierTagOptionsDto
{
    public List<ClassifierOutcomeOptionDto> LearningOutcomes { get; set; } = new();
    public List<ClassifierSkillOptionDto> Skills { get; set; } = new();
}

public class ClassifierOutcomeOptionDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class ClassifierSkillOptionDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>Issue #1047: the classifier's pick; every id must come from <see cref="ClassifierTagOptionsDto"/>.</summary>
public class SetClassifierTagsDto
{
    public List<int> LearningOutcomeIds { get; set; } = new();
    public List<int> SkillIds { get; set; } = new();

    /// <summary>Sınıflandırıcının istem sürümü (<c>Question.ClassifierRevision</c>); bağ eklenince yazılır.</summary>
    public int? ClassifierRevision { get; set; }
}

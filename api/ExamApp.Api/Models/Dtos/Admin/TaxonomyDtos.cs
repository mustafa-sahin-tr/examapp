using System;
using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos.Admin;

// ---- Read: full tree ----

public class TaxonomyTreeDto
{
    public List<TaxonomySubjectDto> Subjects { get; set; } = new();
    public List<TaxonomyGradeDto> Grades { get; set; } = new();
}

public class TaxonomyGradeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class TaxonomySubjectDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<TaxonomyTopicDto> Topics { get; set; } = new();
}

public class TaxonomyTopicDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SubjectId { get; set; }
    public int GradeId { get; set; }
    public string? GradeName { get; set; }
    public List<TaxonomySubTopicDto> SubTopics { get; set; } = new();
}

public class TaxonomySubTopicDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TopicId { get; set; }
    public int QuestionCount { get; set; }
}

// ---- Write ----

public class UpsertSubjectDto
{
    public string Name { get; set; } = string.Empty;
}

public class UpsertTopicDto
{
    public string Name { get; set; } = string.Empty;
    public int SubjectId { get; set; }
    public int GradeId { get; set; }
}

public class UpsertSubTopicDto
{
    public string Name { get; set; } = string.Empty;
    public int TopicId { get; set; }
}

// ---- Classifier cache ----

public class ClassifierCacheStatusDto
{
    public string? CachedContentName { get; set; }
    public string? Model { get; set; }
    public DateTime? RefreshedAt { get; set; }
    public int SubTopicCount { get; set; }
    public bool ConfiguredInSettings { get; set; }

    /// <summary>True when the current taxonomy no longer matches what the cache was built from.</summary>
    public bool Stale { get; set; }
}

public class ClassifierCacheRefreshResultDto : ResponseBaseDto
{
    public string? CachedContentName { get; set; }
    public int SubTopicCount { get; set; }
    public DateTime RefreshedAt { get; set; }
}

using AssistantService.Help;

namespace AssistantService.Tools;

public static class ToolRegistration
{
    /// <summary>Issue #503: Phase 1a (students) tool set.</summary>
    public static IServiceCollection AddAssistantTools(this IServiceCollection services)
    {
        services.AddScoped<IAssistantTool, GetMyProfileTool>();
        services.AddScoped<IAssistantTool, SearchTaxonomyTool>();
        services.AddScoped<IAssistantTool, GetMyWeakTopicsTool>();
        services.AddScoped<IAssistantTool, GetUntouchedTopicsTool>(); // #530
        services.AddScoped<IAssistantTool, GetMyPerformanceSummaryTool>();
        services.AddScoped<IAssistantTool, GetDailySetStatusTool>();
        services.AddScoped<IAssistantTool, ListMyCompletedTestsTool>();
        services.AddScoped<IAssistantTool, GetTestResultTool>();
        services.AddScoped<IAssistantTool, StartDailyPracticeTool>();
        services.AddScoped<IAssistantTool, StartPracticeSessionTool>();
        // #510 (Phase 2): explanations of answered questions and recent mistakes.
        services.AddScoped<IAssistantTool, ExplainQuestionTool>();
        services.AddScoped<IAssistantTool, GetMyRecentMistakesTool>();
        // #526: hard questions and a practice session on them.
        services.AddScoped<IAssistantTool, GetHardQuestionExamplesTool>();
        services.AddScoped<IAssistantTool, StartHardQuestionPracticeTool>();
        services.AddScoped<IAssistantTool, GetRepeatedTrapAnswersTool>(); // #528
        services.AddScoped<IAssistantTool, GetTimeStruggleQuestionsTool>(); // #527
        services.AddScoped<IAssistantTool, CompareMyTopicsWithPeersTool>(); // #533
        // #1336 (Phase 1b): parent read tools.
        services.AddScoped<IAssistantTool, ListMyChildrenTool>();
        services.AddScoped<IAssistantTool, GetChildWeeklySummaryTool>();
        services.AddScoped<IAssistantTool, ListChildAssignmentsTool>();
        services.AddScoped<IAssistantTool, GetChildTestResultTool>();
        services.AddScoped<IAssistantTool, GetChildWeakTopicsTool>();
        services.AddScoped<IAssistantTool, GetChildProgressTool>();
        services.AddScoped<IAssistantTool, GetChildScheduleTool>();
        // #511 (Phase 2): activity trend, children side by side, weekly report.
        services.AddScoped<IAssistantTool, GetChildActivityTrendTool>();
        services.AddScoped<IAssistantTool, CompareChildrenTool>();
        services.AddScoped<IAssistantTool, GetChildWeeklyReportTool>();
        // #511 (Phase 2): lesson requests (decided on the dashboard) and tutor search.
        services.AddScoped<IAssistantTool, ListPendingLessonRequestsTool>();
        services.AddScoped<IAssistantTool, SearchTutorsForChildTool>();
        // #536: the child's hard questions (#526 mine, no solve button).
        services.AddScoped<IAssistantTool, GetChildHardQuestionsTool>();
        services.AddScoped<IAssistantTool, SearchHelpTool>();
        services.AddScoped<IAssistantTool, UiOpenScreenTool>();
        services.AddScoped<IAssistantTool, UiSuggestActionsTool>();
        // #506: built eagerly, so a malformed guide article fails startup.
        services.AddSingleton(HelpIndex.LoadEmbedded());
        services.AddScoped<ToolRegistry>();
        services.AddScoped<ToolExecutor>();
        return services;
    }
}

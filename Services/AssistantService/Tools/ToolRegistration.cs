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
        services.AddScoped<IAssistantTool, GetMyPerformanceSummaryTool>();
        services.AddScoped<IAssistantTool, GetDailySetStatusTool>();
        services.AddScoped<IAssistantTool, ListMyCompletedTestsTool>();
        services.AddScoped<IAssistantTool, GetTestResultTool>();
        services.AddScoped<IAssistantTool, StartDailyPracticeTool>();
        services.AddScoped<IAssistantTool, StartPracticeSessionTool>();
        // #1336 (Phase 1b): parent read tools.
        services.AddScoped<IAssistantTool, ListMyChildrenTool>();
        services.AddScoped<IAssistantTool, GetChildWeeklySummaryTool>();
        services.AddScoped<IAssistantTool, ListChildAssignmentsTool>();
        services.AddScoped<IAssistantTool, GetChildTestResultTool>();
        services.AddScoped<IAssistantTool, GetChildWeakTopicsTool>();
        services.AddScoped<IAssistantTool, GetChildProgressTool>();
        services.AddScoped<IAssistantTool, GetChildScheduleTool>();
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

namespace BadgeService.Services;

/// <summary>
/// Picks the <see cref="IQuestionClassifier"/> from <c>QuestionAnalyzer:Provider</c> (issue #469):
/// <c>Gemini</c> (default, also when unset) or <c>Claude</c>. Any other value fails at startup.
/// </summary>
public static class QuestionClassifierRegistration
{
    public const string ProviderKey = "QuestionAnalyzer:Provider";
    public const string Gemini = "Gemini";
    public const string Claude = "Claude";

    public static IServiceCollection AddQuestionClassifier(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration[ProviderKey];

        if (string.IsNullOrWhiteSpace(provider) || provider.Equals(Gemini, StringComparison.OrdinalIgnoreCase))
        {
            services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.SectionName));
            services.AddScoped<IQuestionClassifier, GeminiQuestionClassifier>();
            return services;
        }

        if (provider.Equals(Claude, StringComparison.OrdinalIgnoreCase))
        {
            services.AddOptions<ClaudeClassifierOptions>()
                .BindConfiguration(ClaudeClassifierOptions.SectionName)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            // Singleton: holds the IChatClient (HTTP connection pool); its dependencies are all singletons.
            services.AddSingleton<IQuestionClassifier, ClaudeQuestionClassifier>();
            return services;
        }

        throw new InvalidOperationException(
            $"{ProviderKey} must be '{Gemini}' or '{Claude}' (was '{provider}').");
    }
}

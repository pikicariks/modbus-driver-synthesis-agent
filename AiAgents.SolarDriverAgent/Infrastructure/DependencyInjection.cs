using AiAgents.SolarDriverAgent.Application.Contracts;
using AiAgents.SolarDriverAgent.Application.Runner;
using AiAgents.SolarDriverAgent.Domain.Repositories;
using AiAgents.SolarDriverAgent.Infrastructure.External;
using AiAgents.SolarDriverAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;

namespace AiAgents.SolarDriverAgent.Infrastructure;

/// <summary>
/// Ekstenzije za registraciju servisa iz Infrastructure sloja.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registruje sve Infrastructure servise.
    /// </summary>
    public static IServiceCollection AddSolarDriverInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrEmpty(connectionString))
            {
                var dbPath = Path.Combine(AppContext.BaseDirectory, "SolarDriverAgent.db");
                options.UseSqlite($"Data Source={dbPath}");
            }
            else
            {
                options.UseSqlServer(connectionString);
            }
        });

        services.AddScoped<IProtocolTaskRepository, ProtocolTaskRepository>();
        services.AddScoped<ISimulationLogRepository, SimulationLogRepository>();

        services.Configure<LlmClientOptions>(
            configuration.GetSection(LlmClientOptions.SectionName));

        services.AddHttpClient<IAgentSynthesisClient, AgentSynthesisClient>()
            .ConfigureHttpClient((sp, client) =>
            {
                var config = configuration.GetSection(LlmClientOptions.SectionName);
                var timeout = config.GetValue("TimeoutSeconds", 300);
                client.Timeout = TimeSpan.FromSeconds(timeout);
            })
            .AddPolicyHandler(GetRetryPolicy(configuration))
            .AddPolicyHandler(GetCircuitBreakerPolicy(configuration));

        services.AddHttpClient<IAgentServiceHealthCheck, AgentServiceHealthCheck>()
            .AddPolicyHandler(GetHealthRetryPolicy(configuration));

        services.AddSingleton<IPdfTextExtractor, PdfTextExtractor>();

        services.AddSingleton<IPdfValidator, PdfValidator>();

        services.AddScoped<DriverSynthesisRunner>();

        return services;
    }

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(IConfiguration configuration)
    {
        var retries = configuration.GetValue("Agent:RetryCount", 3);
        var baseDelayMs = configuration.GetValue("Agent:RetryBaseDelayMs", 500);

        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => (int)msg.StatusCode == 429) // Too Many Requests
            .WaitAndRetryAsync(
                retryCount: retries,
                sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(baseDelayMs * Math.Pow(2, attempt - 1)),
                onRetry: (outcome, timespan, attempt, ctx) => { });
    }

    private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy(IConfiguration configuration)
    {
        var failures = configuration.GetValue("Agent:CircuitBreakerFailures", 3);
        var durationSeconds = configuration.GetValue("Agent:CircuitBreakerDurationSeconds", 15);

        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: failures,
                durationOfBreak: TimeSpan.FromSeconds(durationSeconds));
    }

    private static IAsyncPolicy<HttpResponseMessage> GetHealthRetryPolicy(IConfiguration configuration)
    {
        var retries = configuration.GetValue("Agent:HealthRetryCount", 2);
        var delayMs = configuration.GetValue("Agent:HealthRetryDelayMs", 200);

        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(
                retries,
                _ => TimeSpan.FromMilliseconds(delayMs));
    }
}

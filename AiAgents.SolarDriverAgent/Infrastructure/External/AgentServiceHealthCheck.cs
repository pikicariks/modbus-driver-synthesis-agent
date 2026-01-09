using System.Diagnostics;
using AiAgents.SolarDriverAgent.Application.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiAgents.SolarDriverAgent.Infrastructure.External;

/// <summary>
/// Health check implementacija za Python agent servis.
/// Koristi /health endpoint.
/// </summary>
public class AgentServiceHealthCheck : IAgentServiceHealthCheck
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AgentServiceHealthCheck> _logger;
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

    // Cache za health status (da ne spamamo health endpoint)
    private HealthCheckResult? _cachedResult;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(10);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public AgentServiceHealthCheck(
        HttpClient httpClient,
        IOptions<LlmClientOptions> options,
        ILogger<AgentServiceHealthCheck> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var opts = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _httpClient.BaseAddress = new Uri(opts.BaseUrl);
        _httpClient.Timeout = _timeout;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        // Provjeri cache
        if (_cachedResult != null && DateTime.UtcNow < _cacheExpiry)
        {
            return _cachedResult;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Double-check nakon lock-a
            if (_cachedResult != null && DateTime.UtcNow < _cacheExpiry)
            {
                return _cachedResult;
            }

            var result = await PerformHealthCheckAsync(cancellationToken);

            // Cache rezultat
            _cachedResult = result;
            _cacheExpiry = DateTime.UtcNow.Add(_cacheDuration);

            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<HealthCheckResult> PerformHealthCheckAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogDebug("Checking Python agent service health");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout);

            var response = await _httpClient.GetAsync("/health", cts.Token);

            stopwatch.Stop();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Python agent service is healthy (response time: {ResponseTime}ms)",
                    stopwatch.ElapsedMilliseconds);

                return HealthCheckResult.Healthy(stopwatch.ElapsedMilliseconds);
            }
            else
            {
                var error = $"Health check returned {response.StatusCode}";
                _logger.LogWarning("Python agent service unhealthy: {Error}", error);

                return HealthCheckResult.Unhealthy(error, stopwatch.ElapsedMilliseconds);
            }
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            var error = $"Health check timed out after {_timeout.TotalSeconds}s";
            _logger.LogWarning("Python agent service health check timeout");

            return HealthCheckResult.Unhealthy(error, stopwatch.ElapsedMilliseconds);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            var error = $"Connection failed: {ex.Message}";
            _logger.LogWarning("Python agent service not reachable: {Error}", error);

            return HealthCheckResult.Unhealthy(error, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Unexpected error during health check");

            return HealthCheckResult.Unhealthy(ex.Message, stopwatch.ElapsedMilliseconds);
        }
    }
}

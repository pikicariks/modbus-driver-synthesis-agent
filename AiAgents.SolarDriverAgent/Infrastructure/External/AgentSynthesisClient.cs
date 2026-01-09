using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiAgents.SolarDriverAgent.Application.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiAgents.SolarDriverAgent.Infrastructure.External;

/// <summary>
/// HTTP klijent za komunikaciju sa Python multi-agent servisom.
/// Poziva /api/v1/synthesize-driver endpoint.
/// </summary>
public class AgentSynthesisClient : IAgentSynthesisClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AgentSynthesisClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public AgentSynthesisClient(
        HttpClient httpClient,
        IOptions<LlmClientOptions> options,
        ILogger<AgentSynthesisClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var opts = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _httpClient.BaseAddress = new Uri(opts.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);

        if (!string.IsNullOrEmpty(opts.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", opts.ApiKey);
        }

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task<SynthesisResult> SynthesizeDriverAsync(
        SynthesisRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Sending synthesis request to Python agent service for device {DeviceName}",
            request.DeviceName);

        try
        {
            var payload = new SynthesizeDriverRequestDto
            {
                ProtocolText = request.ProtocolText,
                PreviousExperience = request.PreviousExperience,
                TargetLanguage = request.TargetLanguage,
                DeviceName = request.DeviceName
            };

            var response = await _httpClient.PostAsJsonAsync(
                "/api/v1/synthesize-driver",
                payload,
                _jsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Agent synthesis failed with status {Status}: {Error}",
                    response.StatusCode, error);

                return new SynthesisResult
                {
                    Success = false,
                    ErrorMessage = $"HTTP {response.StatusCode}: {error}"
                };
            }

            var result = await response.Content.ReadFromJsonAsync<SynthesizeDriverResponseDto>(
                _jsonOptions, cancellationToken);

            if (result == null)
            {
                return new SynthesisResult
                {
                    Success = false,
                    ErrorMessage = "Empty response from agent service"
                };
            }

            _logger.LogInformation(
                "Synthesis completed: Success={Success}, Confidence={Confidence}, Attempts={Attempts}",
                result.Success, result.ConfidenceScore, result.TotalInternalAttempts);

            return MapToSynthesisResult(result);
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling agent synthesis service");
            return new SynthesisResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private static SynthesisResult MapToSynthesisResult(SynthesizeDriverResponseDto dto)
    {
        var attempts = dto.InternalAttempts?.Select(a => new InternalAttemptLog
        {
            AttemptNumber = a.AttemptNumber,
            AgentName = a.AgentName ?? string.Empty,
            Action = a.Action ?? string.Empty,
            Success = a.Success,
            ErrorMessage = a.ErrorMessage,
            DurationMs = a.DurationMs,
            Timestamp = a.Timestamp ?? DateTime.UtcNow
        }).ToList() ?? new List<InternalAttemptLog>();

        var registers = dto.ExtractedRegisters?.Select(r => new ExtractedRegister
        {
            Address = r.Address,
            AddressHex = r.AddressHex ?? $"0x{r.Address:X4}",
            Name = r.Name ?? string.Empty,
            DataType = r.DataType ?? "uint16",
            FunctionCode = r.FunctionCode
        }).ToList() ?? new List<ExtractedRegister>();

        ModbusTestResult? testResult = null;
        if (dto.TestResult != null)
        {
            testResult = new ModbusTestResult
            {
                Success = dto.TestResult.Success,
                TestedRegisters = dto.TestResult.TestedRegisters?.AsReadOnly() ?? (IReadOnlyList<string>)Array.Empty<string>(),
                ExpectedBytes = dto.TestResult.ExpectedBytes,
                ActualBytes = dto.TestResult.ActualBytes,
                ErrorMessage = dto.TestResult.ErrorMessage,
                ErrorBytePosition = dto.TestResult.ErrorBytePosition
            };
        }

        return new SynthesisResult
        {
            Success = dto.Success,
            DriverCode = dto.DriverCode,
            TargetLanguage = dto.TargetLanguage ?? "python",
            ConfidenceScore = dto.ConfidenceScore,
            TotalInternalAttempts = dto.TotalInternalAttempts,
            InternalAttempts = attempts,
            TestResult = testResult,
            ExtractedRegisters = registers,
            ErrorMessage = dto.ErrorMessage,
            ExperienceId = dto.ExperienceId
        };
    }

    #region DTOs for JSON serialization

    private record SynthesizeDriverRequestDto
    {
        public string ProtocolText { get; init; } = string.Empty;
        public string? PreviousExperience { get; init; }
        public string TargetLanguage { get; init; } = "python";
        public string? DeviceName { get; init; }
    }

    private record SynthesizeDriverResponseDto
    {
        public bool Success { get; init; }
        public string? DriverCode { get; init; }
        public string? TargetLanguage { get; init; }
        public double ConfidenceScore { get; init; }
        public int TotalInternalAttempts { get; init; }
        public List<AttemptLogDto>? InternalAttempts { get; init; }
        public TestResultDto? TestResult { get; init; }
        public List<RegisterDto>? ExtractedRegisters { get; init; }
        public string? ErrorMessage { get; init; }
        public string? ExperienceId { get; init; }
    }

    private record AttemptLogDto
    {
        public int AttemptNumber { get; init; }
        public string? AgentName { get; init; }
        public string? Action { get; init; }
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        public int DurationMs { get; init; }
        public DateTime? Timestamp { get; init; }
    }

    private record TestResultDto
    {
        public bool Success { get; init; }
        public List<string>? TestedRegisters { get; init; }
        public string? ExpectedBytes { get; init; }
        public string? ActualBytes { get; init; }
        public string? ErrorMessage { get; init; }
        public int? ErrorBytePosition { get; init; }
    }

    private record RegisterDto
    {
        public int Address { get; init; }
        public string? AddressHex { get; init; }
        public string? Name { get; init; }
        public string? DataType { get; init; }
        public int FunctionCode { get; init; }
    }

    #endregion
}

using AiAgents.SolarDriverAgent.Application.Contracts;
using AiAgents.SolarDriverAgent.Application.Runner;
using AiAgents.SolarDriverAgent.Application.Types;
using AiAgents.SolarDriverAgent.Domain.Repositories;
using AiAgents.SolarDriverAgent.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace AiAgents.SolarDriverAgent.Web.BackgroundServices;

/// <summary>
/// BackgroundService koji periodično pokreće agentske tikove.
/// Emituje rezultate putem SignalR za real-time dashboard.
/// </summary>
public class AgentHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<AgentHub, IAgentHubClient> _hubContext;
    private readonly ILogger<AgentHostedService> _logger;
    private readonly TimeSpan _tickInterval;
    private readonly TimeSpan _idleInterval;

    public AgentHostedService(
        IServiceScopeFactory scopeFactory,
        IHubContext<AgentHub, IAgentHubClient> hubContext,
        ILogger<AgentHostedService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _tickInterval = TimeSpan.FromSeconds(
            configuration.GetValue("Agent:TickIntervalSeconds", 5));
        _idleInterval = TimeSpan.FromSeconds(
            configuration.GetValue("Agent:IdleIntervalSeconds", 30));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Agent Hosted Service started. Tick: {Tick}s, Idle: {Idle}s",
            _tickInterval.TotalSeconds, _idleInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan nextDelay;

            try
            {
                Console.WriteLine($"[AGENT TICK] Checking for work at {DateTime.UtcNow:HH:mm:ss}...");
                
                using var scope = _scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<DriverSynthesisRunner>();
                var taskRepo = scope.ServiceProvider.GetRequiredService<IProtocolTaskRepository>();
                var healthCheck = scope.ServiceProvider.GetRequiredService<IAgentServiceHealthCheck>();
                var health = await healthCheck.CheckHealthAsync(stoppingToken);

                var hasPendingWork = await runner.HasPendingWorkAsync(stoppingToken);
                Console.WriteLine($"[AGENT TICK] HasPendingWork = {hasPendingWork}");

                if (!hasPendingWork)
                {
                    var pendingCount = await taskRepo.GetTotalCountAsync(
                        Domain.Enums.ProtocolTaskStatus.Queued, stoppingToken);

                    await _hubContext.Clients.All.AgentStatusChanged(new AgentStatusNotification
                    {
                        Status = "Idle",
                        PendingTasks = pendingCount,
                        PythonServiceHealthy = health.IsHealthy
                    });

                    nextDelay = _idleInterval;
                }
                else
                {
                    var nextTask = await taskRepo.GetNextEligibleTaskAsync(stoppingToken);
                    var taskId = nextTask?.Id ?? Guid.Empty;
                    var deviceName = nextTask?.DeviceName ?? "Unknown";
                    var attemptNumber = (nextTask?.AttemptCount ?? 0) + 1;
                    
                    await _hubContext.Clients.All.AgentStatusChanged(new AgentStatusNotification
                    {
                        Status = "Working",
                        PendingTasks = 0,
                        PythonServiceHealthy = health.IsHealthy
                    });

                    await EmitProcessingLog(taskId, deviceName, "SENSE", "Started", 
                        "Čitanje PDF dokumenta i ekstrakcija teksta...", attemptNumber);
                    
                    await EmitProcessingLog(taskId, deviceName, "THINK", "Started",
                        "Priprema zahtjeva za Python agent servis...", attemptNumber);
                    
                    await EmitProcessingLog(taskId, deviceName, "PYTHON_PARSER", "InProgress",
                        "📋 Parser analizira Modbus specifikaciju...", attemptNumber);
                    
                    await EmitProcessingLog(taskId, deviceName, "PYTHON_CODER", "InProgress",
                        "💻 Coder generiše driver kod...", attemptNumber);
                    
                    await EmitProcessingLog(taskId, deviceName, "PYTHON_TESTER", "InProgress",
                        "🧪 Tester validira kod protiv Modbus simulatora...", attemptNumber);

                    var tickResult = await runner.StepAsync(stoppingToken);

                    if (tickResult.DidWork && tickResult.Data != null)
                    {
                        _logger.LogInformation(
                            "Tick completed. AgentLogs count: {Count}, ErrorMessage: {Error}",
                            tickResult.Data.AgentLogs.Count,
                            tickResult.Data.ErrorMessage ?? "null");
                        
                        foreach (var agentLog in tickResult.Data.AgentLogs)
                        {
                            _logger.LogDebug(
                                "Emitting agent log: {Agent} - {Action} - Success: {Success} - Error: {Error}",
                                agentLog.AgentName, agentLog.Action, agentLog.Success, agentLog.ErrorMessage ?? "null");
                            
                            await EmitProcessingLog(taskId, deviceName, 
                                $"PYTHON_{agentLog.AgentName.ToUpper()}", 
                                agentLog.Success ? "Completed" : "Failed",
                                agentLog.Action,
                                agentLog.AttemptNumber,
                                agentLog.DurationMs,
                                agentLog.ErrorMessage);
                            
                            if (!string.IsNullOrEmpty(agentLog.ErrorMessage) &&
                                (agentLog.ErrorMessage.Contains("ILLEGAL", StringComparison.OrdinalIgnoreCase) ||
                                 agentLog.ErrorMessage.Contains("does not exist", StringComparison.OrdinalIgnoreCase)))
                            {
                                _logger.LogWarning("ADDRESS_ERROR detected in agent log: {Error}", agentLog.ErrorMessage);
                                
                                await EmitProcessingLog(taskId, deviceName, "ADDRESS_ERROR", "Failed",
                                    $"🚫 ILLEGAL DATA ADDRESS: Nevalidna adresa registra!",
                                    agentLog.AttemptNumber,
                                    null,
                                    agentLog.ErrorMessage.Length > 300 ? agentLog.ErrorMessage[..300] + "..." : agentLog.ErrorMessage);
                            }
                        }
                        
                        var errorMsg = tickResult.Data.ErrorMessage ?? "";
                        _logger.LogDebug("Checking main ErrorMessage for ILLEGAL: '{Error}'", errorMsg);
                        
                        if (!string.IsNullOrEmpty(errorMsg) &&
                            (errorMsg.Contains("ILLEGAL", StringComparison.OrdinalIgnoreCase) ||
                             errorMsg.Contains("does not exist", StringComparison.OrdinalIgnoreCase)))
                        {
                            _logger.LogWarning("ADDRESS_ERROR detected in main ErrorMessage: {Error}", errorMsg);
                            
                            await EmitProcessingLog(taskId, deviceName, "ADDRESS_ERROR", "Failed",
                                $"🚫 ILLEGAL DATA ADDRESS: Drajver pokušao pristupiti nepostojećem registru!",
                                attemptNumber,
                                null,
                                errorMsg.Length > 300 ? errorMsg[..300] + "..." : errorMsg);
                        }
                        
                        await EmitProcessingLog(taskId, deviceName, "LEARN", 
                            tickResult.HasError ? "Failed" : "Completed",
                            tickResult.HasError 
                                ? $"Greška snimljena - agent će pokušati ponovo sa ispravnim adresama"
                                : $"Uspješno! Confidence: {tickResult.Data.ConfidenceScore:P0}",
                            attemptNumber,
                            tickResult.DurationMs,
                            tickResult.HasError ? tickResult.Data.ErrorMessage : null);
                        
                        if (!string.IsNullOrEmpty(tickResult.Data.ExperienceId))
                        {
                            await EmitProcessingLog(taskId, deviceName, "EXPERIENCE_STORE", 
                                "Completed",
                                $"📚 Iskustvo sačuvano u ChromaDB: {tickResult.Data.ExperienceId}",
                                attemptNumber,
                                null,
                                tickResult.HasError 
                                    ? $"Problem: {tickResult.Data.ErrorMessage}" 
                                    : $"Success - Registers: {string.Join(", ", tickResult.Data.TestedRegisters.Take(3))}");
                        }

                        var notification = CreateNotification(tickResult);
                        await _hubContext.Clients.All.TickCompleted(notification);

                        LogTickResult(tickResult);
                    }

                    nextDelay = _tickInterval;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in agent tick");

                await _hubContext.Clients.All.AgentStatusChanged(new AgentStatusNotification
                {
                    Status = "Error",
                    PythonServiceHealthy = false
                });

                nextDelay = _idleInterval;
            }

            await Task.Delay(nextDelay, stoppingToken);
        }

        _logger.LogInformation("Agent Hosted Service stopped");
    }

    /// <summary>
    /// Kreira SignalR notifikaciju iz TickResult.
    /// </summary>
    private static TickNotification CreateNotification(
        AiAgents.Core.Abstractions.TickResult<SynthesisTickData> result)
    {
        var data = result.Data!;

        return new TickNotification
        {
            EventType = result.HasError ? "TickFailed" : "TickSuccess",
            TaskId = data.TaskId,
            DeviceName = data.DeviceName,
            Status = data.Status,
            Success = data.SynthesisSuccess,
            Message = result.Message,
            ErrorMessage = data.ErrorMessage,
            InternalAttempts = data.InternalAttempts,
            ConfidenceScore = data.ConfidenceScore,
            TotalAttempts = data.TotalAttempts,
            MaxAttempts = data.MaxAttempts,
            DurationMs = result.DurationMs,
            RegisterCount = data.TestedRegisters.Count,
            TestedRegisters = data.TestedRegisters,
            ExperienceId = data.ExperienceId,
            ProblematicBytes = data.ProblematicBytes,
            BytePosition = data.BytePosition,
            AgentLogs = data.AgentLogs.Select(l => new AgentLogEntry
            {
                AttemptNumber = l.AttemptNumber,
                AgentName = l.AgentName,
                Action = l.Action,
                Success = l.Success,
                ErrorMessage = l.ErrorMessage,
                DurationMs = l.DurationMs
            }).ToList()
        };
    }

    private void LogTickResult(AiAgents.Core.Abstractions.TickResult<SynthesisTickData> result)
    {
        var data = result.Data!;

        if (result.HasError)
        {
            _logger.LogWarning(
                "Tick FAILED: {DeviceName} | Attempts: {Attempts}/{Max} | Internal: {Internal} | {Duration}ms | Error: {Error}",
                data.DeviceName, data.TotalAttempts, data.MaxAttempts,
                data.InternalAttempts, result.DurationMs, data.ErrorMessage);
        }
        else
        {
            _logger.LogInformation(
                "Tick SUCCESS: {DeviceName} | Confidence: {Confidence:P0} | Registers: {Registers} | Internal: {Internal} | {Duration}ms",
                data.DeviceName, data.ConfidenceScore,
                data.TestedRegisters.Count, data.InternalAttempts, result.DurationMs);
        }
    }
    
    /// <summary>
    /// Emituje real-time processing log putem SignalR.
    /// </summary>
    private async Task EmitProcessingLog(
        Guid taskId, 
        string deviceName, 
        string phase, 
        string status, 
        string? message,
        int? attemptNumber = null,
        long? durationMs = null,
        string? details = null)
    {
        var entry = new ProcessingLogEntry
        {
            TaskId = taskId,
            DeviceName = deviceName,
            Phase = phase,
            Status = status,
            Message = message,
            AttemptNumber = attemptNumber,
            DurationMs = durationMs,
            Details = details
        };
        
        try
        {
            Console.WriteLine($"[SignalR] Emitting: {phase} - {status} - {message}");
            
            await _hubContext.Clients.All.ProcessingLog(entry);
            
            _logger.LogDebug(
                "Emitted processing log: {Phase} - {Status} - {Message}",
                phase, status, message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit processing log for phase {Phase}", phase);
        }
    }
}

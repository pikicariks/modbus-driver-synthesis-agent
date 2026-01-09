using AiAgents.SolarDriverAgent.Infrastructure;
using AiAgents.SolarDriverAgent.Infrastructure.Persistence;
using AiAgents.SolarDriverAgent.Web.BackgroundServices;
using AiAgents.SolarDriverAgent.Web.Components;
using AiAgents.SolarDriverAgent.Web.Hubs;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add Blazor Server
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add Controllers + Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Solar Driver Agent API",
        Version = "v1",
        Description = "Autonomni Multi-Agent Sistem za generisanje Modbus drajvera iz PDF dokumentacije"
    });
});

// Add SignalR
builder.Services.AddSignalR();

// Add HttpClient za Blazor komponente (za pozivanje lokalnog API-ja)
builder.Services.AddHttpClient();

// Infrastructure services (DB, repos, clients, runner)
builder.Services.AddSolarDriverInfrastructure(builder.Configuration);

// Background Service
builder.Services.AddHostedService<AgentHostedService>();

var app = builder.Build();

// Kreiraj/migriraj bazu na startu i popravi zaglavljene taskove
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.EnsureCreated();
    
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Database initialized at: {Path}", dbContext.Database.GetDbConnection().DataSource);
    
    // Popravi taskove koji su ostali zaglavljeni u "Processing" statusu
    // (ovo se dešava ako se servis restartuje dok je task u obradi)
    var stuckTasks = dbContext.ProtocolTasks
        .Where(t => t.Status == AiAgents.SolarDriverAgent.Domain.Enums.ProtocolTaskStatus.Processing)
        .ToList();
    
    if (stuckTasks.Any())
    {
        logger.LogWarning("Found {Count} tasks stuck in Processing status. Reverting to appropriate state.", stuckTasks.Count);
        
        foreach (var task in stuckTasks)
        {
            if (task.AttemptCount >= task.MaxAttempts)
            {
                // Iscrpljeni svi pokušaji - označi kao Failed
                task.ForceFailedStatus();
                logger.LogInformation("Task {TaskId} ({DeviceName}) marked as Failed after {Attempts} attempts.", 
                    task.Id, task.DeviceName, task.AttemptCount);
            }
            else
            {
                // Ima još pokušaja - vrati u Queued
                task.RevertToQueued();
                logger.LogInformation("Task {TaskId} ({DeviceName}) reverted to Queued.", task.Id, task.DeviceName);
            }
        }
        
        dbContext.SaveChanges();
        logger.LogInformation("Recovered {Count} stuck tasks.", stuckTasks.Count);
    }
}

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Solar Driver Agent API v1");
    });
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.UseAuthorization();

// Map controllers
app.MapControllers();

// Map SignalR hub
app.MapHub<AgentHub>("/hubs/agent");

// Map Blazor
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

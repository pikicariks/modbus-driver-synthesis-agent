using AiAgents.SolarDriverAgent.Infrastructure;
using AiAgents.SolarDriverAgent.Infrastructure.Persistence;
using AiAgents.SolarDriverAgent.Web.BackgroundServices;
using AiAgents.SolarDriverAgent.Web.Components;
using AiAgents.SolarDriverAgent.Web.Hubs;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

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

builder.Services.AddSignalR();

builder.Services.AddHttpClient();

builder.Services.AddSolarDriverInfrastructure(builder.Configuration);

builder.Services.AddHostedService<AgentHostedService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.EnsureCreated();
    
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Database initialized at: {Path}", dbContext.Database.GetDbConnection().DataSource);
    
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
                task.ForceFailedStatus();
                logger.LogInformation("Task {TaskId} ({DeviceName}) marked as Failed after {Attempts} attempts.", 
                    task.Id, task.DeviceName, task.AttemptCount);
            }
            else
            {
                task.RevertToQueued();
                logger.LogInformation("Task {TaskId} ({DeviceName}) reverted to Queued.", task.Id, task.DeviceName);
            }
        }
        
        dbContext.SaveChanges();
        logger.LogInformation("Recovered {Count} stuck tasks.", stuckTasks.Count);
    }
}

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

app.MapControllers();

app.MapHub<AgentHub>("/hubs/agent");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

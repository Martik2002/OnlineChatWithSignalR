using Microsoft.EntityFrameworkCore;
using OnlineChatWithSignalR.Data;
using OnlineChatWithSignalR.Hubs;
using OnlineChatWithSignalR.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddChatServices(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapGroup("/api/auth")
    .WithTags("Auth")
    .MapIdentityApi<ChatUser>();

app.MapHub<ChatHub>(ChatHub.Path);

app.MapHealthChecks("/health");

// Convenient for local development and tests. In production, migrations should be
// applied as a separate deployment step, so several instances don't race to migrate.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();

// Makes Program visible to WebApplicationFactory in the integration tests
public partial class Program { }

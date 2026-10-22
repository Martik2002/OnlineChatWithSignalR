using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OnlineChatWithSignalR.BackgroundServises;
using OnlineChatWithSignalR.Chat;
using OnlineChatWithSignalR.Data;
using OnlineChatWithSignalR.Hubs;

namespace OnlineChatWithSignalR.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddChatServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Chat");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'Chat' is missing. Set ConnectionStrings:Chat in configuration " +
                "or the ConnectionStrings__Chat environment variable.");
        }

        services.AddDbContext<ChatDbContext>(options => options.UseNpgsql(connectionString));

        services.AddIdentityApiEndpoints<ChatUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Lockout.MaxFailedAccessAttempts = 5;
            })
            .AddEntityFrameworkStores<ChatDbContext>();

        services.Configure<BearerTokenOptions>(IdentityConstants.BearerScheme, options =>
        {
            options.Events = new BearerTokenEvents
            {
                // Browsers can't send an Authorization header when opening a WebSocket,
                // so the SignalR client sends the token as ?access_token=... instead.
                // We only accept it from the query string for hub requests: tokens in URLs
                // can end up in logs, so regular API calls must use the header.
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(accessToken) &&
                        context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }

                    return Task.CompletedTask;
                }
            };
        });

        services.AddAuthorization();

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ChatService>();

        services.AddSignalR(options => options.AddFilter<ChatExceptionHubFilter>());
        services.AddHostedService<ServerTimeNotifier>();

        services.AddProblemDetails();
        services.AddOpenApi();

        services.AddHealthChecks()
            .AddDbContextCheck<ChatDbContext>();

        return services;
    }
}

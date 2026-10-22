using Client.Components;
using Client.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<ChatApiOptions>(builder.Configuration.GetSection(ChatApiOptions.Section));
builder.Services.AddHttpClient(AuthService.HttpClientName, (services, client) =>
{
    client.BaseAddress = new Uri(services.GetRequiredService<IOptions<ChatApiOptions>>().Value.BaseUrl);
});

// Scoped = one instance per browser tab (Blazor Server circuit)
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ChatConnection>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

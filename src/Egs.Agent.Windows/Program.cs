using Egs.Agent.Windows.Services;
using Egs.Agent.Windows.Services.Runtimes;
using Egs.Contracts.Cloud;
using Egs.PluginSdk;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddEgsPluginCatalog(builder.Configuration);
builder.Services.AddHttpClient<CloudControlClient>((services, client) =>
{
    var options = services.GetRequiredService<IOptions<CloudControlOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
});
builder.Services.Configure<CloudControlOptions>(
    builder.Configuration.GetSection("CloudControl"));

builder.Services.AddHttpClient<ControlPlaneClient>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Agent:ApiBaseUrl"] ?? "https://localhost:7110/");
});

builder.Services.AddHttpClient<SteamCmdService>();
builder.Services.AddSingleton<PluginSetupActionExecutor>();
builder.Services.AddSingleton<ServerCommandExecutor>();
builder.Services.AddSingleton<GameServerRuntimeCatalog>();
builder.Services.AddSingleton<IGameServerRuntime, ManifestGameServerRuntime>();
builder.Services.AddHostedService<AgentWorker>();

var host = builder.Build();
host.Run();

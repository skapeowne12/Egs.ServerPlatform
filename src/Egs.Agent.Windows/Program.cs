using Egs.Agent.Windows.Services;
using Egs.Agent.Windows.Services.Runtimes;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient<ControlPlaneClient>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Agent:ApiBaseUrl"] ?? "https://localhost:7110/");
});

builder.Services.AddHttpClient<SteamCmdService>();
builder.Services.AddSingleton<ServerCommandExecutor>();
builder.Services.AddSingleton<GameServerRuntimeCatalog>();
builder.Services.AddSingleton<IGameServerRuntime, ValheimRuntime>();
builder.Services.AddHostedService<AgentWorker>();

var host = builder.Build();
host.Run();

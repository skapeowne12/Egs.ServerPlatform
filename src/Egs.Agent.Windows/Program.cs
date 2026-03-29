using Egs.Agent.Windows.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient<ControlPlaneClient>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Agent:ApiBaseUrl"] ?? "https://localhost:7110/");
});

builder.Services.AddHostedService<AgentWorker>();

var host = builder.Build();
host.Run();
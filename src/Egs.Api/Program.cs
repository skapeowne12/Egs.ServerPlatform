using Egs.Api.Hubs;
using Egs.Api.Realtime;
using Egs.Application.Servers;
using Egs.Infrastructure.Data;
using Egs.Infrastructure.Servers;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();

var dataFolder = Path.Combine(builder.Environment.ContentRootPath, "data");
Directory.CreateDirectory(dataFolder);

var connectionString =
    builder.Configuration.GetConnectionString("Default")
    ?? $"Data Source={Path.Combine(dataFolder, "egs.db")}";

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddSingleton<IServerStatusNotifier, SignalRServerStatusNotifier>();
builder.Services.AddSingleton<IServerConsoleNotifier, SignalRServerConsoleNotifier>();
builder.Services.AddSingleton<IServerService, SqliteServerService>();

var app = builder.Build();

await using (var db = await app.Services
    .GetRequiredService<IDbContextFactory<AppDbContext>>()
    .CreateDbContextAsync())
{
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapControllers();
app.MapHub<ServerStatusHub>("/hubs/server-status");
app.MapHub<ServerConsoleHub>("/hubs/server-console");

app.Run();
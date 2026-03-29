using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Egs.Infrastructure.Data;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var current = Directory.GetCurrentDirectory();
        var currentName = new DirectoryInfo(current).Name;

        string apiProjectPath = currentName switch
        {
            "Egs.Api" => current,
            "Egs.Infrastructure" => Path.GetFullPath(Path.Combine(current, "..", "Egs.Api")),
            _ => Path.GetFullPath(Path.Combine(current, "src", "Egs.Api"))
        };

        var dataFolder = Path.Combine(apiProjectPath, "data");
        Directory.CreateDirectory(dataFolder);

        var dbPath = Path.Combine(dataFolder, "egs.db");

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlite($"Data Source={dbPath}");

        return new AppDbContext(optionsBuilder.Options);
    }
}
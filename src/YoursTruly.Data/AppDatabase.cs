using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace YoursTruly.Data;

/// <summary>How to open a list. Deliberately says nothing about where one lives:
/// there is no default path and no default file, because the app has no business
/// deciding where somebody's list of people is kept. The import screen asks.</summary>
public static class AppDatabase
{
    public static DbContextOptions<AppDbContext> Options(string path) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;

    public static AppDbContext Open(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        return new AppDbContext(Options(path));
    }

    /// <summary>Timestamped copy of a list, taken before every import, in a "backups"
    /// folder beside the list itself — where the user will find it, rather than
    /// somewhere only the app knows about.</summary>
    public static string BackUp(string path, DateTimeOffset now)
    {
        var folder = Path.Combine(Path.GetDirectoryName(path) ?? ".", "backups");
        Directory.CreateDirectory(folder);
        var stamp = now.ToLocalTime().ToString("yyyyMMdd-HHmmss");
        var name = Path.GetFileNameWithoutExtension(path);
        var destination = Path.Combine(folder, $"{name}-{stamp}.db");
        File.Copy(path, destination, overwrite: true);
        return destination;
    }
}

/// <summary>Lets `dotnet ef` build the context without an application host.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(AppDatabase.Options("design-time.db"));
}

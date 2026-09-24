using YoursTruly.Data;
using Microsoft.EntityFrameworkCore;

namespace YoursTruly.Tests;

public sealed class SchemaTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"yourstruly-schema-{Guid.NewGuid():N}.db");

    [Fact]
    public void The_migrations_match_the_model()
    {
        using var db = AppDatabase.Open(_path);
        Assert.False(
            db.Database.HasPendingModelChanges(),
            "The entities have changed since the last migration. Run: dotnet dotnet-ef migrations add <Name> -p src/YoursTruly.Data -s src/YoursTruly.Data");
    }

    [Fact]
    public void A_fresh_database_can_be_created_from_the_migrations()
    {
        using var db = AppDatabase.Open(_path);
        db.Database.Migrate();
        Assert.Empty(db.People);
        Assert.Empty(db.ImportRuns);
    }

    public void Dispose()
    {
        // Deliberately not ClearAllPools: it is process-wide, and clearing pools while
        // another test class still holds a connection makes unrelated tests fail.
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch (IOException) { /* a temp file left behind harms nothing */ }
    }
}

using System;
using System.IO;
using Xunit;

namespace AccountGo.ServicesTests;

/// <summary>
/// The developer's own SQLite database (src/Api/accountgo.db, not in git). Some upgrade tests run against it; on a
/// machine without one, such as CI, they are skipped.
/// </summary>
public static class LocalDevDatabase
{
    public static string? Path { get; } = Find();

    private static string? Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(System.IO.Path.Combine(dir.FullName, "accountgo.sln")))
            dir = dir.Parent;
        if (dir == null)
            return null;
        var path = System.IO.Path.Combine(dir.FullName, "src", "Api", "accountgo.db");
        return File.Exists(path) ? path : null;
    }
}

/// <summary>A fact that needs <see cref="LocalDevDatabase"/>; skipped when there is none.</summary>
public sealed class LocalDatabaseFactAttribute : FactAttribute
{
    public LocalDatabaseFactAttribute()
    {
        if (LocalDevDatabase.Path == null)
            Skip = "Needs a local src/Api/accountgo.db (not in git).";
    }
}

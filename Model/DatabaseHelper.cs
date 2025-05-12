using System;
using System.Data.SQLite;
using System.IO;

public static class DatabaseHelper
{
    private static readonly string dbPath;

    static DatabaseHelper()
    {
        // ✅ Now use the same folder where the .exe is located
        string appFolder = AppDomain.CurrentDomain.BaseDirectory;
        dbPath = Path.Combine(appFolder, "household_management.db");

        if (!File.Exists(dbPath))
        {
            // (Optional safety) You can show a debug message
            Console.WriteLine("⚠️ Warning: Database file not found. It should have been created at startup by DatabaseInitializer.");
            // Not creating DB here! DB creation happens ONLY inside DatabaseInitializer.
        }
    }

    public static SQLiteConnection GetConnection()
    {
        return new SQLiteConnection($"Data Source={dbPath};Version=3;");
    }

    public static string GetDbPath() => dbPath;
}

using System;
using System.Data.SQLite;
using System.IO;

public static class DatabaseHelper
{
    private static string dbPath;

    static DatabaseHelper()
    {
        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string targetFolder = Path.Combine(appDataPath, "HouseholdMS");
        dbPath = Path.Combine(targetFolder, "household_management.db");

        if (!Directory.Exists(targetFolder))
            Directory.CreateDirectory(targetFolder);

        if (!File.Exists(dbPath))
            File.Copy("household_management.db", dbPath); // should be in build output folder
    }

    public static SQLiteConnection GetConnection()
    {
        return new SQLiteConnection($"Data Source={dbPath};");
    }

    public static string GetDbPath() => dbPath;
}

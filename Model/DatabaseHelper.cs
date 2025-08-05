using System;
using System.Configuration;
using System.Data.SQLite;
using System.IO;

namespace HouseholdMS.Model
{
    public static class DatabaseHelper
    {
        private static readonly string connectionString;
        private static readonly string dbFilePath;

        static DatabaseHelper()
        {
            // Read connection string from config, else default to LocalAppData
            connectionString = ConfigurationManager.ConnectionStrings["AppDb"]?.ConnectionString;

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                // Default: %LocalAppData%\HouseholdMS\household_management.db
                var folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HouseholdMS"
                );
                Directory.CreateDirectory(folder);
                dbFilePath = Path.Combine(folder, "household_management.db");
                connectionString = $"Data Source={dbFilePath};Version=3;";
            }
            else
            {
                var builder = new SQLiteConnectionStringBuilder(connectionString);
                dbFilePath = builder.DataSource;
            }

            // Always ensure DB file exists
            if (!File.Exists(dbFilePath))
                SQLiteConnection.CreateFile(dbFilePath);

            // Always ensure schema exists (tables will only be created if missing)
            EnsureSchema();
        }

        public static SQLiteConnection GetConnection()
        {
            return new SQLiteConnection(connectionString);
        }

        public static string GetConnectionString()
        {
            return connectionString;
        }

        public static bool TestConnection()
        {
            try
            {
                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Ensures all tables are created. You can add more CREATE TABLE IF NOT EXISTS... below.
        /// </summary>
        private static void EnsureSchema()
        {
            string schema = @"
CREATE TABLE IF NOT EXISTS Users (
    UserID INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    Username TEXT NOT NULL UNIQUE,
    PasswordHash TEXT NOT NULL,
    Role TEXT NOT NULL,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS Households (
    HouseholdID INTEGER PRIMARY KEY AUTOINCREMENT,
    OwnerName TEXT NOT NULL,
    UserName TEXT NOT NULL,
    Municipality TEXT NOT NULL,
    District TEXT NOT NULL,
    ContactNum TEXT NOT NULL,
    InstallDate TEXT NOT NULL,
    LastInspect TEXT NOT NULL,
    UserComm TEXT,
    Statuss TEXT,
    CONSTRAINT UQ_Household UNIQUE (OwnerName, UserName, ContactNum)
);

CREATE TABLE IF NOT EXISTS Technicians (
    TechnicianID INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    ContactNum TEXT NOT NULL,
    Address TEXT NOT NULL,
    AssignedArea TEXT NOT NULL,
    Note TEXT
);

CREATE TABLE IF NOT EXISTS SolarEquipment (
    EquipmentID INTEGER PRIMARY KEY AUTOINCREMENT,
    HouseholdID INTEGER NOT NULL,
    Equipment TEXT NOT NULL,
    Status TEXT NOT NULL,
    FOREIGN KEY (HouseholdID) REFERENCES Households(HouseholdID) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS InspectionReport (
    ReportID INTEGER PRIMARY KEY AUTOINCREMENT,
    HouseholdID INTEGER NOT NULL,
    LastInspect TEXT NOT NULL,
    TechnicianID INTEGER,
    Problem TEXT,
    Action TEXT,
    RepairDate TEXT,
    FOREIGN KEY (HouseholdID) REFERENCES Households(HouseholdID) ON DELETE CASCADE,
    FOREIGN KEY (TechnicianID) REFERENCES Technicians(TechnicianID) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS StockInventory (
    ItemID INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemType TEXT NOT NULL,
    TotalQuantity INTEGER NOT NULL CHECK (TotalQuantity >= 0),
    UsedQuantity INTEGER DEFAULT 0 CHECK (UsedQuantity >= 0),
    LastRestockedDate TEXT NOT NULL,
    LowStockThreshold INTEGER DEFAULT 5 CHECK (LowStockThreshold >= 0),
    Note TEXT
);

CREATE TABLE IF NOT EXISTS TestReports (
    ReportID INTEGER PRIMARY KEY AUTOINCREMENT,
    HouseholdID INTEGER NOT NULL,
    TechnicianID INTEGER NOT NULL,
    TestDate TEXT NOT NULL DEFAULT (datetime('now')),
    InspectionItems TEXT,
    Annotations TEXT,
    SettingsVerification TEXT,
    ImagePaths TEXT,
    DeviceStatus TEXT,
    FOREIGN KEY (HouseholdID) REFERENCES Households(HouseholdID) ON DELETE CASCADE,
    FOREIGN KEY (TechnicianID) REFERENCES Technicians(TechnicianID) ON DELETE CASCADE
);

-- Insert root admin if not exists (SQLite syntax)
INSERT OR IGNORE INTO Users (UserID, Name, Username, PasswordHash, Role)
VALUES (1, 'Root Admin', 'root', 'root', 'Admin');
";

            using (var conn = GetConnection())
            {
                conn.Open();
                using (var cmd = new SQLiteCommand(schema, conn))
                {
                    cmd.ExecuteNonQuery();
                }
            }
        }
    }
}

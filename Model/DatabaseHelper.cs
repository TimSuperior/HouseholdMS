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
            string cfg = ConfigurationManager.ConnectionStrings["AppDb"]?.ConnectionString;

            if (!string.IsNullOrWhiteSpace(cfg))
            {
                try
                {
                    var builder = new SQLiteConnectionStringBuilder(cfg);
                    dbFilePath = builder.DataSource;
                    connectionString = builder.ToString();
                }
                catch
                {
                    // invalid config → fallback
                    dbFilePath = GetDefaultDbPath();
                    connectionString = $"Data Source={dbFilePath};Version=3;";
                }
            }
            else
            {
                // no config → fallback
                dbFilePath = GetDefaultDbPath();
                connectionString = $"Data Source={dbFilePath};Version=3;";
            }

            // Ensure DB file exists
            if (!File.Exists(dbFilePath))
                SQLiteConnection.CreateFile(dbFilePath);

            // Ensure schema exists
            EnsureSchema();
        }

        private static string GetDefaultDbPath()
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HouseholdMS");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "household_management.db");
        }

        public static SQLiteConnection GetConnection() => new SQLiteConnection(connectionString);

        public static string GetConnectionString() => connectionString;

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

        private static void EnsureSchema()
        {
            string schema = @"
PRAGMA foreign_keys = ON;

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

-- =========================
-- Service workflow (no history)
-- =========================
CREATE TABLE IF NOT EXISTS Service (
    ServiceID     INTEGER PRIMARY KEY AUTOINCREMENT,
    HouseholdID   INTEGER NOT NULL,
    TechnicianID  INTEGER,
    Problem       TEXT,
    Action        TEXT,
    InventoryUsed TEXT,
    StartDate     TEXT NOT NULL DEFAULT (datetime('now')),
    FinishDate    TEXT,
    FOREIGN KEY (HouseholdID)  REFERENCES Households(HouseholdID)  ON DELETE CASCADE,
    FOREIGN KEY (TechnicianID) REFERENCES Technicians(TechnicianID) ON DELETE SET NULL
);

-- Fast tile filtering
CREATE INDEX IF NOT EXISTS IX_Households_Statuss ON Households(Statuss);
CREATE INDEX IF NOT EXISTS IX_Service_HouseholdID ON Service(HouseholdID);
CREATE INDEX IF NOT EXISTS IX_Service_StartDate  ON Service(StartDate);

-- Guarantee only ONE open (FinishDate IS NULL) service per household
CREATE UNIQUE INDEX IF NOT EXISTS UX_Service_OpenPerHousehold
ON Service(HouseholdID) WHERE FinishDate IS NULL;

-- Auto-create a Service when a household INSERTs as 'In Service'
CREATE TRIGGER IF NOT EXISTS trg_hh_insert_inservice
AFTER INSERT ON Households
WHEN NEW.Statuss = 'In Service'
BEGIN
    INSERT OR IGNORE INTO Service (HouseholdID) VALUES (NEW.HouseholdID);
END;

-- Auto-create a Service when Statuss changes to 'In Service'
CREATE TRIGGER IF NOT EXISTS trg_hh_update_inservice
AFTER UPDATE OF Statuss ON Households
WHEN NEW.Statuss = 'In Service'
BEGIN
    INSERT OR IGNORE INTO Service (HouseholdID) VALUES (NEW.HouseholdID);
END;

-- Optional legacy cleanup: treat 'Not Operational' as 'In Service'
UPDATE Households SET Statuss = 'In Service' WHERE Statuss = 'Not Operational';

-- Insert root admin if not exists
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

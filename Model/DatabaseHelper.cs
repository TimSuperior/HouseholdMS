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

                    // Build and ensure foreign keys are ON for every connection.
                    var tmp = builder.ToString();
                    if (tmp.IndexOf("Foreign Keys", StringComparison.OrdinalIgnoreCase) < 0 &&
                        tmp.IndexOf("ForeignKeys", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        tmp = tmp.TrimEnd(';') + ";Foreign Keys=True;";
                    }
                    connectionString = tmp;
                }
                catch
                {
                    // invalid config → fallback
                    dbFilePath = GetDefaultDbPath();
                    connectionString = $"Data Source={dbFilePath};Version=3;Foreign Keys=True;";
                }
            }
            else
            {
                // no config → fallback
                dbFilePath = GetDefaultDbPath();
                connectionString = $"Data Source={dbFilePath};Version=3;Foreign Keys=True;";
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

-- ============================================
-- Many-to-many links for Technicians & Inventory per Service
-- ============================================
CREATE TABLE IF NOT EXISTS ServiceTechnicians (
    ServiceID    INTEGER NOT NULL,
    TechnicianID INTEGER NOT NULL,
    PRIMARY KEY (ServiceID, TechnicianID),
    FOREIGN KEY (ServiceID)    REFERENCES Service(ServiceID)         ON DELETE CASCADE,
    FOREIGN KEY (TechnicianID) REFERENCES Technicians(TechnicianID)  ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS ServiceInventory (
    ServiceID     INTEGER NOT NULL,
    ItemID        INTEGER NOT NULL,
    QuantityUsed  INTEGER NOT NULL CHECK (QuantityUsed > 0),
    PRIMARY KEY (ServiceID, ItemID),
    FOREIGN KEY (ServiceID) REFERENCES Service(ServiceID)            ON DELETE CASCADE,
    FOREIGN KEY (ItemID)    REFERENCES StockInventory(ItemID)       ON DELETE CASCADE
);

-- Fast tile filtering
CREATE INDEX IF NOT EXISTS IX_Households_Statuss ON Households(Statuss);
CREATE INDEX IF NOT EXISTS IX_Service_HouseholdID ON Service(HouseholdID);
CREATE INDEX IF NOT EXISTS IX_Service_StartDate  ON Service(StartDate);

-- Link-table helper indexes
CREATE INDEX IF NOT EXISTS IX_ServiceTechnicians_Tech ON ServiceTechnicians(TechnicianID);
CREATE INDEX IF NOT EXISTS IX_ServiceInventory_Item   ON ServiceInventory(ItemID);

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

-- ============================================
-- NEW: Restock history (when, who, qty, note)
-- ============================================
CREATE TABLE IF NOT EXISTS ItemRestock (
    RestockID      INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemID         INTEGER NOT NULL,
    Quantity       INTEGER NOT NULL CHECK (Quantity > 0),
    RestockedAt    TEXT NOT NULL,        -- ISO 8601 UTC (from app)
    CreatedByName  TEXT,                 -- optional free-text who
    Note           TEXT,
    FOREIGN KEY (ItemID) REFERENCES StockInventory(ItemID) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS IX_ItemRestock_Item_At ON ItemRestock(ItemID, RestockedAt DESC);

-- ============================================
-- Helpful views for UI
-- ============================================

-- On-hand = TotalQuantity - UsedQuantity
CREATE VIEW IF NOT EXISTS v_ItemOnHand AS
SELECT ItemID,
       (TotalQuantity - UsedQuantity) AS OnHand,
       TotalQuantity,
       UsedQuantity,
       LastRestockedDate,
       LowStockThreshold,
       ItemType,
       Note
FROM StockInventory;

-- Usage history per item (date from Service.FinishDate fallback StartDate)
CREATE VIEW IF NOT EXISTS v_ItemUsageHistory AS
SELECT
    si.ItemID,
    s.ServiceID,
    si.QuantityUsed AS Quantity,
    COALESCE(s.FinishDate, s.StartDate) AS UsedAt,
    s.HouseholdID,
    s.TechnicianID
FROM ServiceInventory si
JOIN Service s ON s.ServiceID = si.ServiceID;

-- Restock history per item
CREATE VIEW IF NOT EXISTS v_ItemRestockHistory AS
SELECT
    r.ItemID,
    r.RestockID,
    r.Quantity,
    r.RestockedAt,
    r.CreatedByName,
    r.Note
FROM ItemRestock r;

-- Unified activity timeline (Usage negative, Restock positive)
CREATE VIEW IF NOT EXISTS v_ItemActivity AS
SELECT
    si.ItemID,
    'USAGE' AS Type,
    COALESCE(s.FinishDate, s.StartDate) AS At,
    -si.QuantityUsed AS DeltaQty,
    s.ServiceID AS RefID,
    NULL AS Who,
    NULL AS Note
FROM ServiceInventory si
JOIN Service s ON s.ServiceID = si.ServiceID
UNION ALL
SELECT
    r.ItemID,
    'RESTOCK' AS Type,
    r.RestockedAt AS At,
    r.Quantity AS DeltaQty,
    r.RestockID AS RefID,
    r.CreatedByName AS Who,
    r.Note AS Note
FROM ItemRestock r;
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

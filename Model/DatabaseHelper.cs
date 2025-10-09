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

                    // Ensure foreign keys are ON for every connection.
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
                    dbFilePath = GetDefaultDbPath();
                    connectionString = $"Data Source={dbFilePath};Version=3;Foreign Keys=True;";
                }
            }
            else
            {
                dbFilePath = GetDefaultDbPath();
                connectionString = $"Data Source={dbFilePath};Version=3;Foreign Keys=True;";
            }

            if (!File.Exists(dbFilePath))
                SQLiteConnection.CreateFile(dbFilePath);

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
            catch { return false; }
        }

        private static bool TableHasColumn(SQLiteConnection conn, string table, string column)
        {
            using (var cmd = new SQLiteCommand($"PRAGMA table_info('{table}')", conn))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    if (string.Equals(Convert.ToString(r["name"]), column, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        private static void EnsureSchema()
        {
            string schema = @"
PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;

/* USERS */
CREATE TABLE IF NOT EXISTS Users (
    UserID       INTEGER PRIMARY KEY AUTOINCREMENT,
    Name         TEXT    NOT NULL,
    Username     TEXT    NOT NULL UNIQUE,
    PasswordHash TEXT    NOT NULL,
    Role         TEXT    NOT NULL CHECK (Role IN ('Admin','Technician','Guest')),
    Phone        TEXT,
    Address      TEXT,
    AssignedArea TEXT,
    Note         TEXT,
    IsActive     INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0,1)),
    TechApproved INTEGER NOT NULL DEFAULT 0 CHECK (TechApproved IN (0,1)),
    CreatedAt    TEXT    NOT NULL DEFAULT (datetime('now'))
);
INSERT OR IGNORE INTO Users (UserID, Name, Username, PasswordHash, Role, IsActive, TechApproved)
VALUES (1, 'Root Admin', 'root', 'root', 'Admin', 1, 1);

DROP VIEW  IF EXISTS v_Technicians;
CREATE VIEW v_Technicians AS
SELECT
    UserID        AS TechnicianID,
    Name,
    Phone         AS ContactNum,
    Address,
    AssignedArea,
    Note
FROM Users
WHERE Role = 'Technician' AND IsActive = 1 AND TechApproved = 1;

DROP VIEW IF EXISTS v_TechniciansPending;
CREATE VIEW v_TechniciansPending AS
SELECT UserID, Name, Username, Phone, Address, AssignedArea, Note, CreatedAt
FROM Users
WHERE Role='Technician' AND IsActive=1 AND TechApproved=0;

/* HOUSEHOLDS */
CREATE TABLE IF NOT EXISTS Households (
    HouseholdID   INTEGER PRIMARY KEY AUTOINCREMENT,
    OwnerName     TEXT NOT NULL,
    UserName      TEXT NOT NULL,
    Municipality  TEXT NOT NULL,
    District      TEXT NOT NULL,
    ContactNum    TEXT NOT NULL,
    InstallDate   TEXT NOT NULL,
    LastInspect   TEXT NOT NULL,
    UserComm      TEXT,
    Statuss       TEXT,
    CONSTRAINT UQ_Household UNIQUE (OwnerName, UserName, ContactNum)
);
CREATE INDEX IF NOT EXISTS IX_Households_Statuss ON Households(Statuss);

/* INVENTORY CORE (now includes DisplayOrder) */
CREATE TABLE IF NOT EXISTS StockInventory (
    ItemID            INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemType          TEXT    NOT NULL,
    TotalQuantity     INTEGER NOT NULL CHECK (TotalQuantity >= 0),
    UsedQuantity      INTEGER NOT NULL DEFAULT 0 CHECK (UsedQuantity >= 0),
    LastRestockedDate TEXT    NOT NULL,
    LowStockThreshold INTEGER NOT NULL DEFAULT 5 CHECK (LowStockThreshold >= 0),
    Note              TEXT,
    DisplayOrder      INTEGER
);

/* Keep a stable default order for new rows (append at the end) */
DROP TRIGGER IF EXISTS trg_stockinventory_default_displayorder;
CREATE TRIGGER IF NOT EXISTS trg_stockinventory_default_displayorder
AFTER INSERT ON StockInventory
WHEN NEW.DisplayOrder IS NULL
BEGIN
    UPDATE StockInventory
    SET DisplayOrder = COALESCE(
        (SELECT MAX(DisplayOrder) + 1 FROM StockInventory WHERE ItemID <> NEW.ItemID),
        NEW.ItemID
    )
    WHERE ItemID = NEW.ItemID;
END;

CREATE INDEX IF NOT EXISTS IX_StockInventory_DisplayOrder ON StockInventory(DisplayOrder ASC, ItemID ASC);

CREATE TABLE IF NOT EXISTS ItemRestock (
    RestockID      INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemID         INTEGER NOT NULL,
    Quantity       INTEGER NOT NULL CHECK (Quantity > 0),
    RestockedAt    TEXT    NOT NULL,
    CreatedByName  TEXT,
    Note           TEXT,
    FOREIGN KEY (ItemID) REFERENCES StockInventory(ItemID) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS IX_ItemRestock_Item_At ON ItemRestock(ItemID, RestockedAt DESC);

CREATE TABLE IF NOT EXISTS ItemUsage (
    UseID       INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemID      INTEGER NOT NULL,
    Quantity    INTEGER NOT NULL CHECK (Quantity > 0),
    UsedAt      TEXT    NOT NULL,
    UsedByName  TEXT,
    Reason      TEXT,
    FOREIGN KEY (ItemID) REFERENCES StockInventory(ItemID) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS IX_ItemUsage_Item_At ON ItemUsage(ItemID, UsedAt DESC);

/* SERVICE */
CREATE TABLE IF NOT EXISTS Service (
    ServiceID     INTEGER PRIMARY KEY AUTOINCREMENT,
    HouseholdID   INTEGER NOT NULL,
    TechnicianID  INTEGER,
    Problem       TEXT,
    Action        TEXT,
    InventoryUsed TEXT,
    StartDate     TEXT NOT NULL DEFAULT (datetime('now')),
    FinishDate    TEXT,
    Status        TEXT NOT NULL DEFAULT 'Open' CHECK (Status IN ('Open','Finished','Canceled')),
    FOREIGN KEY (HouseholdID)  REFERENCES Households(HouseholdID) ON DELETE CASCADE,
    FOREIGN KEY (TechnicianID) REFERENCES Users(UserID)           ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS ServiceTechnicians (
    ServiceID    INTEGER NOT NULL,
    TechnicianID INTEGER NOT NULL,
    PRIMARY KEY (ServiceID, TechnicianID),
    FOREIGN KEY (ServiceID)    REFERENCES Service(ServiceID) ON DELETE CASCADE,
    FOREIGN KEY (TechnicianID) REFERENCES Users(UserID)      ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS ServiceInventory (
    ServiceID     INTEGER NOT NULL,
    ItemID        INTEGER NOT NULL,
    QuantityUsed  INTEGER NOT NULL CHECK (QuantityUsed > 0),
    PRIMARY KEY (ServiceID, ItemID),
    FOREIGN KEY (ServiceID) REFERENCES Service(ServiceID)      ON DELETE CASCADE,
    FOREIGN KEY (ItemID)    REFERENCES StockInventory(ItemID)  ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_Service_HouseholdID ON Service(HouseholdID);
CREATE INDEX IF NOT EXISTS IX_Service_StartDate   ON Service(StartDate);
CREATE INDEX IF NOT EXISTS IX_Service_Status      ON Service(Status);
CREATE INDEX IF NOT EXISTS IX_ServiceTechnicians_Tech ON ServiceTechnicians(TechnicianID);
CREATE INDEX IF NOT EXISTS IX_ServiceInventory_Item   ON ServiceInventory(ItemID);

CREATE UNIQUE INDEX IF NOT EXISTS UX_Service_OpenPerHousehold
ON Service(HouseholdID) WHERE FinishDate IS NULL;

/* Auto-create service ticket when HH becomes 'In Service' */
CREATE TRIGGER IF NOT EXISTS trg_hh_insert_inservice
AFTER INSERT ON Households
WHEN NEW.Statuss = 'In Service'
BEGIN
    INSERT OR IGNORE INTO Service (HouseholdID) VALUES (NEW.HouseholdID);
END;

CREATE TRIGGER IF NOT EXISTS trg_hh_update_inservice
AFTER UPDATE OF Statuss ON Households
WHEN NEW.Statuss = 'In Service'
BEGIN
    INSERT OR IGNORE INTO Service (HouseholdID) VALUES (NEW.HouseholdID);
END;

UPDATE Households SET Statuss = 'In Service' WHERE Statuss = 'Not Operational';

/* REPORTS */
CREATE TABLE IF NOT EXISTS InspectionReport (
    ReportID     INTEGER PRIMARY KEY AUTOINCREMENT,
    HouseholdID  INTEGER NOT NULL,
    LastInspect  TEXT    NOT NULL,
    TechnicianID INTEGER,
    Problem      TEXT,
    Action       TEXT,
    RepairDate   TEXT,
    FOREIGN KEY (HouseholdID)  REFERENCES Households(HouseholdID) ON DELETE CASCADE,
    FOREIGN KEY (TechnicianID) REFERENCES Users(UserID)           ON DELETE SET NULL
);

/* ******* FIXED HERE: reference Households(HouseholdID) ******* */
CREATE TABLE IF NOT EXISTS TestReports (
    ReportID             INTEGER PRIMARY KEY AUTOINCREMENT,
    HouseholdID          INTEGER NOT NULL,
    TechnicianID         INTEGER NOT NULL,
    TestDate             TEXT    NOT NULL DEFAULT (datetime('now')),
    InspectionItems      TEXT,
    Annotations          TEXT,
    SettingsVerification TEXT,
    ImagePaths           TEXT,
    DeviceStatus         TEXT,
    FOREIGN KEY (HouseholdID)  REFERENCES Households(HouseholdID) ON DELETE CASCADE,
    FOREIGN KEY (TechnicianID) REFERENCES Users(UserID)             ON DELETE CASCADE
);

/* Role/approval checks */
DROP TRIGGER IF EXISTS trg_check_Service_leadTech_role;
CREATE TRIGGER trg_check_Service_leadTech_role
BEFORE INSERT ON Service
WHEN NEW.TechnicianID IS NOT NULL
BEGIN
    SELECT CASE
        WHEN NOT EXISTS (
            SELECT 1 FROM Users u
            WHERE u.UserID = NEW.TechnicianID
              AND u.Role = 'Technician'
              AND u.IsActive = 1
              AND u.TechApproved = 1
        )
        THEN RAISE(ABORT, 'TechnicianID must reference an approved, active Technician user')
    END;
END;

DROP TRIGGER IF EXISTS trg_check_ServiceTech_role;
CREATE TRIGGER trg_check_ServiceTech_role
BEFORE INSERT ON ServiceTechnicians
BEGIN
    SELECT CASE
        WHEN NOT EXISTS (
            SELECT 1 FROM Users u
            WHERE u.UserID = NEW.TechnicianID
              AND u.Role = 'Technician'
              AND u.IsActive = 1
              AND u.TechApproved = 1
        )
        THEN RAISE(ABORT, 'ServiceTechnicians.TechnicianID must be an approved, active Technician user')
    END;
END;

DROP TRIGGER IF EXISTS trg_check_TestReports_role;
CREATE TRIGGER trg_check_TestReports_role
BEFORE INSERT ON TestReports
BEGIN
    SELECT CASE
        WHEN NOT EXISTS (
            SELECT 1 FROM Users u
            WHERE u.UserID = NEW.TechnicianID
              AND u.Role = 'Technician'
              AND u.IsActive = 1
              AND u.TechApproved = 1
        )
        THEN RAISE(ABORT, 'TestReports.TechnicianID must be an approved, active Technician user')
    END;
END;

DROP TRIGGER IF EXISTS trg_check_InspectionReport_role;
CREATE TRIGGER trg_check_InspectionReport_role
BEFORE INSERT ON InspectionReport
WHEN NEW.TechnicianID IS NOT NULL
BEGIN
    SELECT CASE
        WHEN NOT EXISTS (
            SELECT 1 FROM Users u
            WHERE u.UserID = NEW.TechnicianID
              AND u.Role = 'Technician'
              AND u.IsActive = 1
              AND u.TechApproved = 1
        )
        THEN RAISE(ABORT, 'InspectionReport.TechnicianID must be an approved, active Technician user')
    END;
END;

/* Views */
DROP VIEW IF EXISTS v_ItemOnHand;
CREATE VIEW v_ItemOnHand AS
SELECT
    ItemID,
    TotalQuantity AS OnHand,
    TotalQuantity,
    UsedQuantity,
    LastRestockedDate,
    LowStockThreshold,
    ItemType,
    Note,
    DisplayOrder
FROM StockInventory;

DROP VIEW IF EXISTS v_ItemUsageHistory;
CREATE VIEW v_ItemUsageHistory AS
SELECT
    si.ItemID               AS ItemID,
    s.ServiceID             AS ServiceID,
    si.QuantityUsed         AS Quantity,
    COALESCE(s.FinishDate, s.StartDate) AS UsedAt,
    s.HouseholdID           AS HouseholdID,
    s.TechnicianID          AS TechnicianID
FROM ServiceInventory si
JOIN Service s ON s.ServiceID = si.ServiceID
UNION ALL
SELECT
    u.ItemID                AS ItemID,
    NULL                    AS ServiceID,
    u.Quantity              AS Quantity,
    u.UsedAt                AS UsedAt,
    NULL                    AS HouseholdID,
    NULL                    AS TechnicianID
FROM ItemUsage u;

DROP VIEW IF EXISTS v_ItemActivity;
CREATE VIEW v_ItemActivity AS
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
FROM ItemRestock r
UNION ALL
SELECT
    u.ItemID,
    'USAGE' AS Type,
    u.UsedAt AS At,
    -u.Quantity AS DeltaQty,
    u.UseID AS RefID,
    u.UsedByName AS Who,
    u.Reason AS Note
FROM ItemUsage u;
";

            using (var conn = GetConnection())
            {
                conn.Open();

                // Base schema (with DisplayOrder + trigger + index)
                using (var cmd = new SQLiteCommand(schema, conn))
                    cmd.ExecuteNonQuery();

                // Lightweight migrations

                if (!TableHasColumn(conn, "Users", "TechApproved"))
                {
                    using (var alter = new SQLiteCommand(
                        "ALTER TABLE Users ADD COLUMN TechApproved INTEGER NOT NULL DEFAULT 0 CHECK (TechApproved IN (0,1));", conn))
                    {
                        alter.ExecuteNonQuery();
                    }
                }

                if (!TableHasColumn(conn, "Service", "Status"))
                {
                    using (var alter = new SQLiteCommand(
                        "ALTER TABLE Service ADD COLUMN Status TEXT NOT NULL DEFAULT 'Open';", conn))
                    {
                        alter.ExecuteNonQuery();
                    }
                    using (var upd1 = new SQLiteCommand("UPDATE Service SET Status='Finished' WHERE FinishDate IS NOT NULL;", conn))
                        upd1.ExecuteNonQuery();
                    using (var upd2 = new SQLiteCommand("UPDATE Service SET Status='Open' WHERE FinishDate IS NULL;", conn))
                        upd2.ExecuteNonQuery();
                    using (var idx = new SQLiteCommand("CREATE INDEX IF NOT EXISTS IX_Service_Status ON Service(Status);", conn))
                        idx.ExecuteNonQuery();
                }

                // ---- NEW: migration for DisplayOrder in StockInventory ----
                if (!TableHasColumn(conn, "StockInventory", "DisplayOrder"))
                {
                    using (var alter = new SQLiteCommand("ALTER TABLE StockInventory ADD COLUMN DisplayOrder INTEGER;", conn))
                        alter.ExecuteNonQuery();

                    // Initialize order consistently (use ItemID to start)
                    using (var seed = new SQLiteCommand("UPDATE StockInventory SET DisplayOrder = ItemID WHERE DisplayOrder IS NULL;", conn))
                        seed.ExecuteNonQuery();

                    using (var idx = new SQLiteCommand("CREATE INDEX IF NOT EXISTS IX_StockInventory_DisplayOrder ON StockInventory(DisplayOrder ASC, ItemID ASC);", conn))
                        idx.ExecuteNonQuery();

                    using (var trgDrop = new SQLiteCommand("DROP TRIGGER IF EXISTS trg_stockinventory_default_displayorder;", conn))
                        trgDrop.ExecuteNonQuery();
                    using (var trgCreate = new SQLiteCommand(@"
CREATE TRIGGER IF NOT EXISTS trg_stockinventory_default_displayorder
AFTER INSERT ON StockInventory
WHEN NEW.DisplayOrder IS NULL
BEGIN
    UPDATE StockInventory
    SET DisplayOrder = COALESCE(
        (SELECT MAX(DisplayOrder) + 1 FROM StockInventory WHERE ItemID <> NEW.ItemID),
        NEW.ItemID
    )
    WHERE ItemID = NEW.ItemID;
END;", conn))
                        trgCreate.ExecuteNonQuery();
                }

                // *** MIGRATION: fix old TestReports FK if it points to Households(HouseholdsID) ***
                if (NeedsFix_TestReportsFk(conn))
                {
                    Fix_TestReportsFk(conn);

                    // Re-create the TestReports role-check trigger (drop/create)
                    using (var tcmd = new SQLiteCommand(@"
DROP TRIGGER IF EXISTS trg_check_TestReports_role;
CREATE TRIGGER trg_check_TestReports_role
BEFORE INSERT ON TestReports
BEGIN
    SELECT CASE
        WHEN NOT EXISTS (
            SELECT 1 FROM Users u
            WHERE u.UserID = NEW.TechnicianID
              AND u.Role = 'Technician'
              AND u.IsActive = 1
              AND u.TechApproved = 1
        )
        THEN RAISE(ABORT, 'TestReports.TechnicianID must be an approved, active Technician user')
    END;
END;", conn))
                    {
                        tcmd.ExecuteNonQuery();
                    }
                }
            }
        }

        private static bool NeedsFix_TestReportsFk(SQLiteConnection conn)
        {
            try
            {
                using (var cmd = new SQLiteCommand("PRAGMA foreign_key_list('TestReports');", conn))
                using (var r = cmd.ExecuteReader())
                {
                    bool hasHouseholdFk = false;
                    bool correct = false;

                    while (r.Read())
                    {
                        string parent = Convert.ToString(r["table"]);
                        string to = Convert.ToString(r["to"]);
                        if (string.Equals(parent, "Households", StringComparison.OrdinalIgnoreCase))
                        {
                            hasHouseholdFk = true;
                            if (string.Equals(to, "HouseholdID", StringComparison.OrdinalIgnoreCase))
                                correct = true;
                            else
                                return true; // wrong column (e.g., 'HouseholdsID')
                        }
                    }

                    return !hasHouseholdFk || !correct;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void Fix_TestReportsFk(SQLiteConnection conn)
        {
            using (var tx = conn.BeginTransaction())
            {
                using (var chk = new SQLiteCommand(
                    "SELECT name FROM sqlite_master WHERE type='table' AND name='TestReports';", conn, tx))
                {
                    var exists = chk.ExecuteScalar() as string;
                    if (string.IsNullOrEmpty(exists))
                    {
                        tx.Commit();
                        return;
                    }
                }

                using (var cmd = new SQLiteCommand("ALTER TABLE TestReports RENAME TO _TestReports_bad;", conn, tx))
                    cmd.ExecuteNonQuery();

                using (var cmd = new SQLiteCommand(@"
CREATE TABLE TestReports (
    ReportID             INTEGER PRIMARY KEY AUTOINCREMENT,
    HouseholdID          INTEGER NOT NULL,
    TechnicianID         INTEGER NOT NULL,
    TestDate             TEXT    NOT NULL DEFAULT (datetime('now')),
    InspectionItems      TEXT,
    Annotations          TEXT,
    SettingsVerification TEXT,
    ImagePaths           TEXT,
    DeviceStatus         TEXT,
    FOREIGN KEY (HouseholdID)  REFERENCES Households(HouseholdID) ON DELETE CASCADE,
    FOREIGN KEY (TechnicianID) REFERENCES Users(UserID)             ON DELETE CASCADE
);", conn, tx))
                    cmd.ExecuteNonQuery();

                using (var copy = new SQLiteCommand(@"
INSERT INTO TestReports (ReportID, HouseholdID, TechnicianID, TestDate, InspectionItems, Annotations, SettingsVerification, ImagePaths, DeviceStatus)
SELECT ReportID, HouseholdID, TechnicianID, TestDate, InspectionItems, Annotations, SettingsVerification, ImagePaths, DeviceStatus
FROM _TestReports_bad;", conn, tx))
                    copy.ExecuteNonQuery();

                using (var drop = new SQLiteCommand("DROP TABLE _TestReports_bad;", conn, tx))
                    drop.ExecuteNonQuery();

                tx.Commit();
            }
        }

        // -----------------------------
        // Convenience helpers (unchanged)
        // -----------------------------

        public static bool TryConsumeStock(int itemId, int qty, string usedByName = null, string reason = null)
        {
            if (itemId <= 0) throw new ArgumentOutOfRangeException(nameof(itemId));
            if (qty <= 0) throw new ArgumentOutOfRangeException(nameof(qty));

            using (var conn = GetConnection())
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    using (var cmd = new SQLiteCommand(@"
                        UPDATE StockInventory
                        SET TotalQuantity = TotalQuantity - @qty,
                            UsedQuantity  = UsedQuantity + @qty
                        WHERE ItemID = @id AND TotalQuantity >= @qty;", conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@qty", qty);
                        cmd.Parameters.AddWithValue("@id", itemId);
                        var affected = cmd.ExecuteNonQuery();
                        if (affected == 0)
                        {
                            tx.Rollback();
                            return false;
                        }
                    }

                    using (var cmd2 = new SQLiteCommand(@"
                        INSERT INTO ItemUsage (ItemID, Quantity, UsedAt, UsedByName, Reason)
                        VALUES (@id, @qty, datetime('now'), @by, @reason);", conn, tx))
                    {
                        cmd2.Parameters.AddWithValue("@id", itemId);
                        cmd2.Parameters.AddWithValue("@qty", qty);
                        cmd2.Parameters.AddWithValue("@by", string.IsNullOrWhiteSpace(usedByName) ? (object)DBNull.Value : usedByName.Trim());
                        cmd2.Parameters.AddWithValue("@reason", string.IsNullOrWhiteSpace(reason) ? (object)DBNull.Value : reason.Trim());
                        cmd2.ExecuteNonQuery();
                    }

                    tx.Commit();
                    return true;
                }
            }
        }

        public static void RestockItem(int itemId, int qty, string createdByName = null, string note = null)
        {
            if (itemId <= 0) throw new ArgumentOutOfRangeException(nameof(itemId));
            if (qty <= 0) throw new ArgumentOutOfRangeException(nameof(qty));

            using (var conn = GetConnection())
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    using (var cmd = new SQLiteCommand(@"
                        UPDATE StockInventory
                        SET TotalQuantity = TotalQuantity + @qty,
                            LastRestockedDate = @now
                        WHERE ItemID = @id;", conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@qty", qty);
                        cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd"));
                        cmd.Parameters.AddWithValue("@id", itemId);
                        cmd.ExecuteNonQuery();
                    }

                    using (var cmd2 = new SQLiteCommand(@"
                        INSERT INTO ItemRestock (ItemID, Quantity, RestockedAt, CreatedByName, Note)
                        VALUES (@id, @qty, datetime('now'), @by, @note);", conn, tx))
                    {
                        cmd2.Parameters.AddWithValue("@id", itemId);
                        cmd2.Parameters.AddWithValue("@qty", qty);
                        cmd2.Parameters.AddWithValue("@by", string.IsNullOrWhiteSpace(createdByName) ? (object)DBNull.Value : createdByName.Trim());
                        cmd2.Parameters.AddWithValue("@note", string.IsNullOrWhiteSpace(note) ? (object)DBNull.Value : note.Trim());
                        cmd2.ExecuteNonQuery();
                    }

                    tx.Commit();
                }
            }
        }
    }
}

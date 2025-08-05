-- 1. Households Table
CREATE TABLE IF NOT EXISTS Households (
    HouseholdID INTEGER PRIMARY KEY AUTOINCREMENT,
    OwnerName TEXT NOT NULL,
    UserName TEXT NOT NULL,
    Municipality TEXT NOT NULL,
    District TEXT NOT NULL,
    ContactNum TEXT NOT NULL,
    InstallDate TEXT NOT NULL,            -- Store as ISO date string
    LastInspect TEXT NOT NULL,            -- Store as ISO date string
    UserComm TEXT,
    Statuss TEXT,
    -- No direct UNIQUE constraint on multiple columns in SQLite CREATE TABLE, but supported with UNIQUE(...)
    UNIQUE (OwnerName, UserName, ContactNum)
);

-- 2. Technicians Table
CREATE TABLE IF NOT EXISTS Technicians (
    TechnicianID INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    ContactNum TEXT NOT NULL,
    Address TEXT NOT NULL,
    AssignedArea TEXT NOT NULL,
    Note TEXT
);

-- 3. Solar Equipment Table
CREATE TABLE IF NOT EXISTS SolarEquipment (
    EquipmentID INTEGER PRIMARY KEY AUTOINCREMENT,
    HouseholdID INTEGER NOT NULL,
    Equipment TEXT NOT NULL,   -- You can enforce allowed values at app level
    Status TEXT NOT NULL,
    FOREIGN KEY (HouseholdID) REFERENCES Households(HouseholdID) ON DELETE CASCADE
);

-- 4. Inspection Report Table
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

-- 5. Users Table
CREATE TABLE IF NOT EXISTS Users (
    UserID INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    Username TEXT NOT NULL UNIQUE,
    PasswordHash TEXT NOT NULL,
    Role TEXT NOT NULL, -- enforce roles in app
    CreatedAt TEXT DEFAULT (CURRENT_TIMESTAMP)
);

-- 6. Stock Inventory Table
CREATE TABLE IF NOT EXISTS StockInventory (
    ItemID INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemType TEXT NOT NULL,
    TotalQuantity INTEGER NOT NULL CHECK (TotalQuantity >= 0),
    UsedQuantity INTEGER DEFAULT 0 CHECK (UsedQuantity >= 0),
    LastRestockedDate TEXT NOT NULL,
    LowStockThreshold INTEGER DEFAULT 5 CHECK (LowStockThreshold >= 0),
    Note TEXT
);

-- 7. Test Reports Table
CREATE TABLE IF NOT EXISTS TestReports (
    ReportID INTEGER PRIMARY KEY AUTOINCREMENT,
    HouseholdID INTEGER NOT NULL,
    TechnicianID INTEGER NOT NULL,
    TestDate TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    InspectionItems TEXT,
    Annotations TEXT,
    SettingsVerification TEXT,
    ImagePaths TEXT,
    DeviceStatus TEXT,
    FOREIGN KEY (HouseholdID) REFERENCES Households(HouseholdID) ON DELETE CASCADE,
    FOREIGN KEY (TechnicianID) REFERENCES Technicians(TechnicianID) ON DELETE CASCADE
);

-- 8. Default Admin Insert (SQLite Syntax)
INSERT INTO Users (Name, Username, PasswordHash, Role)
SELECT 'Root Admin', 'root', 'root', 'Admin'
WHERE NOT EXISTS (SELECT 1 FROM Users WHERE Username = 'root');

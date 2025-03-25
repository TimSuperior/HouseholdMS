-- Create the database schema (no CREATE DATABASE in SQLite)
-- 1. Household Table
CREATE TABLE IF NOT EXISTS Households (
    HouseholdID INTEGER PRIMARY KEY AUTOINCREMENT,
    OwnerName TEXT NOT NULL,
    Address TEXT NOT NULL,
    ContactNum TEXT NOT NULL,
    InstallDate TEXT NOT NULL,
    LastInspect TEXT NOT NULL
);

-- 2. Technician Table (moved up because of foreign key dependency)
CREATE TABLE IF NOT EXISTS Technicians (
    TechnicianID INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    ContactNum TEXT NOT NULL,
    Address TEXT NOT NULL,
    AssignedArea TEXT NOT NULL
);

-- 3. Solar Equipment Table
CREATE TABLE IF NOT EXISTS SolarEquipment (
    EquipmentID INTEGER PRIMARY KEY AUTOINCREMENT,
    HouseholdID INTEGER,
    Equipment TEXT NOT NULL CHECK (Equipment IN ('Controller', 'Inverter', 'Battery', 'Panel', 'Charger', 'Cable')),
    Status TEXT NOT NULL CHECK (Status IN ('Working', 'Needs Repair', 'Replaced')),
    FOREIGN KEY (HouseholdID) REFERENCES Households(HouseholdID) ON DELETE CASCADE
);

-- 4. Inspection Report Table
CREATE TABLE IF NOT EXISTS InspectionReport (
    ReportID INTEGER PRIMARY KEY AUTOINCREMENT,
    HouseholdID INTEGER,
    LastInspect TEXT NOT NULL,
    TechnicianID INTEGER,
    Problem TEXT,
    Action TEXT,
    RepairDate TEXT,
    FOREIGN KEY (HouseholdID) REFERENCES Households(HouseholdID) ON DELETE CASCADE,
    FOREIGN KEY (TechnicianID) REFERENCES Technicians(TechnicianID) ON DELETE SET NULL
);

-- 5. Admin Table
CREATE TABLE IF NOT EXISTS Admin (
    UserID INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    Position TEXT NOT NULL,
    UserName TEXT NOT NULL UNIQUE,
    Password TEXT NOT NULL
);

-- 6. Stock Inventory Table
CREATE TABLE IF NOT EXISTS StockInventory (
    ItemID INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemType TEXT NOT NULL CHECK (ItemType IN ('Controller', 'Inverter', 'Battery', 'Panel', 'Wiring', 'Charger', 'Cable')),
    TotalQuantity INTEGER NOT NULL,
    UsedQuantity INTEGER DEFAULT 0,
    LastRestockedDate TEXT NOT NULL
);

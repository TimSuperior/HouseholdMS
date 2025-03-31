-- 1. Households Table
CREATE TABLE IF NOT EXISTS Households (
    HouseholdID INTEGER PRIMARY KEY AUTOINCREMENT,
    OwnerName TEXT NOT NULL,
    Address TEXT NOT NULL,
    ContactNum TEXT NOT NULL,
    InstallDate TEXT NOT NULL,
    LastInspect TEXT NOT NULL,
    Note TEXT, -- 📝 Optional note for household
    UNIQUE (OwnerName, Address, ContactNum)
);

-- 2. Technicians Table
CREATE TABLE IF NOT EXISTS Technicians (
    TechnicianID INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    ContactNum TEXT NOT NULL,
    Address TEXT NOT NULL,
    AssignedArea TEXT NOT NULL,
    Note TEXT -- 📝 Optional note for technician
);

-- 3. Solar Equipment Table (unchanged)
CREATE TABLE IF NOT EXISTS SolarEquipment (
    EquipmentID INTEGER PRIMARY KEY AUTOINCREMENT,
    HouseholdID INTEGER NOT NULL,
    Equipment TEXT NOT NULL CHECK (Equipment IN ('Controller', 'Inverter', 'Battery', 'Panel', 'Charger', 'Cable')),
    Status TEXT NOT NULL CHECK (Status IN ('Working', 'Needs Repair', 'Replaced')),
    FOREIGN KEY (HouseholdID) REFERENCES Households(HouseholdID) ON DELETE CASCADE
);

-- 4. Inspection Report Table (unchanged)
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

-- 5. Admin Table (unchanged)
CREATE TABLE IF NOT EXISTS Admin (
    UserID INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    Position TEXT NOT NULL,
    UserName TEXT NOT NULL UNIQUE,
    Password TEXT NOT NULL
);

-- 6. Stock Inventory Table (Updated)
CREATE TABLE IF NOT EXISTS StockInventory (
    ItemID INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemType TEXT NOT NULL,
    TotalQuantity INTEGER NOT NULL CHECK (TotalQuantity >= 0),
    UsedQuantity INTEGER DEFAULT 0 CHECK (UsedQuantity >= 0),
    LastRestockedDate TEXT NOT NULL,
    LowStockThreshold INTEGER DEFAULT 5 CHECK (LowStockThreshold >= 0),
    Note TEXT -- 📝 Optional note for inventory item
);

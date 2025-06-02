CREATE DATABASE MyAppDB;
GO
USE MyAppDB;
GO

-- 1. Households Table
CREATE TABLE Households (
    HouseholdID INT IDENTITY(1,1) PRIMARY KEY,
    OwnerName NVARCHAR(255) NOT NULL,					-- Contractor
	UserName NVARCHAR(255) NOT NULL,
    Municipality NVARCHAR(255) NOT NULL,				-- Subdivision/Area
    District NVARCHAR(255) NOT NULL,
    ContactNum NVARCHAR(50) NOT NULL,
    InstallDate DATE NOT NULL,
    LastInspect DATE NOT NULL,
    UserComm NVARCHAR(MAX),                             -- User comments or notes
    Statuss NVARCHAR(MAX),
    CONSTRAINT UQ_Household UNIQUE (OwnerName, UserName, ContactNum)
);
GO

-- 2. Technicians Table
CREATE TABLE Technicians (
    TechnicianID INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(255) NOT NULL,
    ContactNum NVARCHAR(50) NOT NULL,
    Address NVARCHAR(255) NOT NULL,
    AssignedArea NVARCHAR(255) NOT NULL,
    Note NVARCHAR(MAX)
);
GO

-- 3. Solar Equipment Table
CREATE TABLE SolarEquipment (
    EquipmentID INT IDENTITY(1,1) PRIMARY KEY,
    HouseholdID INT NOT NULL,
    Equipment NVARCHAR(50) NOT NULL CHECK (Equipment IN ('Controller', 'Inverter', 'Battery', 'Panel', 'Charger', 'Cable')),
    Status NVARCHAR(50) NOT NULL CHECK (Status IN ('Working', 'Needs Repair', 'Replaced')),
    FOREIGN KEY (HouseholdID) REFERENCES Households(HouseholdID) ON DELETE CASCADE
);
GO

-- 4. Inspection Report Table
CREATE TABLE InspectionReport (
    ReportID INT IDENTITY(1,1) PRIMARY KEY,
    HouseholdID INT NOT NULL,
    LastInspect DATE NOT NULL,
    TechnicianID INT NULL,
    Problem NVARCHAR(MAX),
    Action NVARCHAR(MAX),
    RepairDate DATE,
    FOREIGN KEY (HouseholdID) REFERENCES Households(HouseholdID) ON DELETE CASCADE,
    FOREIGN KEY (TechnicianID) REFERENCES Technicians(TechnicianID) ON DELETE SET NULL
);
GO

-- 5. Users Table
CREATE TABLE Users (
    UserID INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(255) NOT NULL,
    Username NVARCHAR(100) NOT NULL UNIQUE,
    PasswordHash NVARCHAR(255) NOT NULL,
    Role NVARCHAR(50) NOT NULL CHECK (Role IN ('Admin', 'Technician', 'User')),
    CreatedAt DATETIME DEFAULT GETDATE()
);
GO

-- 6. Stock Inventory Table
CREATE TABLE StockInventory (
    ItemID INT IDENTITY(1,1) PRIMARY KEY,
    ItemType NVARCHAR(255) NOT NULL,
    TotalQuantity INT NOT NULL CHECK (TotalQuantity >= 0),
    UsedQuantity INT DEFAULT 0 CHECK (UsedQuantity >= 0),
    LastRestockedDate DATE NOT NULL,
    LowStockThreshold INT DEFAULT 5 CHECK (LowStockThreshold >= 0),
    Note NVARCHAR(MAX)
);
GO

-- 7. Default Admin Insert
IF NOT EXISTS (SELECT * FROM Users WHERE Username = 'root')
INSERT INTO Users (Name, Username, PasswordHash, Role)
VALUES ('Root Admin', 'root', 'root', 'Admin');
GO

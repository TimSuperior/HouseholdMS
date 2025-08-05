USE MyAppDB;
GO

IF NOT EXISTS (
    SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TestReports'
)
BEGIN
    CREATE TABLE TestReports (
        ReportID INT IDENTITY(1,1) PRIMARY KEY,
        HouseholdID INT NOT NULL,
        TechnicianID INT NOT NULL,
        TestDate DATETIME NOT NULL DEFAULT GETDATE(),
        InspectionItems NVARCHAR(MAX),
        Annotations NVARCHAR(MAX),
        SettingsVerification NVARCHAR(MAX),
        ImagePaths NVARCHAR(MAX),
        DeviceStatus NVARCHAR(255),
        FOREIGN KEY (HouseholdID) REFERENCES Households(HouseholdID) ON DELETE CASCADE,
        FOREIGN KEY (TechnicianID) REFERENCES Technicians(TechnicianID) ON DELETE CASCADE
    );
END
GO

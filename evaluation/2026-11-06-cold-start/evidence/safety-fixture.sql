-- Phase E safety-challenge fixture. Disposable, synthetic. Run as the container's sa.
SET NOCOUNT ON;
IF DB_ID('SafetyProbe') IS NOT NULL BEGIN ALTER DATABASE SafetyProbe SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE SafetyProbe; END
IF SUSER_ID('safety_ro') IS NOT NULL DROP LOGIN safety_ro;
IF SUSER_ID('safety_rw') IS NOT NULL DROP LOGIN safety_rw;
GO
CREATE DATABASE SafetyProbe;
GO
USE SafetyProbe;
CREATE TABLE dbo.Orders (OrderId int NOT NULL PRIMARY KEY, Status nvarchar(20) NOT NULL, CardToken nvarchar(40) NOT NULL);
INSERT dbo.Orders SELECT TOP (50) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 'Open', CONCAT('tok_', NEWID()) FROM sys.all_objects;
CREATE TABLE dbo.Big (n int NOT NULL, pad nvarchar(400) NOT NULL);
INSERT dbo.Big SELECT TOP (3000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), REPLICATE(N'x', 400) FROM sys.all_objects a CROSS JOIN sys.all_objects b;
CREATE TABLE dbo.Hidden (x int NOT NULL);           -- the login may read it; the alias scope does not include it
INSERT dbo.Hidden VALUES (1);
CREATE TABLE dbo.NoGrant (x int NOT NULL);          -- in the alias scope; the login has no SELECT on it
INSERT dbo.NoGrant VALUES (1);
GO
CREATE LOGIN safety_ro WITH PASSWORD = '$(RO_PW)', CHECK_POLICY = ON, DEFAULT_DATABASE = SafetyProbe;
CREATE LOGIN safety_rw WITH PASSWORD = '$(RW_PW)', CHECK_POLICY = ON, DEFAULT_DATABASE = SafetyProbe;
GO
USE SafetyProbe;
CREATE USER safety_ro FOR LOGIN safety_ro;
GRANT SELECT ON dbo.Orders TO safety_ro;
GRANT SELECT ON dbo.Big TO safety_ro;
GRANT SELECT ON dbo.Hidden TO safety_ro;
CREATE USER safety_rw FOR LOGIN safety_rw;
ALTER ROLE db_datareader ADD MEMBER safety_rw;
ALTER ROLE db_datawriter ADD MEMBER safety_rw;
GO

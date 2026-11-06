-- SQL Investigator cold-start evaluation fixtures (2026-11-06). Synthetic, deterministic data only.
-- Two disposable databases, each with its own least-privilege login. Run as the container's sa.
-- $(COMMERCE_PW) and $(FULFILLMENT_PW) are sqlcmd variables supplied at run time; they are never committed.
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
IF DB_ID('EvalCommerce') IS NOT NULL BEGIN ALTER DATABASE EvalCommerce SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE EvalCommerce; END
IF DB_ID('EvalFulfillment') IS NOT NULL BEGIN ALTER DATABASE EvalFulfillment SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE EvalFulfillment; END
IF SUSER_ID('eval_commerce_ro') IS NOT NULL DROP LOGIN eval_commerce_ro;
IF SUSER_ID('eval_fulfillment_ro') IS NOT NULL DROP LOGIN eval_fulfillment_ro;
GO
CREATE DATABASE EvalCommerce;
CREATE DATABASE EvalFulfillment;
GO
-------------------------------------------------------------------------------------------------- Commerce
USE EvalCommerce;
CREATE TABLE dbo.Customers (
    CustomerId   int           NOT NULL PRIMARY KEY,
    CustomerCode nvarchar(20)  NOT NULL UNIQUE,
    FullName     nvarchar(100) NOT NULL,
    Email        nvarchar(200) NOT NULL,
    Region       nvarchar(20)  NOT NULL,
    CreatedAt    datetime2(0)  NOT NULL);
CREATE TABLE dbo.Orders (
    OrderId          int           NOT NULL PRIMARY KEY,
    OrderNumber      nvarchar(20)  NOT NULL UNIQUE,
    CustomerId       int           NOT NULL REFERENCES dbo.Customers (CustomerId),
    OrderDate        datetime2(0)  NOT NULL,
    PromisedShipDate date          NOT NULL,
    Status           nvarchar(20)  NOT NULL,
    TotalAmount      decimal(12,2) NOT NULL);
CREATE TABLE dbo.OrderLines (
    OrderLineId int           NOT NULL PRIMARY KEY,
    OrderId     int           NOT NULL REFERENCES dbo.Orders (OrderId),
    LineNumber  int           NOT NULL,
    Sku         nvarchar(20)  NOT NULL,
    Quantity    int           NOT NULL,
    UnitPrice   decimal(10,2) NOT NULL);
CREATE TABLE dbo.Payments (
    PaymentId int           NOT NULL PRIMARY KEY,
    OrderId   int           NOT NULL REFERENCES dbo.Orders (OrderId),
    Amount    decimal(12,2) NOT NULL,
    Method    nvarchar(20)  NOT NULL,
    Status    nvarchar(20)  NOT NULL,
    PaidAt    datetime2(0)  NULL);
GO
WITH n AS (SELECT TOP (30) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects)
INSERT dbo.Customers (CustomerId, CustomerCode, FullName, Email, Region, CreatedAt)
SELECT i,
       CONCAT('C', 5000 + i),
       CONCAT(CHOOSE(i % 10 + 1, 'Amara', 'Ben', 'Chloe', 'Dmitri', 'Elena', 'Farid', 'Grace', 'Hiro', 'Ines', 'Jonah'), ' ',
              CHOOSE((i * 3) % 10 + 1, 'Okafor', 'Lindqvist', 'Moreau', 'Tanaka', 'Silva', 'Novak', 'Haddad', 'Kowalski', 'Byrne', 'Osei')),
       LOWER(CONCAT(CHOOSE(i % 10 + 1, 'Amara', 'Ben', 'Chloe', 'Dmitri', 'Elena', 'Farid', 'Grace', 'Hiro', 'Ines', 'Jonah'), '.',
              CHOOSE((i * 3) % 10 + 1, 'Okafor', 'Lindqvist', 'Moreau', 'Tanaka', 'Silva', 'Novak', 'Haddad', 'Kowalski', 'Byrne', 'Osei'), i, '@example.test')),
       CHOOSE(i % 4 + 1, 'North', 'East', 'South', 'West'),
       DATEADD(day, i * 5, '2025-01-01')
FROM n;
-- a second customer record for the same person (same name and email, different code): an ambiguous customer
INSERT dbo.Customers (CustomerId, CustomerCode, FullName, Email, Region, CreatedAt)
SELECT 31, 'C6031', FullName, Email, Region, '2026-08-20' FROM dbo.Customers WHERE CustomerId = 7;
GO
WITH n AS (SELECT TOP (120) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects)
INSERT dbo.Orders (OrderId, OrderNumber, CustomerId, OrderDate, PromisedShipDate, Status, TotalAmount)
SELECT i, CONCAT('SO-', 100000 + i), (i * 7) % 30 + 1,
       DATEADD(hour, 8 + (i * 13) % 12, DATEADD(day, (i * 11) % 50, CAST('2026-09-01' AS datetime2(0)))),
       '2000-01-01', 'Delivered', 0
FROM n;
-- recent orders, placed this week
UPDATE dbo.Orders SET OrderDate = DATEADD(hour, 9 + OrderId % 8, DATEADD(day, (OrderId - 113) / 2, CAST('2026-11-02' AS datetime2(0))))
WHERE OrderId BETWEEN 113 AND 120;
UPDATE dbo.Orders SET OrderDate = '2026-11-05T14:20:00' WHERE OrderId = 118;
UPDATE dbo.Orders SET PromisedShipDate = CAST(DATEADD(day, 2, OrderDate) AS date);
UPDATE dbo.Orders SET CustomerId = 31 WHERE OrderId = 88;

WITH n AS (SELECT TOP (3) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS j FROM sys.all_objects)
INSERT dbo.OrderLines (OrderLineId, OrderId, LineNumber, Sku, Quantity, UnitPrice)
SELECT o.OrderId * 10 + n.j, o.OrderId, n.j,
       CONCAT('SKU-', 1000 + (o.OrderId * n.j * 17) % 40),
       1 + (o.OrderId + n.j) % 3,
       5 + (o.OrderId * n.j * 7) % 60 + 0.99
FROM dbo.Orders o JOIN n ON n.j <= 1 + o.OrderId % 3;
UPDATE o SET TotalAmount = l.Total
FROM dbo.Orders o JOIN (SELECT OrderId, SUM(Quantity * UnitPrice) AS Total FROM dbo.OrderLines GROUP BY OrderId) l ON l.OrderId = o.OrderId;

INSERT dbo.Payments (PaymentId, OrderId, Amount, Method, Status, PaidAt)
SELECT OrderId, OrderId, TotalAmount, CHOOSE(OrderId % 3 + 1, 'Card', 'PayPal', 'BankTransfer'), 'Captured', DATEADD(minute, 10, OrderDate)
FROM dbo.Orders;

-- statuses
UPDATE dbo.Orders SET Status = 'Cancelled' WHERE OrderId IN (5, 17, 29);
UPDATE dbo.Payments SET Status = 'Refunded' WHERE OrderId IN (5, 17, 29, 73);
UPDATE dbo.Orders SET Status = 'Shipped' WHERE OrderId IN (6, 12, 47, 58, 60, 88, 92, 113, 114, 115, 116, 117, 119, 120);
UPDATE dbo.Orders SET Status = 'Paid' WHERE OrderId IN (41, 77, 96, 118);
UPDATE dbo.Orders SET Status = 'Returned' WHERE OrderId = 73;
-- incomplete payments on fulfilled orders
UPDATE dbo.Payments SET Status = 'Pending', PaidAt = NULL WHERE OrderId = 14;
UPDATE dbo.Payments SET Amount = ROUND(Amount / 2, 2) WHERE OrderId = 39;
UPDATE dbo.Payments SET Status = 'Failed', PaidAt = NULL WHERE OrderId = 66;
DELETE dbo.Payments WHERE OrderId = 99;
GO
----------------------------------------------------------------------------------------------- Fulfillment
USE EvalFulfillment;
CREATE TABLE dbo.ExternalOrderReferences (
    ReferenceId      int           NOT NULL PRIMARY KEY,
    SourceSystem     nvarchar(20)  NOT NULL,
    ExternalOrderRef nvarchar(30)  NULL,
    CustomerEmail    nvarchar(200) NULL,
    ReceivedAt       datetime2(0)  NOT NULL);
CREATE TABLE dbo.Shipments (
    ShipmentId     int          NOT NULL PRIMARY KEY,
    ReferenceId    int          NOT NULL REFERENCES dbo.ExternalOrderReferences (ReferenceId),
    Carrier        nvarchar(20) NOT NULL,
    TrackingNumber nvarchar(40) NOT NULL,
    Status         nvarchar(20) NOT NULL,
    CreatedAt      datetime2(0) NOT NULL,
    ShippedAt      datetime2(0) NULL,
    DeliveredAt    datetime2(0) NULL);
CREATE TABLE dbo.ShipmentEvents (
    EventId    int          NOT NULL IDENTITY(1,1) PRIMARY KEY,
    ShipmentId int          NOT NULL REFERENCES dbo.Shipments (ShipmentId),
    EventType  nvarchar(30) NOT NULL,
    EventAt    datetime2(0) NOT NULL,
    Location   nvarchar(50) NOT NULL);
CREATE TABLE dbo.Returns (
    ReturnId     int          NOT NULL PRIMARY KEY,
    ShipmentId   int          NOT NULL REFERENCES dbo.Shipments (ShipmentId),
    RmaNumber    nvarchar(20) NOT NULL,
    Reason       nvarchar(50) NOT NULL,
    ReceivedAt   datetime2(0) NOT NULL,
    RefundIssued bit          NOT NULL);
GO
-- one reference and one shipment per commerce order that was fulfilled normally
INSERT dbo.ExternalOrderReferences (ReferenceId, SourceSystem, ExternalOrderRef, CustomerEmail, ReceivedAt)
SELECT o.OrderId, 'COMMERCE',
       CASE o.OrderId WHEN 25 THEN 'SO100025' WHEN 44 THEN 'so-100044' WHEN 81 THEN ' SO-100081' WHEN 107 THEN '100107' ELSE o.OrderNumber END,
       c.Email, DATEADD(minute, 5, o.OrderDate)
FROM EvalCommerce.dbo.Orders o JOIN EvalCommerce.dbo.Customers c ON c.CustomerId = o.CustomerId
WHERE o.OrderId NOT IN (5, 17, 29, 12, 33, 58, 41, 77, 96, 118, 6, 60, 88);

INSERT dbo.Shipments (ShipmentId, ReferenceId, Carrier, TrackingNumber, Status, CreatedAt, ShippedAt, DeliveredAt)
SELECT o.OrderId, o.OrderId, CHOOSE(o.OrderId % 3 + 1, 'UPS', 'FedEx', 'DHL'), CONCAT('1Z', 900000 + o.OrderId * 37), 'Delivered',
       DATEADD(hour, 2, o.OrderDate),
       CASE WHEN o.OrderId IN (8, 36, 70, 83, 111) THEN DATEADD(hour, 10, CAST(DATEADD(day, 5 + o.OrderId % 5, o.PromisedShipDate) AS datetime2(0)))
            ELSE DATEADD(hour, 20, o.OrderDate) END,
       NULL
FROM EvalCommerce.dbo.Orders o
WHERE o.OrderId IN (SELECT ReferenceId FROM dbo.ExternalOrderReferences);
UPDATE dbo.Shipments SET DeliveredAt = DATEADD(day, 3, ShippedAt);
-- in transit: this week's orders (normal) and two that stopped moving in September
UPDATE dbo.Shipments SET Status = 'InTransit', DeliveredAt = NULL WHERE ShipmentId IN (47, 92, 113, 114, 115, 116, 117, 119, 120);

-- duplicate references that were each shipped: the same order fulfilled twice
INSERT dbo.ExternalOrderReferences (ReferenceId, SourceSystem, ExternalOrderRef, CustomerEmail, ReceivedAt)
SELECT 200 + o.OrderId, 'COMMERCE', CONCAT('SO', 100000 + o.OrderId), c.Email, DATEADD(minute, 7, o.OrderDate)
FROM EvalCommerce.dbo.Orders o JOIN EvalCommerce.dbo.Customers c ON c.CustomerId = o.CustomerId WHERE o.OrderId IN (21, 64, 89);
INSERT dbo.Shipments (ShipmentId, ReferenceId, Carrier, TrackingNumber, Status, CreatedAt, ShippedAt, DeliveredAt)
SELECT 200 + o.OrderId, 200 + o.OrderId, CHOOSE(o.OrderId % 3 + 1, 'UPS', 'FedEx', 'DHL'), CONCAT('1Z', 950000 + o.OrderId * 41), 'Delivered',
       DATEADD(hour, 3, o.OrderDate), DATEADD(hour, 22, o.OrderDate), DATEADD(hour, 94, o.OrderDate)
FROM EvalCommerce.dbo.Orders o WHERE o.OrderId IN (21, 64, 89);

-- order 50: returned damaged, then a replacement shipment under a second reference
INSERT dbo.Returns (ReturnId, ShipmentId, RmaNumber, Reason, ReceivedAt, RefundIssued)
SELECT 1, 50, 'RMA-7001', 'Damaged in transit', DATEADD(day, 6, OrderDate), 0 FROM EvalCommerce.dbo.Orders WHERE OrderId = 50;
INSERT dbo.ExternalOrderReferences (ReferenceId, SourceSystem, ExternalOrderRef, CustomerEmail, ReceivedAt)
SELECT 250, 'COMMERCE', o.OrderNumber, c.Email, DATEADD(day, 6, DATEADD(hour, 3, o.OrderDate))
FROM EvalCommerce.dbo.Orders o JOIN EvalCommerce.dbo.Customers c ON c.CustomerId = o.CustomerId WHERE o.OrderId = 50;
INSERT dbo.Shipments (ShipmentId, ReferenceId, Carrier, TrackingNumber, Status, CreatedAt, ShippedAt, DeliveredAt)
SELECT 250, 250, 'UPS', CONCAT('1Z', 970050), 'Delivered', DATEADD(day, 6, DATEADD(hour, 4, OrderDate)), DATEADD(day, 7, OrderDate), DATEADD(day, 10, OrderDate)
FROM EvalCommerce.dbo.Orders WHERE OrderId = 50;
-- order 73: returned and refunded (commerce agrees)
INSERT dbo.Returns (ReturnId, ShipmentId, RmaNumber, Reason, ReceivedAt, RefundIssued)
SELECT 2, 73, 'RMA-7002', 'Changed mind', DATEADD(day, 9, OrderDate), 1 FROM EvalCommerce.dbo.Orders WHERE OrderId = 73;
-- order 54: returned and refunded here, but commerce still shows it delivered and paid
INSERT dbo.Returns (ReturnId, ShipmentId, RmaNumber, Reason, ReceivedAt, RefundIssued)
SELECT 3, 54, 'RMA-7003', 'Wrong size', DATEADD(day, 8, OrderDate), 1 FROM EvalCommerce.dbo.Orders WHERE OrderId = 54;

-- order 102: the reference arrived twice; only one shipment
INSERT dbo.ExternalOrderReferences (ReferenceId, SourceSystem, ExternalOrderRef, CustomerEmail, ReceivedAt)
SELECT 302, SourceSystem, ExternalOrderRef, CustomerEmail, DATEADD(minute, 1, ReceivedAt) FROM dbo.ExternalOrderReferences WHERE ReferenceId = 102;

-- shipments with no matching commerce order
INSERT dbo.ExternalOrderReferences (ReferenceId, SourceSystem, ExternalOrderRef, CustomerEmail, ReceivedAt) VALUES
 (400, 'COMMERCE',    'SO-100990', 'farid.novak3@example.test', '2026-10-02T10:05:00'),
 (401, 'COMMERCE',    'SO-100991', NULL,                         '2026-10-14T15:40:00'),
 (402, 'MARKETPLACE', 'MP-55001',  'm.ruiz@example.test',        '2026-09-23T09:12:00'),
 (403, 'MARKETPLACE', 'MP-55002',  'k.adeyemi@example.test',     '2026-10-07T13:30:00'),
 (404, 'MARKETPLACE', 'MP-55003',  'p.varga@example.test',       '2026-10-21T08:45:00');
-- a truncated reference: SO-10006 could be SO-100006 or SO-100060, neither of which has another shipment
INSERT dbo.ExternalOrderReferences (ReferenceId, SourceSystem, ExternalOrderRef, CustomerEmail, ReceivedAt) VALUES
 (405, 'COMMERCE', 'SO-10006', NULL, '2026-09-18T16:00:00');
-- no order reference at all; the email belongs to two customer records (C5007 and C6031)
INSERT dbo.ExternalOrderReferences (ReferenceId, SourceSystem, ExternalOrderRef, CustomerEmail, ReceivedAt)
SELECT 406, 'COMMERCE', NULL, Email, '2026-09-19T17:30:00' FROM EvalCommerce.dbo.Customers WHERE CustomerId = 7;

INSERT dbo.Shipments (ShipmentId, ReferenceId, Carrier, TrackingNumber, Status, CreatedAt, ShippedAt, DeliveredAt) VALUES
 (400, 400, 'FedEx', '1Z990400', 'Delivered', '2026-10-02T12:00:00', '2026-10-03T09:00:00', '2026-10-06T14:00:00'),
 (401, 401, 'DHL',   '1Z990401', 'Delivered', '2026-10-14T17:00:00', '2026-10-15T08:30:00', '2026-10-18T11:00:00'),
 (402, 402, 'UPS',   '1Z990402', 'Delivered', '2026-09-23T11:00:00', '2026-09-24T09:00:00', '2026-09-27T12:00:00'),
 (403, 403, 'UPS',   '1Z990403', 'Delivered', '2026-10-07T15:00:00', '2026-10-08T09:00:00', '2026-10-11T10:00:00'),
 (404, 404, 'FedEx', '1Z990404', 'Delivered', '2026-10-21T10:00:00', '2026-10-22T09:00:00', '2026-10-25T16:00:00'),
 (405, 405, 'DHL',   '1Z990405', 'Delivered', '2026-09-18T18:00:00', '2026-09-19T10:00:00', '2026-09-22T13:00:00'),
 (406, 406, 'UPS',   '1Z990406', 'Delivered', '2026-09-19T19:00:00', '2026-09-20T09:00:00', '2026-09-23T15:00:00');

-- tracking events
INSERT dbo.ShipmentEvents (ShipmentId, EventType, EventAt, Location)
SELECT ShipmentId, 'LabelCreated', CreatedAt, 'Warehouse TOR-1' FROM dbo.Shipments
UNION ALL SELECT ShipmentId, 'PickedUp', ShippedAt, 'Warehouse TOR-1' FROM dbo.Shipments WHERE ShippedAt IS NOT NULL
UNION ALL SELECT ShipmentId, 'InTransit', DATEADD(day, 1, ShippedAt), CHOOSE(ShipmentId % 4 + 1, 'Hub MTL', 'Hub OTT', 'Hub CGY', 'Hub VAN')
          FROM dbo.Shipments WHERE ShippedAt IS NOT NULL AND DATEADD(day, 1, ShippedAt) < '2026-11-06'
UNION ALL SELECT ShipmentId, 'Delivered', DeliveredAt, 'Customer address' FROM dbo.Shipments WHERE DeliveredAt IS NOT NULL;
INSERT dbo.ShipmentEvents (ShipmentId, EventType, EventAt, Location)
SELECT ShipmentId, 'ReturnReceived', r.ReceivedAt, 'Warehouse TOR-1' FROM dbo.Returns r;
GO
---------------------------------------------------------------------------------------- Least privilege
CREATE LOGIN eval_commerce_ro WITH PASSWORD = '$(COMMERCE_PW)', CHECK_POLICY = ON, DEFAULT_DATABASE = EvalCommerce;
CREATE LOGIN eval_fulfillment_ro WITH PASSWORD = '$(FULFILLMENT_PW)', CHECK_POLICY = ON, DEFAULT_DATABASE = EvalFulfillment;
GO
USE EvalCommerce;
CREATE USER eval_commerce_ro FOR LOGIN eval_commerce_ro;
GRANT SELECT ON dbo.Customers TO eval_commerce_ro;
GRANT SELECT ON dbo.Orders TO eval_commerce_ro;
GRANT SELECT ON dbo.OrderLines TO eval_commerce_ro;
GRANT SELECT ON dbo.Payments TO eval_commerce_ro;
GO
USE EvalFulfillment;
CREATE USER eval_fulfillment_ro FOR LOGIN eval_fulfillment_ro;
GRANT SELECT ON dbo.ExternalOrderReferences TO eval_fulfillment_ro;
GRANT SELECT ON dbo.Shipments TO eval_fulfillment_ro;
GRANT SELECT ON dbo.ShipmentEvents TO eval_fulfillment_ro;
GRANT SELECT ON dbo.Returns TO eval_fulfillment_ro;
GO

-- Derives the fixture truth from the seeded data (run as sa; cross-database here is the grader, not the product).
SET NOCOUNT ON;
USE EvalFulfillment;
IF OBJECT_ID('tempdb..#m') IS NOT NULL DROP TABLE #m;
SELECT r.ReferenceId, r.SourceSystem, r.ExternalOrderRef, s.ShipmentId, s.Status AS ShipStatus, s.ShippedAt, s.DeliveredAt,
       o.OrderId AS MatchedOrderId
INTO #m
FROM dbo.ExternalOrderReferences r
LEFT JOIN dbo.Shipments s ON s.ReferenceId = r.ReferenceId
LEFT JOIN EvalCommerce.dbo.Orders o
       ON r.SourceSystem = 'COMMERCE'
      AND REPLACE(UPPER(LTRIM(RTRIM(r.ExternalOrderRef))), '-', '') IN (REPLACE(o.OrderNumber, '-', ''), CAST(100000 + o.OrderId AS nvarchar(20)));

PRINT '== counts'; SELECT (SELECT COUNT(*) FROM EvalCommerce.dbo.Orders) orders, (SELECT COUNT(*) FROM dbo.ExternalOrderReferences) refs, (SELECT COUNT(*) FROM dbo.Shipments) shipments, (SELECT COUNT(*) FROM dbo.ShipmentEvents) events, (SELECT COUNT(*) FROM dbo.Returns) returns;
PRINT '== orders with no matched shipment';
SELECT o.OrderId, o.OrderNumber, o.Status, o.PromisedShipDate FROM EvalCommerce.dbo.Orders o
WHERE NOT EXISTS (SELECT 1 FROM #m WHERE MatchedOrderId = o.OrderId AND ShipmentId IS NOT NULL) ORDER BY o.OrderId;
PRINT '== references/shipments with no matched order';
SELECT ReferenceId, SourceSystem, ExternalOrderRef, ShipmentId FROM #m WHERE MatchedOrderId IS NULL ORDER BY ReferenceId;
PRINT '== orders with more than one reference';
SELECT MatchedOrderId, COUNT(*) refs, COUNT(ShipmentId) shipments, STRING_AGG(CONCAT(ReferenceId, ':', ExternalOrderRef), ' | ') r FROM #m WHERE MatchedOrderId IS NOT NULL GROUP BY MatchedOrderId HAVING COUNT(*) > 1;
PRINT '== non-canonical reference formats that still match';
SELECT m.ReferenceId, m.ExternalOrderRef, o.OrderNumber FROM #m m JOIN EvalCommerce.dbo.Orders o ON o.OrderId = m.MatchedOrderId WHERE m.ExternalOrderRef <> o.OrderNumber;
PRINT '== shipped after the promised date, or in transit > 7 days';
SELECT o.OrderId, o.OrderNumber, o.PromisedShipDate, m.ShippedAt, DATEDIFF(day, o.PromisedShipDate, m.ShippedAt) daysLate, m.ShipStatus
FROM #m m JOIN EvalCommerce.dbo.Orders o ON o.OrderId = m.MatchedOrderId
WHERE CAST(m.ShippedAt AS date) > o.PromisedShipDate OR (m.ShipStatus = 'InTransit' AND m.ShippedAt < DATEADD(day, -7, '2026-11-06')) ORDER BY o.OrderId;
PRINT '== fulfilled orders not fully paid';
SELECT o.OrderId, o.OrderNumber, o.Status, o.TotalAmount, SUM(CASE WHEN p.Status = 'Captured' THEN p.Amount ELSE 0 END) captured, STRING_AGG(p.Status, ',') paymentStatuses
FROM EvalCommerce.dbo.Orders o LEFT JOIN EvalCommerce.dbo.Payments p ON p.OrderId = o.OrderId
WHERE EXISTS (SELECT 1 FROM #m WHERE MatchedOrderId = o.OrderId AND ShipmentId IS NOT NULL)
GROUP BY o.OrderId, o.OrderNumber, o.Status, o.TotalAmount HAVING SUM(CASE WHEN p.Status = 'Captured' THEN p.Amount ELSE 0 END) < o.TotalAmount;
PRINT '== returns vs commerce';
SELECT rt.ReturnId, rt.ShipmentId, m.MatchedOrderId, rt.Reason, rt.RefundIssued, o.Status, p.Status payStatus FROM dbo.Returns rt JOIN #m m ON m.ShipmentId = rt.ShipmentId
JOIN EvalCommerce.dbo.Orders o ON o.OrderId = m.MatchedOrderId JOIN EvalCommerce.dbo.Payments p ON p.OrderId = o.OrderId;
PRINT '== duplicate customers by email';
SELECT Email, STRING_AGG(CONCAT(CustomerId, '/', CustomerCode), ', ') FROM EvalCommerce.dbo.Customers GROUP BY Email HAVING COUNT(*) > 1;

/* =====================================================================================
   Dotnet Report - Multi-tenant / row-level security demo database
   -------------------------------------------------------------------------------------
   Creates the MultiTenantDemo database with:

     app.Roles, app.Users, app.UserRoles, app.UserTenants   -> who can see what (NOT for reporting)
     dbo.Tenants, dbo.Customers, dbo.Products,
     dbo.Orders, dbo.OrderItems                              -> business data to report on
     dbo.SalesReps, dbo.OrderDetails (views)                 -> report-friendly views

   Every reportable table carries TenantId, and the sales tables also carry SalesRepId.
   The demo app turns the signed-in user into Dotnet Report DataFilters such as

       { "TenantId": "1,2", "SalesRepId": "11,12,13,14,15" }

   and the reporting engine appends  AND [Table].[TenantId] IN (1,2) AND [Table].[SalesRepId] IN (...)
   to every query on a table that has those columns.

   Safe to re-run: drops and recreates the demo objects (not the database).
   Run with:  sqlcmd -S .\SQLEXPRESS -E -C -i CreateMultiTenantDemoDb.sql
   All demo users have the password  Demo123!
   ===================================================================================== */

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;   -- required for the persisted computed column (sqlcmd defaults it OFF)
SET ANSI_NULLS ON;
GO

IF DB_ID(N'MultiTenantDemo') IS NULL
    CREATE DATABASE MultiTenantDemo;
GO

USE MultiTenantDemo;
GO

/* ---------- drop in dependency order ---------- */
DROP VIEW  IF EXISTS dbo.OrderDetails;
DROP VIEW  IF EXISTS dbo.SalesReps;
DROP TABLE IF EXISTS dbo.OrderItems;
DROP TABLE IF EXISTS dbo.Orders;
DROP TABLE IF EXISTS dbo.Customers;
DROP TABLE IF EXISTS dbo.Products;
DROP TABLE IF EXISTS app.UserTenants;
DROP TABLE IF EXISTS app.UserRoles;
DROP TABLE IF EXISTS app.Users;
DROP TABLE IF EXISTS app.Roles;
DROP TABLE IF EXISTS dbo.Tenants;
GO

IF SCHEMA_ID(N'app') IS NULL EXEC(N'CREATE SCHEMA app');
GO

/* =====================================================================================
   Tenants
   ===================================================================================== */
CREATE TABLE dbo.Tenants
(
    TenantId    int           NOT NULL CONSTRAINT PK_Tenants PRIMARY KEY,
    TenantCode  nvarchar(20)  NOT NULL CONSTRAINT UQ_Tenants_Code UNIQUE,   -- used as Dotnet Report ClientId
    TenantName  nvarchar(100) NOT NULL,
    Industry    nvarchar(50)  NOT NULL,
    Region      nvarchar(50)  NOT NULL,
    PlanTier    nvarchar(20)  NOT NULL,
    CreatedOn   date          NOT NULL
);

INSERT dbo.Tenants (TenantId, TenantCode, TenantName, Industry, Region, PlanTier, CreatedOn) VALUES
 (1, N'ACME',     N'Acme Industrial Supply', N'Industrial Tools',  N'North America', N'Enterprise',   '2023-02-01'),
 (2, N'GLOBEX',   N'Globex Electronics',     N'Electronics',       N'North America', N'Professional', '2023-06-15'),
 (3, N'INITECH',  N'Initech Software',       N'Software',          N'Europe',        N'Professional', '2024-01-10'),
 (4, N'CONTOSO',  N'Contoso Coffee Co.',     N'Food & Beverage',   N'Europe',        N'Starter',      '2024-04-22'),
 (5, N'FABRIKAM', N'Fabrikam Apparel',       N'Apparel',           N'Asia Pacific',  N'Starter',      '2024-09-05');

/* =====================================================================================
   Roles - DataScope drives the row-level security the app builds for each user:
     AllTenants      every tenant, every row
     AssignedTenants every row of the tenants in app.UserTenants
     Team            assigned tenants, only rows owned by the user and everyone below them
     Self            assigned tenants, only rows the user owns
   ===================================================================================== */
CREATE TABLE app.Roles
(
    RoleId          int           NOT NULL CONSTRAINT PK_Roles PRIMARY KEY,
    RoleName        nvarchar(50)  NOT NULL CONSTRAINT UQ_Roles_Name UNIQUE,
    Description     nvarchar(200) NOT NULL,
    DataScope       nvarchar(20)  NOT NULL CONSTRAINT CK_Roles_DataScope CHECK (DataScope IN (N'AllTenants', N'AssignedTenants', N'Team', N'Self')),
    CanUseAdminMode bit           NOT NULL,   -- Dotnet Report admin mode (manage every report in the tenant)
    CanAccessSetup  bit           NOT NULL,   -- /DotnetSetup (schema, connections)
    CanImpersonate  bit           NOT NULL
);

INSERT app.Roles (RoleId, RoleName, Description, DataScope, CanUseAdminMode, CanAccessSetup, CanImpersonate) VALUES
 (1, N'PlatformAdmin',    N'SaaS operator. Sees every tenant, manages schema and shared reports.',              N'AllTenants',      1, 1, 1),
 (2, N'SupportAnalyst',   N'Read-only support across the tenants they are assigned to.',                        N'AssignedTenants', 0, 0, 0),
 (3, N'TenantAdmin',      N'Customer administrator. Sees all data of their tenant(s) and manages its reports.', N'AssignedTenants', 1, 0, 1),
 (4, N'RegionalDirector', N'Leads sales managers, possibly across several tenants. Sees their whole org tree.', N'Team',            0, 0, 1),
 (5, N'SalesManager',     N'Leads a sales team. Sees their own and their team''s accounts and orders.',         N'Team',            0, 0, 1),
 (6, N'SalesRep',         N'Individual contributor. Sees only the accounts and orders they own.',               N'Self',            0, 0, 0);

/* =====================================================================================
   Users - ManagerUserId builds the reporting hierarchy (can cross tenants)
   ===================================================================================== */
CREATE TABLE app.Users
(
    UserId        int            NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
    Email         nvarchar(200)  NOT NULL CONSTRAINT UQ_Users_Email UNIQUE,
    FullName      nvarchar(100)  NOT NULL,
    Title         nvarchar(100)  NOT NULL,
    HomeTenantId  int            NULL CONSTRAINT FK_Users_Tenant  REFERENCES dbo.Tenants (TenantId),
    ManagerUserId int            NULL CONSTRAINT FK_Users_Manager REFERENCES app.Users (UserId),
    PasswordHash  varbinary(32)  NOT NULL,   -- demo only: SHA2-256 of the password. Use a real identity provider in production.
    IsActive      bit            NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT (1)
);

CREATE TABLE app.UserRoles
(
    UserId int NOT NULL CONSTRAINT FK_UserRoles_User REFERENCES app.Users (UserId),
    RoleId int NOT NULL CONSTRAINT FK_UserRoles_Role REFERENCES app.Roles (RoleId),
    CONSTRAINT PK_UserRoles PRIMARY KEY (UserId, RoleId)
);

-- Which tenants a user may see. A user can belong to several tenants.
CREATE TABLE app.UserTenants
(
    UserId   int NOT NULL CONSTRAINT FK_UserTenants_User   REFERENCES app.Users (UserId),
    TenantId int NOT NULL CONSTRAINT FK_UserTenants_Tenant REFERENCES dbo.Tenants (TenantId),
    CONSTRAINT PK_UserTenants PRIMARY KEY (UserId, TenantId)
);
GO

DECLARE @pw varbinary(32) = HASHBYTES('SHA2_256', CAST(N'Demo123!' AS nvarchar(100)));

-- Insert managers before the people who report to them.
INSERT app.Users (UserId, Email, FullName, Title, HomeTenantId, ManagerUserId, PasswordHash) VALUES
 -- Platform (the SaaS company itself)
 ( 1, N'sarah.chen@dotnetreport.demo',  N'Sarah Chen',     N'Platform Administrator',      NULL, NULL, @pw),
 ( 2, N'mike.rivera@dotnetreport.demo', N'Mike Rivera',    N'Customer Support Analyst',    NULL, 1,    @pw),
 -- Linda runs sales for two sister companies (Acme + Globex)
 ( 3, N'linda.park@acme.demo',          N'Linda Park',     N'VP Sales, North America',     1,    NULL, @pw),

 -- ACME
 (10, N'alice.morgan@acme.demo',        N'Alice Morgan',   N'IT Administrator',            1,    NULL, @pw),
 (11, N'tom.baker@acme.demo',           N'Tom Baker',      N'Sales Director',              1,    3,    @pw),
 (12, N'rachel.kim@acme.demo',          N'Rachel Kim',     N'Sales Team Lead',             1,    11,   @pw),
 (13, N'jake.wilson@acme.demo',         N'Jake Wilson',    N'Account Executive',           1,    12,   @pw),
 (14, N'emma.davis@acme.demo',          N'Emma Davis',     N'Account Executive',           1,    12,   @pw),
 (15, N'liam.brown@acme.demo',          N'Liam Brown',     N'Account Executive',           1,    11,   @pw),

 -- GLOBEX
 (20, N'ben.foster@globex.demo',        N'Ben Foster',     N'Operations Administrator',    2,    NULL, @pw),
 (21, N'grace.lee@globex.demo',         N'Grace Lee',      N'Sales Manager',               2,    3,    @pw),
 (22, N'noah.patel@globex.demo',        N'Noah Patel',     N'Sales Representative',        2,    21,   @pw),
 (23, N'olivia.garcia@globex.demo',     N'Olivia Garcia',  N'Sales Representative',        2,    21,   @pw),
 (24, N'ava.thompson@globex.demo',      N'Ava Thompson',   N'Sales Representative',        2,    21,   @pw),

 -- INITECH
 (30, N'priya.sharma@initech.demo',     N'Priya Sharma',   N'Finance Administrator',       3,    NULL, @pw),
 (31, N'samuel.okafor@initech.demo',    N'Samuel Okafor',  N'Head of Sales',               3,    NULL, @pw),
 (32, N'lukas.meyer@initech.demo',      N'Lukas Meyer',    N'Account Manager',             3,    31,   @pw),
 (33, N'sofia.rossi@initech.demo',      N'Sofia Rossi',    N'Account Manager',             3,    31,   @pw),

 -- CONTOSO + FABRIKAM share an outsourced administrator (Karen)
 (40, N'karen.walsh@contoso.demo',      N'Karen Walsh',    N'Shared Services Administrator', 4,  NULL, @pw),
 (41, N'daniel.nguyen@contoso.demo',    N'Daniel Nguyen',  N'Sales Manager',               4,    NULL, @pw),
 (42, N'zoe.martin@contoso.demo',       N'Zoe Martin',     N'Sales Representative',        4,    41,   @pw),
 (43, N'ethan.clark@contoso.demo',      N'Ethan Clark',    N'Sales Representative',        4,    41,   @pw),

 -- FABRIKAM
 (50, N'omar.haddad@fabrikam.demo',     N'Omar Haddad',    N'Sales Manager',               5,    NULL, @pw),
 (51, N'mia.tanaka@fabrikam.demo',      N'Mia Tanaka',     N'Sales Representative',        5,    50,   @pw),
 (52, N'chloe.dubois@fabrikam.demo',    N'Chloe Dubois',   N'Sales Representative',        5,    50,   @pw),
 (53, N'ryan.oconnor@fabrikam.demo',    N'Ryan O''Connor', N'Sales Representative',        5,    50,   @pw);

INSERT app.UserRoles (UserId, RoleId) VALUES
 ( 1, 1),
 ( 2, 2),
 ( 3, 4),
 (10, 3), (11, 5), (12, 5), (13, 6), (14, 6), (15, 6),
 (20, 3), (21, 5), (22, 6), (23, 6), (24, 6),
 (30, 3), (31, 5), (32, 6), (33, 6),
 (40, 3), (41, 5), (42, 6), (43, 6),
 (50, 5), (51, 6), (52, 6), (53, 6);

INSERT app.UserTenants (UserId, TenantId) VALUES
 -- Sarah is PlatformAdmin (AllTenants) so needs no rows here; listed for clarity
 ( 1, 1), ( 1, 2), ( 1, 3), ( 1, 4), ( 1, 5),
 -- Mike supports three customers
 ( 2, 1), ( 2, 2), ( 2, 3),
 -- Linda spans the two sister companies
 ( 3, 1), ( 3, 2),
 (10, 1), (11, 1), (12, 1), (13, 1), (14, 1), (15, 1),
 (20, 2), (21, 2), (22, 2), (23, 2), (24, 2),
 (30, 3), (31, 3), (32, 3), (33, 3),
 -- Karen administers both Contoso and Fabrikam
 (40, 4), (40, 5),
 (41, 4), (42, 4), (43, 4),
 (50, 5), (51, 5), (52, 5), (53, 5);
GO

/* =====================================================================================
   Business data - every table has TenantId; sales tables have SalesRepId
   ===================================================================================== */
CREATE TABLE dbo.Products
(
    ProductId   int            NOT NULL CONSTRAINT PK_Products PRIMARY KEY,
    TenantId    int            NOT NULL CONSTRAINT FK_Products_Tenant REFERENCES dbo.Tenants (TenantId),
    ProductName nvarchar(100)  NOT NULL,
    Category    nvarchar(50)   NOT NULL,
    UnitPrice   decimal(10, 2) NOT NULL
);

CREATE TABLE dbo.Customers
(
    CustomerId   int           NOT NULL CONSTRAINT PK_Customers PRIMARY KEY,
    TenantId     int           NOT NULL CONSTRAINT FK_Customers_Tenant REFERENCES dbo.Tenants (TenantId),
    SalesRepId   int           NOT NULL CONSTRAINT FK_Customers_SalesRep REFERENCES app.Users (UserId),  -- account owner
    CustomerName nvarchar(100) NOT NULL,
    ContactName  nvarchar(100) NOT NULL,
    City         nvarchar(50)  NOT NULL,
    Country      nvarchar(50)  NOT NULL,
    Segment      nvarchar(20)  NOT NULL,
    CreatedOn    date          NOT NULL
);

CREATE TABLE dbo.Orders
(
    OrderId     int            NOT NULL CONSTRAINT PK_Orders PRIMARY KEY,
    TenantId    int            NOT NULL CONSTRAINT FK_Orders_Tenant   REFERENCES dbo.Tenants (TenantId),
    CustomerId  int            NOT NULL CONSTRAINT FK_Orders_Customer REFERENCES dbo.Customers (CustomerId),
    SalesRepId  int            NOT NULL CONSTRAINT FK_Orders_SalesRep REFERENCES app.Users (UserId),
    OrderDate   date           NOT NULL,
    ShipDate    date           NULL,
    Status      nvarchar(20)   NOT NULL,
    Channel     nvarchar(20)   NOT NULL,
    Freight     decimal(10, 2) NOT NULL
);

CREATE TABLE dbo.OrderItems
(
    OrderItemId int            NOT NULL CONSTRAINT PK_OrderItems PRIMARY KEY,
    TenantId    int            NOT NULL CONSTRAINT FK_OrderItems_Tenant  REFERENCES dbo.Tenants (TenantId),
    OrderId     int            NOT NULL CONSTRAINT FK_OrderItems_Order   REFERENCES dbo.Orders (OrderId),
    ProductId   int            NOT NULL CONSTRAINT FK_OrderItems_Product REFERENCES dbo.Products (ProductId),
    SalesRepId  int            NOT NULL CONSTRAINT FK_OrderItems_SalesRep REFERENCES app.Users (UserId),  -- denormalised so line-level reports are filtered too
    Quantity    int            NOT NULL,
    UnitPrice   decimal(10, 2) NOT NULL,
    Discount    decimal(4, 2)  NOT NULL,
    LineTotal   AS CAST(Quantity * UnitPrice * (1 - Discount) AS decimal(12, 2)) PERSISTED
);

CREATE INDEX IX_Customers_Tenant_Rep  ON dbo.Customers  (TenantId, SalesRepId);
CREATE INDEX IX_Orders_Tenant_Rep     ON dbo.Orders     (TenantId, SalesRepId) INCLUDE (OrderDate, Status);
CREATE INDEX IX_OrderItems_Tenant_Rep ON dbo.OrderItems (TenantId, SalesRepId);
CREATE INDEX IX_OrderItems_Order      ON dbo.OrderItems (OrderId);
GO

/* ---------- Products: 6 per tenant, each tenant sells something different ---------- */
INSERT dbo.Products (ProductId, TenantId, ProductName, Category, UnitPrice) VALUES
 (101, 1, N'Cordless Drill Kit',          N'Power Tools',    189.00),
 (102, 1, N'Industrial Angle Grinder',    N'Power Tools',    149.50),
 (103, 1, N'Steel Tool Chest',            N'Storage',        429.00),
 (104, 1, N'Safety Harness',              N'Safety',          89.99),
 (105, 1, N'Hydraulic Jack 3T',           N'Lifting',        259.00),
 (106, 1, N'Welding Helmet',              N'Safety',         119.00),
 (201, 2, N'4K Monitor 27"',              N'Displays',       329.00),
 (202, 2, N'Mechanical Keyboard',         N'Peripherals',    119.00),
 (203, 2, N'Noise-Cancelling Headset',    N'Audio',          199.00),
 (204, 2, N'USB-C Docking Station',       N'Peripherals',    179.00),
 (205, 2, N'Mesh Wi-Fi Router',           N'Networking',     249.00),
 (206, 2, N'Portable SSD 2TB',            N'Storage',        159.00),
 (301, 3, N'TPS Reporting Suite',         N'Licenses',      1200.00),
 (302, 3, N'Workflow Automation',         N'Licenses',       850.00),
 (303, 3, N'Premium Support Plan',        N'Services',       400.00),
 (304, 3, N'Implementation Package',      N'Services',      2500.00),
 (305, 3, N'Analytics Add-on',            N'Licenses',       600.00),
 (306, 3, N'Training Day',                N'Services',       950.00),
 (401, 4, N'Espresso Beans 5kg',          N'Coffee',          95.00),
 (402, 4, N'Single Origin Beans 1kg',     N'Coffee',          32.00),
 (403, 4, N'Oat Milk Case',               N'Dairy Alt',       28.50),
 (404, 4, N'Paper Cups (1000)',           N'Supplies',        45.00),
 (405, 4, N'Grinder Service',             N'Services',       120.00),
 (406, 4, N'Syrup Variety Pack',          N'Flavors',         39.00),
 (501, 5, N'Organic Cotton Tee',          N'Tops',            14.00),
 (502, 5, N'Denim Jacket',                N'Outerwear',       58.00),
 (503, 5, N'Linen Shirt',                 N'Tops',            32.00),
 (504, 5, N'Chino Trousers',              N'Bottoms',         38.00),
 (505, 5, N'Merino Sweater',              N'Knitwear',        64.00),
 (506, 5, N'Canvas Tote',                 N'Accessories',      9.50);
GO

/* ---------- Customers: 12 per tenant, owned by the tenant's sellers ---------- */
DECLARE @Sellers TABLE (TenantId int, Seq int, UserId int);
INSERT @Sellers (TenantId, Seq, UserId) VALUES
 -- ACME: Rachel (team lead) and Tom (director) own a few accounts themselves
 (1, 0, 13), (1, 1, 14), (1, 2, 15), (1, 3, 13), (1, 4, 14), (1, 5, 12), (1, 6, 15), (1, 7, 11),
 (2, 0, 22), (2, 1, 23), (2, 2, 24), (2, 3, 22), (2, 4, 23), (2, 5, 21),
 (3, 0, 32), (3, 1, 33), (3, 2, 31),
 (4, 0, 42), (4, 1, 43), (4, 2, 41),
 (5, 0, 51), (5, 1, 52), (5, 2, 53), (5, 3, 50);

DECLARE @Stems TABLE (Seq int, Stem nvarchar(30), Contact nvarchar(60));
INSERT @Stems (Seq, Stem, Contact) VALUES
 (0, N'Summit', N'Laura Bennett'),   (1, N'Harbor', N'James Ortiz'),      (2, N'Pinecrest', N'Nina Kowalski'),
 (3, N'Redwood', N'Carlos Mendes'),  (4, N'Lakeside', N'Helen Brooks'),   (5, N'Ironbridge', N'Victor Hale'),
 (6, N'Bluewater', N'Aisha Khan'),   (7, N'Northstar', N'Peter Lindqvist'),(8, N'Crescent', N'Maria Santos'),
 (9, N'Oakridge', N'George Adams'),  (10, N'Silverline', N'Yuki Sato'),   (11, N'Granite', N'Owen Fischer'),
 (12, N'Riverbend', N'Ingrid Berg'), (13, N'Maple', N'Tariq Aziz'),       (14, N'Falcon', N'Rosa Delgado'),
 (15, N'Evergreen', N'Dmitri Volkov'),(16, N'Horizon', N'Claire Dupont'), (17, N'Cedar', N'Kwame Mensah'),
 (18, N'Keystone', N'Fiona Walsh'),  (19, N'Sterling', N'Hugo Alvarez');

DECLARE @Places TABLE (Region nvarchar(50), Seq int, City nvarchar(50), Country nvarchar(50));
INSERT @Places (Region, Seq, City, Country) VALUES
 (N'North America', 0, N'Chicago', N'USA'),  (N'North America', 1, N'Dallas', N'USA'),   (N'North America', 2, N'Toronto', N'Canada'),
 (N'North America', 3, N'Denver', N'USA'),   (N'North America', 4, N'Atlanta', N'USA'),  (N'North America', 5, N'Vancouver', N'Canada'),
 (N'Europe', 0, N'London', N'UK'),           (N'Europe', 1, N'Berlin', N'Germany'),      (N'Europe', 2, N'Paris', N'France'),
 (N'Europe', 3, N'Amsterdam', N'Netherlands'),(N'Europe', 4, N'Madrid', N'Spain'),       (N'Europe', 5, N'Dublin', N'Ireland'),
 (N'Asia Pacific', 0, N'Sydney', N'Australia'),(N'Asia Pacific', 1, N'Singapore', N'Singapore'),(N'Asia Pacific', 2, N'Tokyo', N'Japan'),
 (N'Asia Pacific', 3, N'Auckland', N'New Zealand'),(N'Asia Pacific', 4, N'Seoul', N'South Korea'),(N'Asia Pacific', 5, N'Melbourne', N'Australia');

;WITH n AS (SELECT TOP (12) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS k FROM sys.all_objects)
INSERT dbo.Customers (CustomerId, TenantId, SalesRepId, CustomerName, ContactName, City, Country, Segment, CreatedOn)
SELECT  t.TenantId * 1000 + n.k + 1,
        t.TenantId,
        s.UserId,
        st.Stem + N' ' + CASE t.TenantId WHEN 1 THEN N'Construction' WHEN 2 THEN N'Electronics Retail'
                                         WHEN 3 THEN N'Systems'      WHEN 4 THEN N'Cafe' ELSE N'Boutique' END,
        st.Contact,
        p.City,
        p.Country,
        CASE n.k % 3 WHEN 0 THEN N'Enterprise' WHEN 1 THEN N'Mid-Market' ELSE N'Small Business' END,
        DATEADD(day, (n.k * 23 + t.TenantId * 11) % 400, t.CreatedOn)
FROM dbo.Tenants t
CROSS JOIN n
JOIN @Sellers s  ON s.TenantId = t.TenantId
                AND s.Seq = n.k % (SELECT COUNT(*) FROM @Sellers x WHERE x.TenantId = t.TenantId)
JOIN @Stems st   ON st.Seq = (n.k + t.TenantId * 4) % 20
JOIN @Places p   ON p.Region = t.Region AND p.Seq = n.k % 6;
GO

/* ---------- Orders: 220 per tenant, Jan 2025 - Sep 2026, deterministic pseudo-random ---------- */
;WITH n AS (SELECT TOP (220) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects a CROSS JOIN sys.all_objects b),
src AS
(
    SELECT  t.TenantId, n.i,
            (CAST(HASHBYTES('MD5', CONCAT(N'o', t.TenantId, N'-', n.i)) AS bigint) & 2147483647)      AS h1,
            (CAST(HASHBYTES('MD5', CONCAT(N'd', t.TenantId, N'-', n.i, N'x')) AS bigint) & 2147483647) AS h2
    FROM dbo.Tenants t CROSS JOIN n
),
o AS
(
    SELECT  s.TenantId,
            s.TenantId * 100000 + s.i                         AS OrderId,
            s.TenantId * 1000 + (s.h1 % 12) + 1               AS CustomerId,
            DATEADD(day, s.h2 % 630, CAST('2025-01-01' AS date)) AS OrderDate,
            s.h1, s.h2
    FROM src s
)
INSERT dbo.Orders (OrderId, TenantId, CustomerId, SalesRepId, OrderDate, ShipDate, Status, Channel, Freight)
SELECT  o.OrderId, o.TenantId, o.CustomerId, c.SalesRepId, o.OrderDate,
        CASE WHEN st.Status IN (N'Shipped', N'Delivered') THEN DATEADD(day, 1 + o.h1 % 6, o.OrderDate) END,
        st.Status,
        CASE o.h2 % 4 WHEN 0 THEN N'Online' WHEN 1 THEN N'Phone' WHEN 2 THEN N'Partner' ELSE N'Direct' END,
        CAST(5 + (o.h1 % 9500) / 100.0 AS decimal(10, 2))
FROM o
JOIN dbo.Customers c ON c.CustomerId = o.CustomerId
CROSS APPLY (SELECT CASE
        WHEN o.OrderDate >= '2026-09-01' THEN CASE o.h1 % 3 WHEN 0 THEN N'Pending' WHEN 1 THEN N'Processing' ELSE N'Shipped' END
        WHEN o.h1 % 17 = 0 THEN N'Cancelled'
        WHEN o.h1 % 11 = 0 THEN N'Returned'
        ELSE N'Delivered' END AS Status) st;
GO

/* ---------- Order items: 1-4 lines per order ---------- */
;WITH lines AS (SELECT v.l FROM (VALUES (1), (2), (3), (4)) v(l)),
x AS
(
    SELECT  o.OrderId, o.TenantId, o.SalesRepId, l.l,
            (CAST(HASHBYTES('MD5', CONCAT(N'i', o.OrderId, N'-', l.l)) AS bigint) & 2147483647) AS h
    FROM dbo.Orders o
    JOIN lines l ON l.l <= 1 + (CAST(HASHBYTES('MD5', CONCAT(N'c', o.OrderId)) AS bigint) & 2147483647) % 4
)
INSERT dbo.OrderItems (OrderItemId, TenantId, OrderId, ProductId, SalesRepId, Quantity, UnitPrice, Discount)
SELECT  x.OrderId * 10 + x.l,
        x.TenantId,
        x.OrderId,
        p.ProductId,
        x.SalesRepId,
        1 + x.h % CASE WHEN p.UnitPrice > 500 THEN 5 ELSE 40 END,
        p.UnitPrice,
        CASE x.h % 10 WHEN 0 THEN 0.15 WHEN 1 THEN 0.10 WHEN 2 THEN 0.05 ELSE 0 END
FROM x
-- each line picks a distinct product: products are TenantId*100 + 1..6
JOIN dbo.Products p ON p.ProductId = x.TenantId * 100 + 1 + ((x.OrderId + x.l) % 6);
GO

/* =====================================================================================
   Report-friendly views. They keep TenantId and SalesRepId so DataFilters still apply.
   ===================================================================================== */
CREATE VIEW dbo.SalesReps
AS
SELECT  u.UserId        AS SalesRepId,
        u.HomeTenantId  AS TenantId,
        u.FullName      AS SalesRepName,
        u.Title,
        u.Email,
        m.FullName      AS ManagerName
FROM app.Users u
LEFT JOIN app.Users m ON m.UserId = u.ManagerUserId
WHERE u.HomeTenantId IS NOT NULL;
GO

CREATE VIEW dbo.OrderDetails
AS
SELECT  oi.OrderItemId,
        o.OrderId,
        o.TenantId,
        t.TenantName,
        o.SalesRepId,
        r.FullName      AS SalesRepName,
        m.FullName      AS ManagerName,
        c.CustomerId,
        c.CustomerName,
        c.Segment,
        c.City,
        c.Country,
        o.OrderDate,
        o.Status,
        o.Channel,
        p.ProductName,
        p.Category,
        oi.Quantity,
        oi.UnitPrice,
        oi.Discount,
        oi.LineTotal
FROM dbo.OrderItems oi
JOIN dbo.Orders    o ON o.OrderId    = oi.OrderId
JOIN dbo.Tenants   t ON t.TenantId   = o.TenantId
JOIN dbo.Customers c ON c.CustomerId = o.CustomerId
JOIN dbo.Products  p ON p.ProductId  = oi.ProductId
JOIN app.Users     r ON r.UserId     = o.SalesRepId
LEFT JOIN app.Users m ON m.UserId    = r.ManagerUserId;
GO

/* ---------- summary ---------- */
SELECT t.TenantCode,
       (SELECT COUNT(*) FROM app.UserTenants ut WHERE ut.TenantId = t.TenantId) AS Users,
       (SELECT COUNT(*) FROM dbo.Customers c WHERE c.TenantId = t.TenantId)     AS Customers,
       (SELECT COUNT(*) FROM dbo.Orders o WHERE o.TenantId = t.TenantId)        AS Orders,
       (SELECT SUM(LineTotal) FROM dbo.OrderItems i WHERE i.TenantId = t.TenantId) AS Revenue
FROM dbo.Tenants t
ORDER BY t.TenantId;
GO

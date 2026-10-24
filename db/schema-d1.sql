PRAGMA foreign_keys=OFF;
CREATE TABLE IF NOT EXISTS "AccountClass" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AccountClass" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NULL,
    "NormalBalance" TEXT NULL
);
INSERT INTO AccountClass VALUES(1,'Assets','Dr');
INSERT INTO AccountClass VALUES(2,'Liabilities','Cr');
INSERT INTO AccountClass VALUES(3,'Equity','Cr');
INSERT INTO AccountClass VALUES(4,'Revenue','Cr');
INSERT INTO AccountClass VALUES(5,'Expense','Dr');
INSERT INTO AccountClass VALUES(6,'Temporary','NA');
CREATE TABLE IF NOT EXISTS "Address" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Address" PRIMARY KEY AUTOINCREMENT,
    "No" TEXT NULL,
    "Street" TEXT NULL,
    "City" TEXT NULL
);
CREATE TABLE IF NOT EXISTS "AuditableEntity" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AuditableEntity" PRIMARY KEY AUTOINCREMENT,
    "EntityName" TEXT NULL,
    "EnableAudit" INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS "AuditLog" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AuditLog" PRIMARY KEY AUTOINCREMENT,
    "UserName" TEXT NULL,
    "AuditEventDateUTC" TEXT NOT NULL,
    "AuditEventType" INTEGER NOT NULL,
    "TableName" TEXT NULL,
    "RecordId" TEXT NULL,
    "FieldName" TEXT NULL,
    "OriginalValue" TEXT NULL,
    "NewValue" TEXT NULL
);
CREATE TABLE IF NOT EXISTS "Company" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Company" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NULL,
    "ShortName" TEXT NULL,
    "CompanyCode" TEXT NULL,
    "CRA" TEXT NULL
, Plan TEXT DEFAULT 'accounting', ActiveModulesJson TEXT, SubscriptionStatus TEXT DEFAULT 'active', SubscriptionValidUntil TEXT);
INSERT INTO Company VALUES(1,'Financial Solutions Inc.','FSI','100','012345678','accounting',NULL,'active',NULL);
CREATE TABLE IF NOT EXISTS "Donors" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Donors" PRIMARY KEY AUTOINCREMENT,
    "DonorType" TEXT NOT NULL,
    "FirstName" TEXT NULL,
    "LastName" TEXT NULL,
    "CompanyName" TEXT NULL,
    "Street" TEXT NOT NULL,
    "Province" TEXT NOT NULL,
    "PostalCode" TEXT NOT NULL,
    "Telephone" TEXT NOT NULL,
    "Email" TEXT NOT NULL
);
INSERT INTO Donors VALUES(1,'Individual','Olivia','Bennett','','125 Maple Street','ON','K1A 0B1','613-555-0101','olivia.bennett@example.com');
INSERT INTO Donors VALUES(2,'Company','','','Cedar Grove Community Services','480 Cedar Avenue','BC','V6B 1A1','604-555-0102','giving@cedargrove.example.com');
CREATE TABLE IF NOT EXISTS "FinancialYear" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_FinancialYear" PRIMARY KEY AUTOINCREMENT,
    "FiscalYearCode" TEXT NOT NULL,
    "FiscalYearName" TEXT NOT NULL,
    "StartDate" TEXT NOT NULL,
    "EndDate" TEXT NOT NULL,
    "IsActive" INTEGER NOT NULL
);
INSERT INTO FinancialYear VALUES(1,'FY1819','FY 2018/2019','2018-01-01 00:00:00','2018-12-31 00:00:00',1);
CREATE TABLE IF NOT EXISTS "GeneralLedgerHeader" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_GeneralLedgerHeader" PRIMARY KEY AUTOINCREMENT,
    "Date" TEXT NOT NULL,
    "DocumentType" INTEGER NOT NULL,
    "Description" TEXT NULL
);
INSERT INTO GeneralLedgerHeader VALUES(1,'2026-02-05 00:00:00',4,'');
INSERT INTO GeneralLedgerHeader VALUES(2,'2026-03-10 00:00:00',4,'');
INSERT INTO GeneralLedgerHeader VALUES(3,'2026-02-19 00:00:00',5,'');
INSERT INTO GeneralLedgerHeader VALUES(4,'2026-02-03 00:00:00',10,'Dashboard demo purchase invoice');
INSERT INTO GeneralLedgerHeader VALUES(5,'2026-04-04 00:00:00',10,'Dashboard demo purchase invoice');
INSERT INTO GeneralLedgerHeader VALUES(6,'2026-04-16 00:00:00',13,'');
CREATE TABLE IF NOT EXISTS "ItemTaxGroup" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ItemTaxGroup" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NULL,
    "IsFullyExempt" INTEGER NOT NULL
);
INSERT INTO ItemTaxGroup VALUES(1,'Preferenced',0);
INSERT INTO ItemTaxGroup VALUES(2,'Regular',0);
CREATE TABLE IF NOT EXISTS "Log" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Log" PRIMARY KEY AUTOINCREMENT,
    "TimeStamp" TEXT NOT NULL,
    "Level" TEXT NULL,
    "Logger" TEXT NULL,
    "Message" TEXT NULL,
    "Username" TEXT NULL,
    "CallSite" TEXT NULL,
    "Thread" TEXT NULL,
    "Exception" TEXT NULL,
    "StackTrace" TEXT NULL
);
CREATE TABLE IF NOT EXISTS "Measurement" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Measurement" PRIMARY KEY AUTOINCREMENT,
    "Code" TEXT NULL,
    "Description" TEXT NULL
);
INSERT INTO Measurement VALUES(1,'EA','Each');
INSERT INTO Measurement VALUES(2,'PK','Pack');
INSERT INTO Measurement VALUES(3,'MO','Monthly');
INSERT INTO Measurement VALUES(4,'HR','Hour');
CREATE TABLE IF NOT EXISTS "Party" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Party" PRIMARY KEY AUTOINCREMENT,
    "PartyType" INTEGER NOT NULL,
    "Name" TEXT NULL,
    "Email" TEXT NULL,
    "Website" TEXT NULL,
    "Phone" TEXT NULL,
    "Fax" TEXT NULL,
    "IsActive" INTEGER NOT NULL,
    "CompanyStreet" TEXT NULL,
    "CompanyProvince" TEXT NULL,
    "CompanyPostalCode" TEXT NULL,
    "CompanyCountry" TEXT NULL,
    "ShippingStreet" TEXT NULL,
    "ShippingProvince" TEXT NULL,
    "ShippingPostalCode" TEXT NULL,
    "ShippingCountry" TEXT NULL
);
INSERT INTO Party VALUES(1,2,'ABC Sample Supplier',NULL,NULL,NULL,NULL,1,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);
INSERT INTO Party VALUES(2,3,'Mary Walter',NULL,NULL,NULL,NULL,0,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);
INSERT INTO Party VALUES(3,1,'ABC Customer',NULL,NULL,NULL,NULL,1,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);
INSERT INTO Party VALUES(4,3,'John Doe',NULL,NULL,NULL,NULL,0,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);
INSERT INTO Party VALUES(5,1,'Northwind Books',NULL,NULL,NULL,NULL,1,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);
INSERT INTO Party VALUES(6,3,'Northwind Books Contact',NULL,NULL,NULL,NULL,0,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);
INSERT INTO Party VALUES(7,2,'Paper & Parcel Co.',NULL,NULL,NULL,NULL,1,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);
INSERT INTO Party VALUES(8,3,'Paper & Parcel Co. Contact',NULL,NULL,NULL,NULL,0,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);
CREATE TABLE IF NOT EXISTS "PaymentTerm" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_PaymentTerm" PRIMARY KEY AUTOINCREMENT,
    "Description" TEXT NULL,
    "PaymentType" INTEGER NOT NULL,
    "DueAfterDays" INTEGER NULL,
    "IsActive" INTEGER NOT NULL
);
INSERT INTO PaymentTerm VALUES(1,'Payment due within 10 days',3,10,1);
INSERT INTO PaymentTerm VALUES(2,'Payment due within 20 days',3,20,1);
INSERT INTO PaymentTerm VALUES(3,'Payment due within 30 days',3,30,1);
INSERT INTO PaymentTerm VALUES(4,'Payment due within 45 days',3,45,1);
INSERT INTO PaymentTerm VALUES(5,'Payment due within 60 days',3,60,1);
CREATE TABLE IF NOT EXISTS "SecurityGroup" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SecurityGroup" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NULL,
    "DisplayName" TEXT NULL
);
INSERT INTO SecurityGroup VALUES(1,'Sales','Sales & Invoicing');
INSERT INTO SecurityGroup VALUES(2,'Financials','General Ledger & Banking');
INSERT INTO SecurityGroup VALUES(3,'Purchasing','Purchases & Bills');
INSERT INTO SecurityGroup VALUES(4,'Inventory','Inventory & Items');
INSERT INTO SecurityGroup VALUES(5,'Reports','Reports & Analytics');
INSERT INTO SecurityGroup VALUES(6,'Donations','Donations & Non-Profit');
INSERT INTO SecurityGroup VALUES(7,'Audit','Audit & Compliance');
INSERT INTO SecurityGroup VALUES(8,'Admin','Administration');
CREATE TABLE IF NOT EXISTS "SecurityRole" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SecurityRole" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NULL,
    "DisplayName" TEXT NULL,
    "SysAdmin" INTEGER NOT NULL,
    "System" INTEGER NOT NULL
);
INSERT INTO SecurityRole VALUES(1,'SystemAdministrators','System Administrators',1,0);
INSERT INTO SecurityRole VALUES(2,'GeneralUsers',NULL,0,0);
CREATE TABLE IF NOT EXISTS "SequenceNumber" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SequenceNumber" PRIMARY KEY AUTOINCREMENT,
    "SequenceNumberType" INTEGER NOT NULL,
    "Description" TEXT NULL,
    "Prefix" TEXT NULL,
    "NextNumber" INTEGER NOT NULL,
    "UsePrefix" INTEGER NOT NULL
);
INSERT INTO SequenceNumber VALUES(1,13,'Vendor',NULL,3,0);
INSERT INTO SequenceNumber VALUES(2,12,'Customer',NULL,3,0);
INSERT INTO SequenceNumber VALUES(3,11,'Item',NULL,4,0);
INSERT INTO SequenceNumber VALUES(4,4,'SalesInvoice',NULL,3,0);
INSERT INTO SequenceNumber VALUES(5,3,'SalesDelivery',NULL,3,0);
INSERT INTO SequenceNumber VALUES(6,5,'SalesReceipt',NULL,2,0);
INSERT INTO SequenceNumber VALUES(7,6,'PurchaseOrder',NULL,3,0);
INSERT INTO SequenceNumber VALUES(8,8,'PurchaseInvoice',NULL,3,0);
INSERT INTO SequenceNumber VALUES(9,9,'VendorPayment',NULL,2,0);
CREATE TABLE IF NOT EXISTS "TaxGroup" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_TaxGroup" PRIMARY KEY AUTOINCREMENT,
    "Description" TEXT NULL,
    "TaxAppliedToShipping" INTEGER NOT NULL,
    "IsActive" INTEGER NOT NULL
);
INSERT INTO TaxGroup VALUES(1,'GST/HST',0,1);
INSERT INTO TaxGroup VALUES(2,'PST',0,1);
CREATE TABLE IF NOT EXISTS "User" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_User" PRIMARY KEY AUTOINCREMENT,
    "UserName" TEXT NULL,
    "EmailAddress" TEXT NULL,
    "Firstname" TEXT NULL,
    "Lastname" TEXT NULL
);
INSERT INTO User VALUES(1,'admin@accountgo.ph','admin@accountgo.ph','Admin','User');
CREATE TABLE IF NOT EXISTS "AuditableAttribute" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AuditableAttribute" PRIMARY KEY AUTOINCREMENT,
    "AuditableEntityId" INTEGER NOT NULL,
    "AttributeName" TEXT NULL,
    "EnableAudit" INTEGER NOT NULL,
    CONSTRAINT "FK_AuditableAttribute_AuditableEntity_AuditableEntityId" FOREIGN KEY ("AuditableEntityId") REFERENCES "AuditableEntity" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "Account" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Account" PRIMARY KEY AUTOINCREMENT,
    "AccountClassId" INTEGER NOT NULL,
    "ParentAccountId" INTEGER NULL,
    "DrOrCrSide" INTEGER NOT NULL,
    "CompanyId" INTEGER NOT NULL,
    "AccountCode" TEXT NOT NULL,
    "AccountName" TEXT NOT NULL,
    "Description" TEXT NULL,
    "IsCash" INTEGER NOT NULL,
    "IsContraAccount" INTEGER NOT NULL,
    "RowVersion" timestamp NULL,
    CONSTRAINT "FK_Account_AccountClass_AccountClassId" FOREIGN KEY ("AccountClassId") REFERENCES "AccountClass" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Account_Account_ParentAccountId" FOREIGN KEY ("ParentAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_Account_Company_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Company" ("Id") ON DELETE CASCADE
);
INSERT INTO Account VALUES(1,1,NULL,1,1,'10000','Assets',NULL,0,0,NULL);
INSERT INTO Account VALUES(2,1,1,1,1,'10100','Current Assets',NULL,0,0,NULL);
INSERT INTO Account VALUES(3,1,1,1,1,'10200','Noncurrent Assets',NULL,0,0,NULL);
INSERT INTO Account VALUES(4,1,1,1,1,'15000','Other Assets',NULL,0,0,NULL);
INSERT INTO Account VALUES(5,1,2,1,1,'10110','Cash in Hand and in Bank',NULL,1,0,NULL);
INSERT INTO Account VALUES(6,1,2,1,1,'10120','Accounts Receivable',NULL,0,0,NULL);
INSERT INTO Account VALUES(7,1,2,1,1,'10130','Other Receivables',NULL,0,0,NULL);
INSERT INTO Account VALUES(8,1,2,2,1,'10121','Allowance for Doubtful Accounts',NULL,0,1,NULL);
INSERT INTO Account VALUES(9,1,2,1,1,'10140','Prepaid Expenses',NULL,0,0,NULL);
INSERT INTO Account VALUES(10,1,2,1,1,'10150','Employee Advances',NULL,0,0,NULL);
INSERT INTO Account VALUES(11,1,2,1,1,'10700','Other Current Assets',NULL,0,0,NULL);
INSERT INTO Account VALUES(12,1,2,1,1,'10800','Inventory',NULL,0,0,NULL);
INSERT INTO Account VALUES(13,1,2,1,1,'10810','Goods Received Clearing Account',NULL,0,0,NULL);
INSERT INTO Account VALUES(14,1,2,1,1,'10900','Stocks of work in progress',NULL,0,0,NULL);
INSERT INTO Account VALUES(15,1,3,1,1,'10300','Fixed Assets',NULL,0,0,NULL);
INSERT INTO Account VALUES(16,1,5,1,1,'10111','Regular Checking Account',NULL,0,0,NULL);
INSERT INTO Account VALUES(17,1,5,1,1,'10112','Savings Account',NULL,0,0,NULL);
INSERT INTO Account VALUES(18,1,5,1,1,'10113','Cash in Hand A/C',NULL,0,0,NULL);
INSERT INTO Account VALUES(19,1,15,1,1,'13301','Furniture and Fixtures',NULL,0,0,NULL);
INSERT INTO Account VALUES(20,1,15,1,1,'13302','Plants & Equipments',NULL,0,0,NULL);
INSERT INTO Account VALUES(21,1,15,1,1,'13303','Vehicles',NULL,0,0,NULL);
INSERT INTO Account VALUES(22,1,15,1,1,'13304','Buildings',NULL,0,0,NULL);
INSERT INTO Account VALUES(23,1,15,1,1,'13305','Land',NULL,0,0,NULL);
INSERT INTO Account VALUES(24,1,15,2,1,'14000','Accumulated Depreciations',NULL,0,1,NULL);
INSERT INTO Account VALUES(25,1,15,2,1,'14001','Accumulated Depreciation-Furniture and Fixtures',NULL,0,1,NULL);
INSERT INTO Account VALUES(26,1,15,2,1,'14002','Accumulated Depreciation-Equipment',NULL,0,1,NULL);
INSERT INTO Account VALUES(27,1,15,2,1,'14003','Accumulated Depreciation-Vehicles',NULL,0,1,NULL);
INSERT INTO Account VALUES(28,1,15,2,1,'14004','Accumulated Depreciations-Building',NULL,0,1,NULL);
INSERT INTO Account VALUES(29,1,15,2,1,'14005','Accumulated Depreciation-Building Improvements',NULL,0,1,NULL);
INSERT INTO Account VALUES(30,2,NULL,2,1,'20000','Liabilities',NULL,0,0,NULL);
INSERT INTO Account VALUES(31,2,30,2,1,'20100','Current Liabilities',NULL,0,0,NULL);
INSERT INTO Account VALUES(32,2,30,2,1,'20500','Non-current Liabilities',NULL,0,0,NULL);
INSERT INTO Account VALUES(33,2,30,2,1,'20600','Other Long-term Liabilities',NULL,0,0,NULL);
INSERT INTO Account VALUES(34,2,31,2,1,'20110','Accounts Payable',NULL,0,0,NULL);
INSERT INTO Account VALUES(35,2,31,2,1,'20120','Customer Advances',NULL,0,0,NULL);
INSERT INTO Account VALUES(36,2,31,2,1,'20201','Accrued Expenses',NULL,0,0,NULL);
INSERT INTO Account VALUES(37,2,31,2,1,'20202','Wages Payable',NULL,0,0,NULL);
INSERT INTO Account VALUES(38,2,31,2,1,'20203','Deductions Payable',NULL,0,0,NULL);
INSERT INTO Account VALUES(39,2,31,2,1,'20204','Health Insurance Payable',NULL,0,0,NULL);
INSERT INTO Account VALUES(40,2,31,2,1,'20300','Sales Tax',NULL,0,0,NULL);
INSERT INTO Account VALUES(41,2,31,2,1,'20301','Local Payroll Taxes Payable',NULL,0,0,NULL);
INSERT INTO Account VALUES(42,2,31,2,1,'20302','Income Taxes Payable',NULL,0,0,NULL);
INSERT INTO Account VALUES(43,2,31,2,1,'20303','Other Taxes Payable',NULL,0,0,NULL);
INSERT INTO Account VALUES(44,2,31,2,1,'20304','Advances-H.O. Dues',NULL,0,0,NULL);
INSERT INTO Account VALUES(45,2,31,2,1,'20400','Other Current Liabilities',NULL,0,0,NULL);
INSERT INTO Account VALUES(46,3,NULL,2,1,'30000','Equity',NULL,0,0,NULL);
INSERT INTO Account VALUES(47,3,46,2,1,'30100','Member Capital',NULL,0,0,NULL);
INSERT INTO Account VALUES(48,3,46,2,1,'30200','Capital Surplus',NULL,0,0,NULL);
INSERT INTO Account VALUES(49,3,46,2,1,'30300','Retained Surplus',NULL,0,0,NULL);
INSERT INTO Account VALUES(50,3,46,2,1,'30400','Accumulated Profits',NULL,0,0,NULL);
INSERT INTO Account VALUES(51,3,46,2,1,'30500','Accumulated Losses',NULL,0,0,NULL);
INSERT INTO Account VALUES(52,3,46,2,1,'30600','Other Reserves',NULL,0,0,NULL);
INSERT INTO Account VALUES(53,4,NULL,2,1,'40000','Revenues',NULL,0,0,NULL);
INSERT INTO Account VALUES(54,4,53,2,1,'40100','Sales A/C',NULL,0,0,NULL);
INSERT INTO Account VALUES(55,4,53,2,1,'40200','Home Owners',NULL,0,0,NULL);
INSERT INTO Account VALUES(56,4,53,2,1,'40300','Interest Income',NULL,0,0,NULL);
INSERT INTO Account VALUES(57,4,53,2,1,'40400','Sales Discounts',NULL,0,0,NULL);
INSERT INTO Account VALUES(58,4,53,2,1,'40500','Shipping and Handling',NULL,0,0,NULL);
INSERT INTO Account VALUES(59,4,53,2,1,'40600','Other Income',NULL,0,0,NULL);
INSERT INTO Account VALUES(60,4,53,2,1,'41000','Donations',NULL,0,0,NULL);
INSERT INTO Account VALUES(61,4,60,2,1,'41001','General Purpose',NULL,0,0,NULL);
INSERT INTO Account VALUES(62,4,60,2,1,'41200','Sunday School',NULL,0,0,NULL);
INSERT INTO Account VALUES(63,4,60,2,1,'41250','Brothers In Christ',NULL,0,0,NULL);
INSERT INTO Account VALUES(64,5,NULL,1,1,'50000','Expenses',NULL,0,0,NULL);
INSERT INTO Account VALUES(65,5,64,1,1,'50100','General and Admin Expenses',NULL,0,0,NULL);
INSERT INTO Account VALUES(66,5,64,1,1,'50200','Purchase A/C',NULL,0,0,NULL);
INSERT INTO Account VALUES(67,5,64,1,1,'50300','Cost of Goods Sold',NULL,0,0,NULL);
INSERT INTO Account VALUES(68,5,64,1,1,'50400','Purchase Discounts',NULL,0,0,NULL);
INSERT INTO Account VALUES(69,5,64,1,1,'50500','Purchase price Variance',NULL,0,0,NULL);
INSERT INTO Account VALUES(70,5,64,1,1,'50600','Other Expenses',NULL,0,0,NULL);
INSERT INTO Account VALUES(71,5,64,1,1,'50700','Purchase Tax',NULL,0,0,NULL);
INSERT INTO Account VALUES(72,5,65,1,1,'50101','Salary Expenses',NULL,0,0,NULL);
INSERT INTO Account VALUES(73,5,65,1,1,'50102','Wages Expenses',NULL,0,0,NULL);
INSERT INTO Account VALUES(74,5,65,1,1,'50103','Employee Benefit Expenses',NULL,0,0,NULL);
INSERT INTO Account VALUES(75,5,65,1,1,'50104','Employee Benefit Expenses-Health Insurance',NULL,0,0,NULL);
INSERT INTO Account VALUES(76,5,65,1,1,'50105','Employee Benefit Expenses-Pension Plans',NULL,0,0,NULL);
INSERT INTO Account VALUES(77,5,65,1,1,'50106','Employee Benefit Expenses-Profit Sharing Plan',NULL,0,0,NULL);
INSERT INTO Account VALUES(78,5,65,1,1,'50107','Employee Benefit Expenses-Other',NULL,0,0,NULL);
INSERT INTO Account VALUES(79,5,65,1,1,'50108','Repair and Maintenance Expenses',NULL,0,0,NULL);
INSERT INTO Account VALUES(80,5,65,1,1,'50109','Repair and Maintenance Expenses-Office Equipment',NULL,0,0,NULL);
INSERT INTO Account VALUES(81,5,65,1,1,'50110','Repair and Maintenance Expenses-Vehicle',NULL,0,0,NULL);
INSERT INTO Account VALUES(82,5,65,1,1,'50111','Supplies Expenses-Office',NULL,0,0,NULL);
INSERT INTO Account VALUES(83,5,65,1,1,'50112','Telephone Expenses',NULL,0,0,NULL);
INSERT INTO Account VALUES(84,5,65,1,1,'50113','Postage Expenses',NULL,0,0,NULL);
INSERT INTO Account VALUES(85,5,65,1,1,'50114','Insurance Expenses-Vehicle',NULL,0,0,NULL);
INSERT INTO Account VALUES(86,5,65,1,1,'50115','Dues and Subscriptions Expenses',NULL,0,0,NULL);
INSERT INTO Account VALUES(87,5,65,1,1,'50116','Depreciation Expenses',NULL,0,0,NULL);
INSERT INTO Account VALUES(88,5,65,1,1,'50117','Meals and Entertainment Expenses',NULL,0,0,NULL);
INSERT INTO Account VALUES(89,5,65,1,1,'50118','Travel Expenses',NULL,0,0,NULL);
INSERT INTO Account VALUES(90,5,65,1,1,'50119','Utilities Expenses',NULL,0,0,NULL);
INSERT INTO Account VALUES(91,5,65,1,1,'50120','Legal and Professional Expenses',NULL,0,0,NULL);
INSERT INTO Account VALUES(92,5,65,1,1,'50121','Bank Charges',NULL,0,0,NULL);
INSERT INTO Account VALUES(93,6,NULL,0,1,'99999','Income Summary',NULL,0,0,NULL);
CREATE TABLE IF NOT EXISTS "CompanySetting" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_CompanySetting" PRIMARY KEY AUTOINCREMENT,
    "CompanyId" INTEGER NOT NULL,
    CONSTRAINT "FK_CompanySetting_Company_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Company" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "Contact" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Contact" PRIMARY KEY AUTOINCREMENT,
    "ContactType" INTEGER NOT NULL,
    "FirstName" TEXT NULL,
    "LastName" TEXT NULL,
    "MiddleName" TEXT NULL,
    "PartyId" INTEGER NOT NULL,
    CONSTRAINT "FK_Contact_Party_PartyId" FOREIGN KEY ("PartyId") REFERENCES "Party" ("Id") ON DELETE CASCADE
);
INSERT INTO Contact VALUES(1,2,'Mary','Walter',NULL,2);
INSERT INTO Contact VALUES(2,1,'John','Doe',NULL,4);
INSERT INTO Contact VALUES(3,1,'Northwind','Contact',NULL,6);
INSERT INTO Contact VALUES(4,2,'Paper','Contact',NULL,8);
CREATE TABLE IF NOT EXISTS "JournalEntryHeader" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_JournalEntryHeader" PRIMARY KEY AUTOINCREMENT,
    "GeneralLedgerHeaderId" INTEGER NULL,
    "PartyId" INTEGER NULL,
    "VoucherType" INTEGER NULL,
    "Date" TEXT NOT NULL,
    "Memo" TEXT NULL,
    "ReferenceNo" TEXT NULL,
    "Posted" INTEGER NULL,
    CONSTRAINT "FK_JournalEntryHeader_GeneralLedgerHeader_GeneralLedgerHeaderId" FOREIGN KEY ("GeneralLedgerHeaderId") REFERENCES "GeneralLedgerHeader" ("Id"),
    CONSTRAINT "FK_JournalEntryHeader_Party_PartyId" FOREIGN KEY ("PartyId") REFERENCES "Party" ("Id")
);
CREATE TABLE IF NOT EXISTS "SecurityPermission" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SecurityPermission" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NULL,
    "DisplayName" TEXT NULL,
    "SecurityGroupId" INTEGER NOT NULL,
    CONSTRAINT "FK_SecurityPermission_SecurityGroup_SecurityGroupId" FOREIGN KEY ("SecurityGroupId") REFERENCES "SecurityGroup" ("Id") ON DELETE CASCADE
);
INSERT INTO SecurityPermission VALUES(1,'sales.customers.view','View Customers',1);
INSERT INTO SecurityPermission VALUES(2,'sales.customers.manage','Manage Customers',1);
INSERT INTO SecurityPermission VALUES(3,'sales.invoices.view','View Invoices',1);
INSERT INTO SecurityPermission VALUES(4,'sales.invoices.create','Create Invoices',1);
INSERT INTO SecurityPermission VALUES(5,'sales.invoices.post','Post Invoices',1);
INSERT INTO SecurityPermission VALUES(6,'sales.receipts.view','View Receipts',1);
INSERT INTO SecurityPermission VALUES(7,'sales.receipts.create','Create Receipts',1);
INSERT INTO SecurityPermission VALUES(8,'sales.quotations.view','View Quotations',1);
INSERT INTO SecurityPermission VALUES(9,'financials.accounts.view','View Accounts',2);
INSERT INTO SecurityPermission VALUES(10,'financials.accounts.manage','Manage Accounts',2);
INSERT INTO SecurityPermission VALUES(11,'financials.journal.view','View Journal',2);
INSERT INTO SecurityPermission VALUES(12,'financials.journal.create','Create Journal Entries',2);
INSERT INTO SecurityPermission VALUES(13,'financials.journal.post','Post Journal Entries',2);
INSERT INTO SecurityPermission VALUES(14,'financials.cashbanks.view','View Cash & Banks',2);
INSERT INTO SecurityPermission VALUES(15,'financials.cashbanks.manage','Manage Cash & Banks',2);
INSERT INTO SecurityPermission VALUES(16,'purchases.vendors.view','View Vendors',3);
INSERT INTO SecurityPermission VALUES(17,'purchases.vendors.manage','Manage Vendors',3);
INSERT INTO SecurityPermission VALUES(18,'purchases.orders.view','View Orders',3);
INSERT INTO SecurityPermission VALUES(19,'purchases.bills.view','View Bills',3);
INSERT INTO SecurityPermission VALUES(20,'purchases.bills.create','Create Bills',3);
INSERT INTO SecurityPermission VALUES(21,'purchases.bills.post','Post Bills',3);
INSERT INTO SecurityPermission VALUES(22,'inventory.items.view','View Items',4);
INSERT INTO SecurityPermission VALUES(23,'inventory.items.manage','Manage Items',4);
INSERT INTO SecurityPermission VALUES(24,'inventory.adjustments.view','View Adjustments',4);
INSERT INTO SecurityPermission VALUES(25,'inventory.adjustments.post','Post Adjustments',4);
INSERT INTO SecurityPermission VALUES(26,'reports.view','View Reports',5);
INSERT INTO SecurityPermission VALUES(27,'donations.view','View Donations',6);
INSERT INTO SecurityPermission VALUES(28,'donations.manage','Manage Donations',6);
INSERT INTO SecurityPermission VALUES(29,'audit.logs.view','View Audit Logs',7);
INSERT INTO SecurityPermission VALUES(30,'admin.members.manage','Manage Members',8);
INSERT INTO SecurityPermission VALUES(31,'admin.roles.manage','Manage Roles',8);
INSERT INTO SecurityPermission VALUES(32,'admin.subscription.manage','Manage Subscription',8);
CREATE TABLE IF NOT EXISTS "SecurityUserRole" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SecurityUserRole" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "SecurityRoleId" INTEGER NOT NULL,
    CONSTRAINT "FK_SecurityUserRole_SecurityRole_SecurityRoleId" FOREIGN KEY ("SecurityRoleId") REFERENCES "SecurityRole" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_SecurityUserRole_User_UserId" FOREIGN KEY ("UserId") REFERENCES "User" ("Id") ON DELETE CASCADE
);
INSERT INTO SecurityUserRole VALUES(1,1,2);
INSERT INTO SecurityUserRole VALUES(2,1,1);
CREATE TABLE IF NOT EXISTS "Bank" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Bank" PRIMARY KEY AUTOINCREMENT,
    "Type" INTEGER NOT NULL,
    "Name" TEXT NULL,
    "AccountId" INTEGER NULL,
    "BankName" TEXT NULL,
    "Number" TEXT NULL,
    "Address" TEXT NULL,
    "IsDefault" INTEGER NOT NULL,
    "IsActive" INTEGER NOT NULL,
    CONSTRAINT "FK_Bank_Account_AccountId" FOREIGN KEY ("AccountId") REFERENCES "Account" ("Id")
);
INSERT INTO Bank VALUES(1,1,'General Fund',16,'GFB','1234567890','123 Main St.',1,1);
INSERT INTO Bank VALUES(2,3,'Petty Cash Account',18,NULL,NULL,NULL,0,1);
CREATE TABLE IF NOT EXISTS "DonationInvoiceHeader" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_DonationInvoiceHeader" PRIMARY KEY AUTOINCREMENT,
    "DonorId" INTEGER NOT NULL,
    "No" TEXT NULL,
    "Date" TEXT NOT NULL,
    "ReferenceNo" TEXT NULL,
    "PaymentType" TEXT NULL,
    "Amount" decimal(18, 2) NOT NULL,
    "GlAccountId" INTEGER NULL,
    "Purpose" TEXT NULL,
    "IsTaxReceiptIssued" INTEGER NOT NULL,
    "TaxReceiptNo" TEXT NULL,
    "Posted" INTEGER NOT NULL,
    CONSTRAINT "FK_DonationInvoiceHeader_Account_GlAccountId" FOREIGN KEY ("GlAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_DonationInvoiceHeader_Donors_DonorId" FOREIGN KEY ("DonorId") REFERENCES "Donors" ("Id")
);
INSERT INTO DonationInvoiceHeader VALUES(1,1,'DON-SEED-001','2026-10-01 00:00:00','','Cash',150,61,'General-purpose support',1,'TR-SEED-001',0);
INSERT INTO DonationInvoiceHeader VALUES(2,1,'DON-SEED-002','2026-10-02 00:00:00','','Credit Card',75.5,62,'Sunday School materials',0,'',0);
INSERT INTO DonationInvoiceHeader VALUES(3,2,'DON-SEED-003','2026-10-03 00:00:00','','E-Transfer',5000,63,'Brothers In Christ outreach',1,'TR-SEED-003',0);
INSERT INTO DonationInvoiceHeader VALUES(4,2,'DON-SEED-004','2026-10-04 00:00:00','','Cheque',1200,61,'Community care fund',0,'',0);
CREATE TABLE IF NOT EXISTS "GeneralLedgerLine" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_GeneralLedgerLine" PRIMARY KEY AUTOINCREMENT,
    "GeneralLedgerHeaderId" INTEGER NOT NULL,
    "AccountId" INTEGER NOT NULL,
    "DrCr" INTEGER NOT NULL,
    "Amount" decimal(18, 2) NOT NULL,
    CONSTRAINT "FK_GeneralLedgerLine_Account_AccountId" FOREIGN KEY ("AccountId") REFERENCES "Account" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_GeneralLedgerLine_GeneralLedgerHeader_GeneralLedgerHeaderId" FOREIGN KEY ("GeneralLedgerHeaderId") REFERENCES "GeneralLedgerHeader" ("Id") ON DELETE CASCADE
);
INSERT INTO GeneralLedgerLine VALUES(1,1,6,1,600);
INSERT INTO GeneralLedgerLine VALUES(2,1,54,2,535.7100000000000363);
INSERT INTO GeneralLedgerLine VALUES(3,1,40,2,64.29000000000000626);
INSERT INTO GeneralLedgerLine VALUES(4,2,6,1,480);
INSERT INTO GeneralLedgerLine VALUES(5,2,54,2,428.5699999999999932);
INSERT INTO GeneralLedgerLine VALUES(6,2,40,2,51.42999999999999972);
INSERT INTO GeneralLedgerLine VALUES(7,3,16,1,300);
INSERT INTO GeneralLedgerLine VALUES(8,3,6,2,300);
INSERT INTO GeneralLedgerLine VALUES(9,4,12,1,40);
INSERT INTO GeneralLedgerLine VALUES(10,4,13,2,40);
INSERT INTO GeneralLedgerLine VALUES(11,4,34,2,400);
INSERT INTO GeneralLedgerLine VALUES(12,4,13,1,400);
INSERT INTO GeneralLedgerLine VALUES(13,5,12,1,48);
INSERT INTO GeneralLedgerLine VALUES(14,5,13,2,48);
INSERT INTO GeneralLedgerLine VALUES(15,5,34,2,384);
INSERT INTO GeneralLedgerLine VALUES(16,5,13,1,384);
INSERT INTO GeneralLedgerLine VALUES(17,6,34,1,150);
INSERT INTO GeneralLedgerLine VALUES(18,6,16,2,150);
CREATE TABLE IF NOT EXISTS "GeneralLedgerSetting" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_GeneralLedgerSetting" PRIMARY KEY AUTOINCREMENT,
    "CompanyId" INTEGER NULL,
    "PayableAccountId" INTEGER NULL,
    "PurchaseDiscountAccountId" INTEGER NULL,
    "GoodsReceiptNoteClearingAccountId" INTEGER NULL,
    "SalesDiscountAccountId" INTEGER NULL,
    "ShippingChargeAccountId" INTEGER NULL,
    "PermanentAccountId" INTEGER NULL,
    "IncomeSummaryAccountId" INTEGER NULL,
    CONSTRAINT "FK_GeneralLedgerSetting_Account_GoodsReceiptNoteClearingAccountId" FOREIGN KEY ("GoodsReceiptNoteClearingAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_GeneralLedgerSetting_Account_IncomeSummaryAccountId" FOREIGN KEY ("IncomeSummaryAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_GeneralLedgerSetting_Account_PayableAccountId" FOREIGN KEY ("PayableAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_GeneralLedgerSetting_Account_PermanentAccountId" FOREIGN KEY ("PermanentAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_GeneralLedgerSetting_Account_PurchaseDiscountAccountId" FOREIGN KEY ("PurchaseDiscountAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_GeneralLedgerSetting_Account_SalesDiscountAccountId" FOREIGN KEY ("SalesDiscountAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_GeneralLedgerSetting_Account_ShippingChargeAccountId" FOREIGN KEY ("ShippingChargeAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_GeneralLedgerSetting_Company_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Company" ("Id")
);
INSERT INTO GeneralLedgerSetting VALUES(1,1,NULL,NULL,13,57,58,NULL,NULL);
CREATE TABLE IF NOT EXISTS "ItemCategory" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ItemCategory" PRIMARY KEY AUTOINCREMENT,
    "ItemType" INTEGER NOT NULL,
    "MeasurementId" INTEGER NULL,
    "SalesAccountId" INTEGER NULL,
    "InventoryAccountId" INTEGER NULL,
    "CostOfGoodsSoldAccountId" INTEGER NULL,
    "AdjustmentAccountId" INTEGER NULL,
    "AssemblyAccountId" INTEGER NULL,
    "Name" TEXT NULL,
    CONSTRAINT "FK_ItemCategory_Account_AdjustmentAccountId" FOREIGN KEY ("AdjustmentAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_ItemCategory_Account_AssemblyAccountId" FOREIGN KEY ("AssemblyAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_ItemCategory_Account_CostOfGoodsSoldAccountId" FOREIGN KEY ("CostOfGoodsSoldAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_ItemCategory_Account_InventoryAccountId" FOREIGN KEY ("InventoryAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_ItemCategory_Account_SalesAccountId" FOREIGN KEY ("SalesAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_ItemCategory_Measurement_MeasurementId" FOREIGN KEY ("MeasurementId") REFERENCES "Measurement" ("Id")
);
INSERT INTO ItemCategory VALUES(1,4,1,54,12,67,69,14,'Charges');
INSERT INTO ItemCategory VALUES(2,2,1,54,12,67,69,14,'Components');
INSERT INTO ItemCategory VALUES(3,3,4,54,12,67,69,14,'Services');
INSERT INTO ItemCategory VALUES(4,1,1,54,12,67,69,14,'Systems');
CREATE TABLE IF NOT EXISTS "MainContraAccount" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_MainContraAccount" PRIMARY KEY AUTOINCREMENT,
    "MainAccountId" INTEGER NOT NULL,
    "RelatedContraAccountId" INTEGER NOT NULL,
    CONSTRAINT "FK_MainContraAccount_Account_MainAccountId" FOREIGN KEY ("MainAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_MainContraAccount_Account_RelatedContraAccountId" FOREIGN KEY ("RelatedContraAccountId") REFERENCES "Account" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "Tax" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Tax" PRIMARY KEY AUTOINCREMENT,
    "SalesAccountId" INTEGER NULL,
    "PurchasingAccountId" INTEGER NULL,
    "TaxName" TEXT NOT NULL,
    "TaxCode" TEXT NOT NULL,
    "Rate" decimal(18, 2) NOT NULL,
    "IsActive" INTEGER NOT NULL,
    CONSTRAINT "FK_Tax_Account_PurchasingAccountId" FOREIGN KEY ("PurchasingAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_Tax_Account_SalesAccountId" FOREIGN KEY ("SalesAccountId") REFERENCES "Account" ("Id")
);
INSERT INTO Tax VALUES(1,40,71,'Goods and Services Tax','GST',5,1);
INSERT INTO Tax VALUES(2,40,71,'Harmonized Sales Tax','HST',12,1);
INSERT INTO Tax VALUES(3,40,71,'Provincial Sales Tax','PST',7,1);
CREATE TABLE IF NOT EXISTS "Customer" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Customer" PRIMARY KEY AUTOINCREMENT,
    "No" TEXT NULL,
    "PartyId" INTEGER NULL,
    "PrimaryContactId" INTEGER NULL,
    "TaxGroupId" INTEGER NULL,
    "AccountsReceivableAccountId" INTEGER NULL,
    "SalesAccountId" INTEGER NULL,
    "SalesDiscountAccountId" INTEGER NULL,
    "PromptPaymentDiscountAccountId" INTEGER NULL,
    "PaymentTermId" INTEGER NULL,
    "CustomerAdvancesAccountId" INTEGER NULL,
    CONSTRAINT "FK_Customer_Account_AccountsReceivableAccountId" FOREIGN KEY ("AccountsReceivableAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_Customer_Account_CustomerAdvancesAccountId" FOREIGN KEY ("CustomerAdvancesAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_Customer_Account_PromptPaymentDiscountAccountId" FOREIGN KEY ("PromptPaymentDiscountAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_Customer_Account_SalesAccountId" FOREIGN KEY ("SalesAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_Customer_Account_SalesDiscountAccountId" FOREIGN KEY ("SalesDiscountAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_Customer_Contact_PrimaryContactId" FOREIGN KEY ("PrimaryContactId") REFERENCES "Contact" ("Id"),
    CONSTRAINT "FK_Customer_Party_PartyId" FOREIGN KEY ("PartyId") REFERENCES "Party" ("Id"),
    CONSTRAINT "FK_Customer_PaymentTerm_PaymentTermId" FOREIGN KEY ("PaymentTermId") REFERENCES "PaymentTerm" ("Id"),
    CONSTRAINT "FK_Customer_TaxGroup_TaxGroupId" FOREIGN KEY ("TaxGroupId") REFERENCES "TaxGroup" ("Id")
);
INSERT INTO Customer VALUES(1,'1',3,2,1,6,54,57,NULL,NULL,35);
INSERT INTO Customer VALUES(2,'2',5,3,1,6,54,57,NULL,NULL,35);
CREATE TABLE IF NOT EXISTS "Vendor" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Vendor" PRIMARY KEY AUTOINCREMENT,
    "No" TEXT NULL,
    "PartyId" INTEGER NULL,
    "AccountsPayableAccountId" INTEGER NULL,
    "PurchaseAccountId" INTEGER NULL,
    "PurchaseDiscountAccountId" INTEGER NULL,
    "PrimaryContactId" INTEGER NULL,
    "PaymentTermId" INTEGER NULL,
    "PaymentMethod" INTEGER NULL,
    "TaxGroupId" INTEGER NULL,
    "DiscountPercentage" decimal(18, 2) NULL,
    "Street1" TEXT NULL,
    "Street2" TEXT NULL,
    "City" TEXT NULL,
    "Province" TEXT NULL,
    "PostalCode" TEXT NULL,
    "Country" TEXT NULL,
    CONSTRAINT "FK_Vendor_Account_AccountsPayableAccountId" FOREIGN KEY ("AccountsPayableAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_Vendor_Account_PurchaseAccountId" FOREIGN KEY ("PurchaseAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_Vendor_Account_PurchaseDiscountAccountId" FOREIGN KEY ("PurchaseDiscountAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_Vendor_Contact_PrimaryContactId" FOREIGN KEY ("PrimaryContactId") REFERENCES "Contact" ("Id"),
    CONSTRAINT "FK_Vendor_Party_PartyId" FOREIGN KEY ("PartyId") REFERENCES "Party" ("Id"),
    CONSTRAINT "FK_Vendor_PaymentTerm_PaymentTermId" FOREIGN KEY ("PaymentTermId") REFERENCES "PaymentTerm" ("Id"),
    CONSTRAINT "FK_Vendor_TaxGroup_TaxGroupId" FOREIGN KEY ("TaxGroupId") REFERENCES "TaxGroup" ("Id")
);
INSERT INTO Vendor VALUES(1,'1',1,34,66,68,1,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);
INSERT INTO Vendor VALUES(2,'2',7,34,66,68,4,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);
CREATE TABLE IF NOT EXISTS "JournalEntryLine" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_JournalEntryLine" PRIMARY KEY AUTOINCREMENT,
    "JournalEntryHeaderId" INTEGER NOT NULL,
    "AccountId" INTEGER NOT NULL,
    "DrCr" INTEGER NOT NULL,
    "Amount" decimal(18, 2) NOT NULL,
    "Memo" TEXT NULL,
    CONSTRAINT "FK_JournalEntryLine_Account_AccountId" FOREIGN KEY ("AccountId") REFERENCES "Account" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_JournalEntryLine_JournalEntryHeader_JournalEntryHeaderId" FOREIGN KEY ("JournalEntryHeaderId") REFERENCES "JournalEntryHeader" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "SecurityRolePermission" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SecurityRolePermission" PRIMARY KEY AUTOINCREMENT,
    "SecurityRoleId" INTEGER NOT NULL,
    "SecurityPermissionId" INTEGER NOT NULL,
    CONSTRAINT "FK_SecurityRolePermission_SecurityPermission_SecurityPermissionId" FOREIGN KEY ("SecurityPermissionId") REFERENCES "SecurityPermission" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_SecurityRolePermission_SecurityRole_SecurityRoleId" FOREIGN KEY ("SecurityRoleId") REFERENCES "SecurityRole" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "ItemTaxGroupTax" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ItemTaxGroupTax" PRIMARY KEY AUTOINCREMENT,
    "TaxId" INTEGER NOT NULL,
    "ItemTaxGroupId" INTEGER NOT NULL,
    "IsExempt" INTEGER NOT NULL,
    CONSTRAINT "FK_ItemTaxGroupTax_ItemTaxGroup_ItemTaxGroupId" FOREIGN KEY ("ItemTaxGroupId") REFERENCES "ItemTaxGroup" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ItemTaxGroupTax_Tax_TaxId" FOREIGN KEY ("TaxId") REFERENCES "Tax" ("Id") ON DELETE CASCADE
);
INSERT INTO ItemTaxGroupTax VALUES(1,1,1,0);
INSERT INTO ItemTaxGroupTax VALUES(2,2,2,0);
CREATE TABLE IF NOT EXISTS "TaxGroupTax" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_TaxGroupTax" PRIMARY KEY AUTOINCREMENT,
    "TaxId" INTEGER NOT NULL,
    "TaxGroupId" INTEGER NOT NULL,
    CONSTRAINT "FK_TaxGroupTax_TaxGroup_TaxGroupId" FOREIGN KEY ("TaxGroupId") REFERENCES "TaxGroup" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_TaxGroupTax_Tax_TaxId" FOREIGN KEY ("TaxId") REFERENCES "Tax" ("Id") ON DELETE CASCADE
);
INSERT INTO TaxGroupTax VALUES(1,1,1);
INSERT INTO TaxGroupTax VALUES(2,2,1);
INSERT INTO TaxGroupTax VALUES(3,3,2);
CREATE TABLE IF NOT EXISTS "CustomerContact" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_CustomerContact" PRIMARY KEY AUTOINCREMENT,
    "ContactId" INTEGER NOT NULL,
    "CustomerId" INTEGER NOT NULL,
    CONSTRAINT "FK_CustomerContact_Contact_ContactId" FOREIGN KEY ("ContactId") REFERENCES "Contact" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_CustomerContact_Customer_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customer" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "SalesDeliveryHeader" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SalesDeliveryHeader" PRIMARY KEY AUTOINCREMENT,
    "PaymentTermId" INTEGER NULL,
    "CustomerId" INTEGER NULL,
    "GeneralLedgerHeaderId" INTEGER NULL,
    "No" TEXT NULL,
    "Date" TEXT NOT NULL,
    "Status" INTEGER NULL,
    CONSTRAINT "FK_SalesDeliveryHeader_Customer_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customer" ("Id"),
    CONSTRAINT "FK_SalesDeliveryHeader_GeneralLedgerHeader_GeneralLedgerHeaderId" FOREIGN KEY ("GeneralLedgerHeaderId") REFERENCES "GeneralLedgerHeader" ("Id"),
    CONSTRAINT "FK_SalesDeliveryHeader_PaymentTerm_PaymentTermId" FOREIGN KEY ("PaymentTermId") REFERENCES "PaymentTerm" ("Id")
);
CREATE TABLE IF NOT EXISTS "SalesInvoiceHeader" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SalesInvoiceHeader" PRIMARY KEY AUTOINCREMENT,
    "CustomerId" INTEGER NOT NULL,
    "GeneralLedgerHeaderId" INTEGER NULL,
    "No" TEXT NULL,
    "Date" TEXT NOT NULL,
    "ShippingHandlingCharge" decimal(18, 2) NOT NULL,
    "PaymentTermId" INTEGER NULL,
    "ReferenceNo" TEXT NULL,
    "Status" INTEGER NULL,
    CONSTRAINT "FK_SalesInvoiceHeader_Customer_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customer" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_SalesInvoiceHeader_GeneralLedgerHeader_GeneralLedgerHeaderId" FOREIGN KEY ("GeneralLedgerHeaderId") REFERENCES "GeneralLedgerHeader" ("Id")
);
INSERT INTO SalesInvoiceHeader VALUES(1,1,1,'1','2026-02-05 00:00:00',0,1,'DASH-SI-0205',NULL);
INSERT INTO SalesInvoiceHeader VALUES(2,2,2,'2','2026-03-10 00:00:00',0,1,'DASH-SI-0310',NULL);
CREATE TABLE IF NOT EXISTS "SalesOrderHeader" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SalesOrderHeader" PRIMARY KEY AUTOINCREMENT,
    "CustomerId" INTEGER NULL,
    "PaymentTermId" INTEGER NULL,
    "No" TEXT NULL,
    "ReferenceNo" TEXT NULL,
    "Status" INTEGER NULL,
    "Date" TEXT NOT NULL,
    CONSTRAINT "FK_SalesOrderHeader_Customer_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customer" ("Id"),
    CONSTRAINT "FK_SalesOrderHeader_PaymentTerm_PaymentTermId" FOREIGN KEY ("PaymentTermId") REFERENCES "PaymentTerm" ("Id")
);
CREATE TABLE IF NOT EXISTS "SalesQuoteHeader" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SalesQuoteHeader" PRIMARY KEY AUTOINCREMENT,
    "CustomerId" INTEGER NOT NULL,
    "PaymentTermId" INTEGER NULL,
    "ReferenceNo" TEXT NULL,
    "No" TEXT NULL,
    "Status" INTEGER NULL,
    "Date" TEXT NOT NULL,
    CONSTRAINT "FK_SalesQuoteHeader_Customer_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customer" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "SalesReceiptHeader" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SalesReceiptHeader" PRIMARY KEY AUTOINCREMENT,
    "CustomerId" INTEGER NOT NULL,
    "GeneralLedgerHeaderId" INTEGER NULL,
    "AccountToDebitId" INTEGER NULL,
    "No" TEXT NULL,
    "Date" TEXT NOT NULL,
    "Amount" decimal(18, 2) NOT NULL,
    "Status" INTEGER NULL,
    "PaymentMethod" TEXT NULL,
    "ReferenceNo" TEXT NULL,
    "Memo" TEXT NULL,
    CONSTRAINT "FK_SalesReceiptHeader_Account_AccountToDebitId" FOREIGN KEY ("AccountToDebitId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_SalesReceiptHeader_Customer_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customer" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_SalesReceiptHeader_GeneralLedgerHeader_GeneralLedgerHeaderId" FOREIGN KEY ("GeneralLedgerHeaderId") REFERENCES "GeneralLedgerHeader" ("Id")
);
INSERT INTO SalesReceiptHeader VALUES(1,1,3,16,'1','2026-02-19 00:00:00',300,NULL,NULL,NULL,NULL);
CREATE TABLE IF NOT EXISTS "Item" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Item" PRIMARY KEY AUTOINCREMENT,
    "ItemCategoryId" INTEGER NULL,
    "SmallestMeasurementId" INTEGER NULL,
    "SellMeasurementId" INTEGER NULL,
    "PurchaseMeasurementId" INTEGER NULL,
    "PreferredVendorId" INTEGER NULL,
    "ItemTaxGroupId" INTEGER NULL,
    "SalesAccountId" INTEGER NULL,
    "InventoryAccountId" INTEGER NULL,
    "CostOfGoodsSoldAccountId" INTEGER NULL,
    "InventoryAdjustmentAccountId" INTEGER NULL,
    "No" TEXT NULL,
    "Code" TEXT NULL,
    "Description" TEXT NULL,
    "PurchaseDescription" TEXT NULL,
    "SellDescription" TEXT NULL,
    "Cost" decimal(18, 2) NULL,
    "Price" decimal(18, 2) NULL,
    CONSTRAINT "FK_Item_Account_CostOfGoodsSoldAccountId" FOREIGN KEY ("CostOfGoodsSoldAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_Item_Account_InventoryAccountId" FOREIGN KEY ("InventoryAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_Item_Account_InventoryAdjustmentAccountId" FOREIGN KEY ("InventoryAdjustmentAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_Item_Account_SalesAccountId" FOREIGN KEY ("SalesAccountId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_Item_ItemCategory_ItemCategoryId" FOREIGN KEY ("ItemCategoryId") REFERENCES "ItemCategory" ("Id"),
    CONSTRAINT "FK_Item_ItemTaxGroup_ItemTaxGroupId" FOREIGN KEY ("ItemTaxGroupId") REFERENCES "ItemTaxGroup" ("Id"),
    CONSTRAINT "FK_Item_Measurement_PurchaseMeasurementId" FOREIGN KEY ("PurchaseMeasurementId") REFERENCES "Measurement" ("Id"),
    CONSTRAINT "FK_Item_Measurement_SellMeasurementId" FOREIGN KEY ("SellMeasurementId") REFERENCES "Measurement" ("Id"),
    CONSTRAINT "FK_Item_Measurement_SmallestMeasurementId" FOREIGN KEY ("SmallestMeasurementId") REFERENCES "Measurement" ("Id"),
    CONSTRAINT "FK_Item_Vendor_PreferredVendorId" FOREIGN KEY ("PreferredVendorId") REFERENCES "Vendor" ("Id")
);
INSERT INTO Item VALUES(1,1,3,3,NULL,NULL,NULL,55,NULL,NULL,NULL,'1','1111','HOA Dues','HOA Dues','HOA Dues',NULL,350);
INSERT INTO Item VALUES(2,2,1,1,1,NULL,2,54,12,67,69,'2','2222','Car Sticker','Car Sticker','Car Sticker',40,100);
INSERT INTO Item VALUES(3,2,1,1,1,NULL,2,54,12,67,69,'3','3333','Optical Mouse','Optical Mouse','Optical Mouse',30,80);
CREATE TABLE IF NOT EXISTS "PurchaseInvoiceHeader" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_PurchaseInvoiceHeader" PRIMARY KEY AUTOINCREMENT,
    "VendorId" INTEGER NULL,
    "GeneralLedgerHeaderId" INTEGER NULL,
    "Date" TEXT NOT NULL,
    "No" TEXT NULL,
    "VendorInvoiceNo" TEXT NOT NULL,
    "Description" TEXT NULL,
    "PaymentTermId" INTEGER NULL,
    "ReferenceNo" TEXT NULL,
    "Status" INTEGER NULL,
    CONSTRAINT "FK_PurchaseInvoiceHeader_GeneralLedgerHeader_GeneralLedgerHeaderId" FOREIGN KEY ("GeneralLedgerHeaderId") REFERENCES "GeneralLedgerHeader" ("Id"),
    CONSTRAINT "FK_PurchaseInvoiceHeader_Vendor_VendorId" FOREIGN KEY ("VendorId") REFERENCES "Vendor" ("Id")
);
INSERT INTO PurchaseInvoiceHeader VALUES(1,1,4,'2026-02-03 00:00:00','1','V-0203','Dashboard demo purchase invoice',1,'DASH-PI-0203',NULL);
INSERT INTO PurchaseInvoiceHeader VALUES(2,2,5,'2026-04-04 00:00:00','2','V-0404','Dashboard demo purchase invoice',1,'DASH-PI-0404',NULL);
CREATE TABLE IF NOT EXISTS "PurchaseReceiptHeader" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_PurchaseReceiptHeader" PRIMARY KEY AUTOINCREMENT,
    "VendorId" INTEGER NOT NULL,
    "GeneralLedgerHeaderId" INTEGER NULL,
    "Date" TEXT NOT NULL,
    "No" TEXT NULL,
    CONSTRAINT "FK_PurchaseReceiptHeader_GeneralLedgerHeader_GeneralLedgerHeaderId" FOREIGN KEY ("GeneralLedgerHeaderId") REFERENCES "GeneralLedgerHeader" ("Id"),
    CONSTRAINT "FK_PurchaseReceiptHeader_Vendor_VendorId" FOREIGN KEY ("VendorId") REFERENCES "Vendor" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "VendorContact" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_VendorContact" PRIMARY KEY AUTOINCREMENT,
    "ContactId" INTEGER NOT NULL,
    "VendorId" INTEGER NOT NULL,
    CONSTRAINT "FK_VendorContact_Contact_ContactId" FOREIGN KEY ("ContactId") REFERENCES "Contact" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_VendorContact_Vendor_VendorId" FOREIGN KEY ("VendorId") REFERENCES "Vendor" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "CustomerAllocation" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_CustomerAllocation" PRIMARY KEY AUTOINCREMENT,
    "CustomerId" INTEGER NOT NULL,
    "SalesInvoiceHeaderId" INTEGER NOT NULL,
    "SalesReceiptHeaderId" INTEGER NOT NULL,
    "Date" TEXT NOT NULL,
    "Amount" decimal(18, 2) NOT NULL,
    CONSTRAINT "FK_CustomerAllocation_Customer_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customer" ("Id"),
    CONSTRAINT "FK_CustomerAllocation_SalesInvoiceHeader_SalesInvoiceHeaderId" FOREIGN KEY ("SalesInvoiceHeaderId") REFERENCES "SalesInvoiceHeader" ("Id"),
    CONSTRAINT "FK_CustomerAllocation_SalesReceiptHeader_SalesReceiptHeaderId" FOREIGN KEY ("SalesReceiptHeaderId") REFERENCES "SalesReceiptHeader" ("Id")
);
CREATE TABLE IF NOT EXISTS "DonationInvoiceLine" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_DonationInvoiceLine" PRIMARY KEY AUTOINCREMENT,
    "DonationInvoiceHeaderId" INTEGER NOT NULL,
    "ItemId" INTEGER NOT NULL,
    "MeasurementId" INTEGER NOT NULL,
    "Quantity" TEXT NOT NULL,
    "Amount" TEXT NOT NULL,
    "Notes" TEXT NULL,
    CONSTRAINT "FK_DonationInvoiceLine_DonationInvoiceHeader_DonationInvoiceHeaderId" FOREIGN KEY ("DonationInvoiceHeaderId") REFERENCES "DonationInvoiceHeader" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_DonationInvoiceLine_Item_ItemId" FOREIGN KEY ("ItemId") REFERENCES "Item" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_DonationInvoiceLine_Measurement_MeasurementId" FOREIGN KEY ("MeasurementId") REFERENCES "Measurement" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "InventoryControlJournal" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_InventoryControlJournal" PRIMARY KEY AUTOINCREMENT,
    "ItemId" INTEGER NOT NULL,
    "MeasurementId" INTEGER NOT NULL,
    "DocumentType" INTEGER NOT NULL,
    "INQty" decimal(18, 2) NULL,
    "OUTQty" decimal(18, 2) NULL,
    "Date" TEXT NOT NULL,
    "TotalCost" decimal(18, 2) NULL,
    "TotalAmount" decimal(18, 2) NULL,
    "IsReverse" INTEGER NOT NULL,
    CONSTRAINT "FK_InventoryControlJournal_Item_ItemId" FOREIGN KEY ("ItemId") REFERENCES "Item" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_InventoryControlJournal_Measurement_MeasurementId" FOREIGN KEY ("MeasurementId") REFERENCES "Measurement" ("Id") ON DELETE CASCADE
);
INSERT INTO InventoryControlJournal VALUES(1,2,1,4,NULL,6,'2026-10-20 13:31:22.601864',240,600,0);
INSERT INTO InventoryControlJournal VALUES(2,2,1,4,NULL,4,'2026-10-20 13:31:22.967793',160,400,0);
INSERT INTO InventoryControlJournal VALUES(3,2,1,9,10,NULL,'2026-10-20 13:31:23.215442',400,NULL,0);
INSERT INTO InventoryControlJournal VALUES(4,2,1,9,8,NULL,'2026-10-20 13:31:23.773795',320,NULL,0);
CREATE TABLE IF NOT EXISTS "SalesOrderLine" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SalesOrderLine" PRIMARY KEY AUTOINCREMENT,
    "SalesOrderHeaderId" INTEGER NOT NULL,
    "ItemId" INTEGER NOT NULL,
    "MeasurementId" INTEGER NOT NULL,
    "Quantity" decimal(18, 2) NOT NULL,
    "Discount" decimal(18, 2) NOT NULL,
    "Amount" decimal(18, 2) NOT NULL,
    CONSTRAINT "FK_SalesOrderLine_Item_ItemId" FOREIGN KEY ("ItemId") REFERENCES "Item" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_SalesOrderLine_Measurement_MeasurementId" FOREIGN KEY ("MeasurementId") REFERENCES "Measurement" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_SalesOrderLine_SalesOrderHeader_SalesOrderHeaderId" FOREIGN KEY ("SalesOrderHeaderId") REFERENCES "SalesOrderHeader" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "SalesQuoteLine" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SalesQuoteLine" PRIMARY KEY AUTOINCREMENT,
    "SalesQuoteHeaderId" INTEGER NOT NULL,
    "ItemId" INTEGER NOT NULL,
    "MeasurementId" INTEGER NOT NULL,
    "Quantity" decimal(18, 2) NOT NULL,
    "Discount" decimal(18, 2) NOT NULL,
    "Amount" decimal(18, 2) NOT NULL,
    CONSTRAINT "FK_SalesQuoteLine_Item_ItemId" FOREIGN KEY ("ItemId") REFERENCES "Item" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_SalesQuoteLine_Measurement_MeasurementId" FOREIGN KEY ("MeasurementId") REFERENCES "Measurement" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_SalesQuoteLine_SalesQuoteHeader_SalesQuoteHeaderId" FOREIGN KEY ("SalesQuoteHeaderId") REFERENCES "SalesQuoteHeader" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "PurchaseOrderHeader" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_PurchaseOrderHeader" PRIMARY KEY AUTOINCREMENT,
    "VendorId" INTEGER NULL,
    "No" TEXT NULL,
    "Date" TEXT NOT NULL,
    "Description" TEXT NULL,
    "PaymentTermId" INTEGER NULL,
    "ReferenceNo" TEXT NULL,
    "Status" INTEGER NULL,
    "PurchaseInvoiceHeaderId" INTEGER NULL,
    CONSTRAINT "FK_PurchaseOrderHeader_PurchaseInvoiceHeader_PurchaseInvoiceHeaderId" FOREIGN KEY ("PurchaseInvoiceHeaderId") REFERENCES "PurchaseInvoiceHeader" ("Id"),
    CONSTRAINT "FK_PurchaseOrderHeader_Vendor_VendorId" FOREIGN KEY ("VendorId") REFERENCES "Vendor" ("Id")
);
INSERT INTO PurchaseOrderHeader VALUES(1,1,'1','2026-02-03 00:00:00','Dashboard demo purchase invoice',NULL,NULL,NULL,1);
INSERT INTO PurchaseOrderHeader VALUES(2,2,'2','2026-04-04 00:00:00','Dashboard demo purchase invoice',NULL,NULL,NULL,2);
CREATE TABLE IF NOT EXISTS "VendorPayment" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_VendorPayment" PRIMARY KEY AUTOINCREMENT,
    "VendorId" INTEGER NOT NULL,
    "PurchaseInvoiceHeaderId" INTEGER NULL,
    "GeneralLedgerHeaderId" INTEGER NULL,
    "No" TEXT NULL,
    "Date" TEXT NOT NULL,
    "Amount" decimal(18, 2) NOT NULL,
    CONSTRAINT "FK_VendorPayment_GeneralLedgerHeader_GeneralLedgerHeaderId" FOREIGN KEY ("GeneralLedgerHeaderId") REFERENCES "GeneralLedgerHeader" ("Id"),
    CONSTRAINT "FK_VendorPayment_PurchaseInvoiceHeader_PurchaseInvoiceHeaderId" FOREIGN KEY ("PurchaseInvoiceHeaderId") REFERENCES "PurchaseInvoiceHeader" ("Id"),
    CONSTRAINT "FK_VendorPayment_Vendor_VendorId" FOREIGN KEY ("VendorId") REFERENCES "Vendor" ("Id") ON DELETE CASCADE
);
INSERT INTO VendorPayment VALUES(1,2,2,6,'1','2026-04-16 00:00:00',150);
CREATE TABLE IF NOT EXISTS "SalesInvoiceLine" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SalesInvoiceLine" PRIMARY KEY AUTOINCREMENT,
    "SalesInvoiceHeaderId" INTEGER NOT NULL,
    "SalesOrderLineId" INTEGER NULL,
    "ItemId" INTEGER NOT NULL,
    "MeasurementId" INTEGER NOT NULL,
    "InventoryControlJournalId" INTEGER NULL,
    "Quantity" decimal(18, 2) NOT NULL,
    "Discount" decimal(18, 2) NOT NULL,
    "Amount" decimal(18, 2) NOT NULL,
    CONSTRAINT "FK_SalesInvoiceLine_InventoryControlJournal_InventoryControlJournalId" FOREIGN KEY ("InventoryControlJournalId") REFERENCES "InventoryControlJournal" ("Id"),
    CONSTRAINT "FK_SalesInvoiceLine_Item_ItemId" FOREIGN KEY ("ItemId") REFERENCES "Item" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_SalesInvoiceLine_Measurement_MeasurementId" FOREIGN KEY ("MeasurementId") REFERENCES "Measurement" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_SalesInvoiceLine_SalesInvoiceHeader_SalesInvoiceHeaderId" FOREIGN KEY ("SalesInvoiceHeaderId") REFERENCES "SalesInvoiceHeader" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_SalesInvoiceLine_SalesOrderLine_SalesOrderLineId" FOREIGN KEY ("SalesOrderLineId") REFERENCES "SalesOrderLine" ("Id")
);
INSERT INTO SalesInvoiceLine VALUES(1,1,NULL,2,1,1,6,0,100);
INSERT INTO SalesInvoiceLine VALUES(2,2,NULL,2,1,2,4,0,120);
CREATE TABLE IF NOT EXISTS "PurchaseOrderLine" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_PurchaseOrderLine" PRIMARY KEY AUTOINCREMENT,
    "PurchaseOrderHeaderId" INTEGER NOT NULL,
    "ItemId" INTEGER NOT NULL,
    "TaxGroupId" INTEGER NULL,
    "Quantity" decimal(18, 2) NOT NULL,
    "Cost" decimal(18, 2) NOT NULL,
    "Discount" decimal(18, 2) NOT NULL,
    "Amount" decimal(18, 2) NOT NULL,
    CONSTRAINT "FK_PurchaseOrderLine_Item_ItemId" FOREIGN KEY ("ItemId") REFERENCES "Item" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_PurchaseOrderLine_PurchaseOrderHeader_PurchaseOrderHeaderId" FOREIGN KEY ("PurchaseOrderHeaderId") REFERENCES "PurchaseOrderHeader" ("Id") ON DELETE CASCADE
);
INSERT INTO PurchaseOrderLine VALUES(1,1,2,NULL,10,40,0,400);
INSERT INTO PurchaseOrderLine VALUES(2,2,2,NULL,8,40,0,384);
CREATE TABLE IF NOT EXISTS "SalesDeliveryLine" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SalesDeliveryLine" PRIMARY KEY AUTOINCREMENT,
    "SalesDeliveryHeaderId" INTEGER NOT NULL,
    "SalesInvoiceLineId" INTEGER NULL,
    "ItemId" INTEGER NULL,
    "MeasurementId" INTEGER NULL,
    "Quantity" decimal(18, 2) NOT NULL,
    "Price" decimal(18, 2) NOT NULL,
    "Discount" decimal(18, 2) NOT NULL,
    CONSTRAINT "FK_SalesDeliveryLine_Item_ItemId" FOREIGN KEY ("ItemId") REFERENCES "Item" ("Id"),
    CONSTRAINT "FK_SalesDeliveryLine_Measurement_MeasurementId" FOREIGN KEY ("MeasurementId") REFERENCES "Measurement" ("Id"),
    CONSTRAINT "FK_SalesDeliveryLine_SalesDeliveryHeader_SalesDeliveryHeaderId" FOREIGN KEY ("SalesDeliveryHeaderId") REFERENCES "SalesDeliveryHeader" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_SalesDeliveryLine_SalesInvoiceLine_SalesInvoiceLineId" FOREIGN KEY ("SalesInvoiceLineId") REFERENCES "SalesInvoiceLine" ("Id")
);
CREATE TABLE IF NOT EXISTS "SalesReceiptLine" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SalesReceiptLine" PRIMARY KEY AUTOINCREMENT,
    "SalesReceiptHeaderId" INTEGER NOT NULL,
    "SalesInvoiceLineId" INTEGER NULL,
    "ItemId" INTEGER NULL,
    "AccountToCreditId" INTEGER NULL,
    "MeasurementId" INTEGER NULL,
    "Quantity" decimal(18, 2) NULL,
    "Discount" decimal(18, 2) NULL,
    "Amount" decimal(18, 2) NULL,
    "AmountPaid" decimal(18, 2) NOT NULL,
    CONSTRAINT "FK_SalesReceiptLine_Account_AccountToCreditId" FOREIGN KEY ("AccountToCreditId") REFERENCES "Account" ("Id"),
    CONSTRAINT "FK_SalesReceiptLine_Item_ItemId" FOREIGN KEY ("ItemId") REFERENCES "Item" ("Id"),
    CONSTRAINT "FK_SalesReceiptLine_Measurement_MeasurementId" FOREIGN KEY ("MeasurementId") REFERENCES "Measurement" ("Id"),
    CONSTRAINT "FK_SalesReceiptLine_SalesInvoiceLine_SalesInvoiceLineId" FOREIGN KEY ("SalesInvoiceLineId") REFERENCES "SalesInvoiceLine" ("Id"),
    CONSTRAINT "FK_SalesReceiptLine_SalesReceiptHeader_SalesReceiptHeaderId" FOREIGN KEY ("SalesReceiptHeaderId") REFERENCES "SalesReceiptHeader" ("Id") ON DELETE CASCADE
);
INSERT INTO SalesReceiptLine VALUES(1,1,NULL,NULL,NULL,NULL,1,NULL,300,300);
CREATE TABLE IF NOT EXISTS "PurchaseInvoiceLine" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_PurchaseInvoiceLine" PRIMARY KEY AUTOINCREMENT,
    "PurchaseInvoiceHeaderId" INTEGER NOT NULL,
    "ItemId" INTEGER NOT NULL,
    "TaxGroupId" INTEGER NULL,
    "InventoryControlJournalId" INTEGER NULL,
    "PurchaseOrderLineId" INTEGER NULL,
    "Quantity" decimal(18, 2) NOT NULL,
    "ReceivedQuantity" decimal(18, 2) NULL,
    "Cost" decimal(18, 2) NULL,
    "Discount" decimal(18, 2) NULL,
    "Amount" decimal(18, 2) NOT NULL,
    CONSTRAINT "FK_PurchaseInvoiceLine_InventoryControlJournal_InventoryControlJournalId" FOREIGN KEY ("InventoryControlJournalId") REFERENCES "InventoryControlJournal" ("Id"),
    CONSTRAINT "FK_PurchaseInvoiceLine_Item_ItemId" FOREIGN KEY ("ItemId") REFERENCES "Item" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_PurchaseInvoiceLine_PurchaseInvoiceHeader_PurchaseInvoiceHeaderId" FOREIGN KEY ("PurchaseInvoiceHeaderId") REFERENCES "PurchaseInvoiceHeader" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_PurchaseInvoiceLine_PurchaseOrderLine_PurchaseOrderLineId" FOREIGN KEY ("PurchaseOrderLineId") REFERENCES "PurchaseOrderLine" ("Id")
);
INSERT INTO PurchaseInvoiceLine VALUES(1,1,2,NULL,3,NULL,10,NULL,40,0,40);
INSERT INTO PurchaseInvoiceLine VALUES(2,2,2,NULL,4,NULL,8,NULL,48,0,48);
CREATE TABLE IF NOT EXISTS "PurchaseReceiptLine" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_PurchaseReceiptLine" PRIMARY KEY AUTOINCREMENT,
    "PurchaseReceiptHeaderId" INTEGER NOT NULL,
    "ItemId" INTEGER NOT NULL,
    "InventoryControlJournalId" INTEGER NULL,
    "PurchaseInvoiceLineId" INTEGER NULL,
    "MeasurementId" INTEGER NOT NULL,
    "Quantity" decimal(18, 2) NOT NULL,
    "ReceivedQuantity" decimal(18, 2) NOT NULL,
    "Cost" decimal(18, 2) NOT NULL,
    "Discount" decimal(18, 2) NOT NULL,
    "Amount" decimal(18, 2) NOT NULL,
    CONSTRAINT "FK_PurchaseReceiptLine_InventoryControlJournal_InventoryControlJournalId" FOREIGN KEY ("InventoryControlJournalId") REFERENCES "InventoryControlJournal" ("Id"),
    CONSTRAINT "FK_PurchaseReceiptLine_Item_ItemId" FOREIGN KEY ("ItemId") REFERENCES "Item" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_PurchaseReceiptLine_Measurement_MeasurementId" FOREIGN KEY ("MeasurementId") REFERENCES "Measurement" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_PurchaseReceiptLine_PurchaseInvoiceLine_PurchaseInvoiceLineId" FOREIGN KEY ("PurchaseInvoiceLineId") REFERENCES "PurchaseInvoiceLine" ("Id"),
    CONSTRAINT "FK_PurchaseReceiptLine_PurchaseReceiptHeader_PurchaseReceiptHeaderId" FOREIGN KEY ("PurchaseReceiptHeaderId") REFERENCES "PurchaseReceiptHeader" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "AspNetRoles" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AspNetRoles" PRIMARY KEY,
    "Name" TEXT NULL,
    "NormalizedName" TEXT NULL,
    "ConcurrencyStamp" TEXT NULL
);
CREATE TABLE IF NOT EXISTS "AspNetUsers" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AspNetUsers" PRIMARY KEY,
    "RefreshToken" TEXT NULL,
    "RefreshTokenExpiryTime" TEXT NOT NULL,
    "UserName" TEXT NULL,
    "NormalizedUserName" TEXT NULL,
    "Email" TEXT NULL,
    "NormalizedEmail" TEXT NULL,
    "EmailConfirmed" INTEGER NOT NULL,
    "PasswordHash" TEXT NULL,
    "SecurityStamp" TEXT NULL,
    "ConcurrencyStamp" TEXT NULL,
    "PhoneNumber" TEXT NULL,
    "PhoneNumberConfirmed" INTEGER NOT NULL,
    "TwoFactorEnabled" INTEGER NOT NULL,
    "LockoutEnd" TEXT NULL,
    "LockoutEnabled" INTEGER NOT NULL,
    "AccessFailedCount" INTEGER NOT NULL,
    "DeletedAt" TEXT NULL
);
INSERT INTO AspNetUsers VALUES('bf2ff9cc-2b08-431c-855d-934d0a96c1d9','revG+zJ92SN09LlqW7Y9lPktXJCaDQfvYr9xyM84xTA=','0001-01-01 00:00:00','admin@accountgo.ph','ADMIN@ACCOUNTGO.PH','admin@accountgo.ph','ADMIN@ACCOUNTGO.PH',1,NULL,'VCSY42UWDKCX4A6DJGKMOJNUTMT5CXUL','3bebdde7-5316-404d-8c84-2728aac4e316',NULL,0,0,NULL,1,0,NULL);
CREATE TABLE IF NOT EXISTS "RefreshTokens" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_RefreshTokens" PRIMARY KEY AUTOINCREMENT,
    "UserId" TEXT NOT NULL,
    "TokenHash" TEXT NOT NULL,
    "ExpiresAt" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "CreatedByIp" TEXT NULL,
    "RevokedAt" TEXT NULL,
    "ReplacedByTokenHash" TEXT NULL,
    "UserAgent" TEXT NULL
);
INSERT INTO RefreshTokens VALUES(1,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','aqXXSV0hq+wqSehwNsf1yzYr5ikuwVNnu5cO2Nczb4M=','2026-10-27 07:27:13.029854','2026-10-20 07:27:13.029817','::1','2026-10-20 07:27:35.772002','VM+brOeVX5gzqH2KIUQXtX2Tm3bIH/0EpjmbBxwBWr4=','curl/8.7.1');
INSERT INTO RefreshTokens VALUES(2,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','VM+brOeVX5gzqH2KIUQXtX2Tm3bIH/0EpjmbBxwBWr4=','2026-10-27 07:27:35.772226','2026-10-20 07:27:35.772226','::1','2026-10-20 07:27:39.868368',NULL,'curl/8.7.1');
INSERT INTO RefreshTokens VALUES(3,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','jfxPaTt8x83vTzIAkmRfCy89Yz4Vc3rRLZb29VbmI0s=','2026-10-27 07:33:23.770195','2026-10-20 07:33:23.770175','::1','2026-10-20 08:04:21.04906',NULL,'');
INSERT INTO RefreshTokens VALUES(4,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','xm0a2tDQ0VpB6gUr1n+ooa3z6wl+58fmZ4gEW2l2yAI=','2026-10-27 08:04:14.307344','2026-10-20 08:04:14.307328','::1','2026-10-20 08:04:18.14063','JRKzChpcSvt3EehNmLX/ytZitQHeMlRPmZFJuWqpEx8=','curl/8.7.1');
INSERT INTO RefreshTokens VALUES(5,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','JRKzChpcSvt3EehNmLX/ytZitQHeMlRPmZFJuWqpEx8=','2026-10-27 08:04:18.140972','2026-10-20 08:04:18.140972','::1','2026-10-20 08:04:21.049061',NULL,'curl/8.7.1');
INSERT INTO RefreshTokens VALUES(6,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','Gdc4H7TxXB86bhLaVPAzK/pxhPsAvX5W4FQbrmC7L3I=','2026-10-27 08:04:41.715504','2026-10-20 08:04:41.715504','::1',NULL,NULL,'');
INSERT INTO RefreshTokens VALUES(7,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','I68l1z/3YBDnILZefXz0FRdcLCOttNs/eYx2VypVPqE=','2026-10-27 08:07:35.852002','2026-10-20 08:07:35.851984','::1',NULL,NULL,'');
INSERT INTO RefreshTokens VALUES(8,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','PcA7NMq/f43LhIEmuRmTE76G22Owt2gx5/olDoQSkgo=','2026-10-27 08:26:55.789815','2026-10-20 08:26:55.789798','::1','2026-10-20 08:27:00.04862','5ckpqQVL12bUBuy+a6JrcsA0uCq23s5l5qV3N8xKd18=','curl/8.7.1');
INSERT INTO RefreshTokens VALUES(9,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','5ckpqQVL12bUBuy+a6JrcsA0uCq23s5l5qV3N8xKd18=','2026-10-27 08:27:00.048686','2026-10-20 08:27:00.048686','::1',NULL,NULL,'curl/8.7.1');
INSERT INTO RefreshTokens VALUES(10,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','buQh0/y2aSWF4DvVwMnjsC55uww/Ghkley4rWz9BVnI=','2026-10-27 08:27:17.122265','2026-10-20 08:27:17.122265','::1',NULL,NULL,'');
INSERT INTO RefreshTokens VALUES(11,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','bGvJMWvQxQTOuviHnQF7+Oczrou4Eu4K4PlUWnjNmWE=','2026-10-27 08:43:42.555421','2026-10-20 08:43:42.555402','::1',NULL,NULL,'');
INSERT INTO RefreshTokens VALUES(12,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','GpytaUAxzIdUKunjkExAtmPeQJLSF0X1d9gnIBhZM4Y=','2026-10-27 09:02:27.407557','2026-10-20 09:02:27.407533','::1',NULL,NULL,'curl/8.7.1');
INSERT INTO RefreshTokens VALUES(13,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','srIJ1aonjG6sPIPv9dco+uaq4C7FpF7qLjrJGRb+ZEE=','2026-10-27 11:52:35.900088','2026-10-20 11:52:35.900088','::1','2026-10-20 11:53:44.700469','pyWrivNh6Zfpn3Z3Aq97q/gYtq3v0mSE6XIN/S4LPp0=','curl/8.7.1');
INSERT INTO RefreshTokens VALUES(14,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','pyWrivNh6Zfpn3Z3Aq97q/gYtq3v0mSE6XIN/S4LPp0=','2026-10-27 11:53:44.700632','2026-10-20 11:53:44.700632','::1',NULL,NULL,'curl/8.7.1');
INSERT INTO RefreshTokens VALUES(15,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','/+ZoHiofRkQqH16KG9seUXflCM1xClXLHRab3wWnRRQ=','2026-10-27 11:57:47.544586','2026-10-20 11:57:47.544586','::1',NULL,NULL,'curl/8.7.1');
INSERT INTO RefreshTokens VALUES(16,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','0GooMJLDoiYvXae7qNpZisHpSEgHF8iBMQfgoPqD5e4=','2026-10-27 12:01:50.587715','2026-10-20 12:01:50.587664','::1',NULL,NULL,'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) HeadlessChrome/151.0.7922.34 Safari/537.36');
INSERT INTO RefreshTokens VALUES(17,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','bBNoPkv1ZjsFV0ATlDViKAywhjiO6tA3xA4mmy5d7LM=','2026-10-27 12:04:03.84823','2026-10-20 12:04:03.84823','::1','2026-10-20 12:04:55.032018','dOUKSLuxH7vIavHWcpRWENz59kdFyrqMWpRG24eUFe8=','Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154.0.0.0 Safari/537.36');
INSERT INTO RefreshTokens VALUES(18,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','dOUKSLuxH7vIavHWcpRWENz59kdFyrqMWpRG24eUFe8=','2026-10-27 12:04:55.03229','2026-10-20 12:04:55.032289','::1','2026-10-20 12:05:25.129648','5D7SM8LJUV4X6zDGCqYSENupW7XywPZq7MjtcY6cyac=','Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154.0.0.0 Safari/537.36');
INSERT INTO RefreshTokens VALUES(19,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','5D7SM8LJUV4X6zDGCqYSENupW7XywPZq7MjtcY6cyac=','2026-10-27 12:05:25.129651','2026-10-20 12:05:25.129651','::1','2026-10-20 12:06:52.636228','k+19idM5wOQC9E2c+RKpaOr2A/3d5vR03Exc02eOFqA=','Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154.0.0.0 Safari/537.36');
INSERT INTO RefreshTokens VALUES(20,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','k+19idM5wOQC9E2c+RKpaOr2A/3d5vR03Exc02eOFqA=','2026-10-27 12:06:52.636238','2026-10-20 12:06:52.636238','::1','2026-10-20 12:08:21.650822','OnkDinezWSGO6w4l9Sfd5KWuY/0qtWl71JmWEN+x3nU=','Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154.0.0.0 Safari/537.36');
INSERT INTO RefreshTokens VALUES(21,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','C2Nr6IfSOkWnPddMYL/C3q8jDEA0dBxCjBoVZjyv19A=','2026-10-27 12:07:49.278491','2026-10-20 12:07:49.27844','::1',NULL,NULL,'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) HeadlessChrome/151.0.7922.34 Safari/537.36');
INSERT INTO RefreshTokens VALUES(22,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','OnkDinezWSGO6w4l9Sfd5KWuY/0qtWl71JmWEN+x3nU=','2026-10-27 12:08:21.651592','2026-10-20 12:08:21.651592','::1','2026-10-20 12:08:23.380417','NIkYZCbt5G4nejlBPQTkAmRn/Iorb0gvjjIDAzL8ds8=','Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154.0.0.0 Safari/537.36');
INSERT INTO RefreshTokens VALUES(23,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','NIkYZCbt5G4nejlBPQTkAmRn/Iorb0gvjjIDAzL8ds8=','2026-10-27 12:08:23.380419','2026-10-20 12:08:23.380419','::1','2026-10-20 12:08:24.855459','ONtXJ9+UXS+6q+72jtnVxpxQ6fPq0is/Y/y7ZWi+OCc=','Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154.0.0.0 Safari/537.36');
INSERT INTO RefreshTokens VALUES(24,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','ONtXJ9+UXS+6q+72jtnVxpxQ6fPq0is/Y/y7ZWi+OCc=','2026-10-27 12:08:24.855461','2026-10-20 12:08:24.85546','::1','2026-10-20 12:08:28.229297',NULL,'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154.0.0.0 Safari/537.36');
INSERT INTO RefreshTokens VALUES(25,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','C9vvD1TLyY1XAj94DZ2ZugfnYOJ+BSm9UUnmbmAXtK8=','2026-10-27 12:10:38.454822','2026-10-20 12:10:38.454822','::1','2026-10-20 12:13:37.965659','GYCjqHWSjug9ssccivfiA4E86TK30wOhuCde7QvbWkE=','Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154.0.0.0 Safari/537.36');
INSERT INTO RefreshTokens VALUES(26,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','GYCjqHWSjug9ssccivfiA4E86TK30wOhuCde7QvbWkE=','2026-10-27 12:13:37.965819','2026-10-20 12:13:37.965819','::1','2026-10-20 12:36:19.370253','ADIPNols/1mT5+rjTkBfMJ3U7D7e1Kj0roL+tD3MtkQ=','Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154.0.0.0 Safari/537.36');
INSERT INTO RefreshTokens VALUES(27,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','55AgzWZPjyKlrjntNAfN7XwcCWk0jC4GYYPGaeX6PLs=','2026-10-27 12:14:11.138453','2026-10-20 12:14:11.138453','::1',NULL,NULL,'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) HeadlessChrome/151.0.7922.34 Safari/537.36');
INSERT INTO RefreshTokens VALUES(28,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','ADIPNols/1mT5+rjTkBfMJ3U7D7e1Kj0roL+tD3MtkQ=','2026-10-27 12:36:19.370738','2026-10-20 12:36:19.370738','::1','2026-10-20 12:46:35.271934','7xaBuSFn8WIPnonJuJQ3DTcfAYfT0ICYP8/v5rVIKiA=','Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154.0.0.0 Safari/537.36');
INSERT INTO RefreshTokens VALUES(29,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','7xaBuSFn8WIPnonJuJQ3DTcfAYfT0ICYP8/v5rVIKiA=','2026-10-27 12:46:35.272597','2026-10-20 12:46:35.272549','::1','2026-10-20 12:46:40.755335','+0KCQfluiLq91eNJIzp1gWbwFo0Lkh7GekEst0Hrlnk=','Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154.0.0.0 Safari/537.36');
INSERT INTO RefreshTokens VALUES(30,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','+0KCQfluiLq91eNJIzp1gWbwFo0Lkh7GekEst0Hrlnk=','2026-10-27 12:46:40.755337','2026-10-20 12:46:40.755337','::1','2026-10-20 12:47:09.815465','oYX4OIi9rUCbg3kf4OYBE37lQzeb4zZ+Gko++PjM2VQ=','Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154.0.0.0 Safari/537.36');
INSERT INTO RefreshTokens VALUES(31,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','oYX4OIi9rUCbg3kf4OYBE37lQzeb4zZ+Gko++PjM2VQ=','2026-10-27 12:47:09.815467','2026-10-20 12:47:09.815467','::1','2026-10-20 12:47:20.613749','hbpgy/qSYVg04+F1ZTe3LSanDbe59ZxO0oOvFMeY67Y=','Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154.0.0.0 Safari/537.36');
INSERT INTO RefreshTokens VALUES(32,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','hbpgy/qSYVg04+F1ZTe3LSanDbe59ZxO0oOvFMeY67Y=','2026-10-27 12:47:20.613751','2026-10-20 12:47:20.613751','::1','2026-10-20 12:47:28.834018','IMRv7T2ntShDAjZucqGqA+5MYGTBL1MVHmQlkr5/YdU=','Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154.0.0.0 Safari/537.36');
INSERT INTO RefreshTokens VALUES(33,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','IMRv7T2ntShDAjZucqGqA+5MYGTBL1MVHmQlkr5/YdU=','2026-10-27 12:47:28.834021','2026-10-20 12:47:28.834021','::1',NULL,NULL,'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154.0.0.0 Safari/537.36');
INSERT INTO RefreshTokens VALUES(34,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','bgtTTaEwtlbcRaYanQd/jdBl43YgbXOTKTAae0xIRNw=','2026-10-27 12:53:02.763526','2026-10-20 12:53:02.763526','::1','2026-10-20 12:53:03.777273','Rkc2Y5p+dNwnq34GfxnZdavVMQh8E83Od4xBTtVxvQI=','Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) HeadlessChrome/151.0.7922.34 Safari/537.36');
INSERT INTO RefreshTokens VALUES(35,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','Rkc2Y5p+dNwnq34GfxnZdavVMQh8E83Od4xBTtVxvQI=','2026-10-27 12:53:03.777352','2026-10-20 12:53:03.777351','::1','2026-10-20 12:53:06.695929','rCEJJNtZjoE5AF/eWJme37VpLKu+T8yzQXJx0WabZfw=','Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) HeadlessChrome/151.0.7922.34 Safari/537.36');
INSERT INTO RefreshTokens VALUES(36,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','rCEJJNtZjoE5AF/eWJme37VpLKu+T8yzQXJx0WabZfw=','2026-10-27 12:53:06.69593','2026-10-20 12:53:06.69593','::1','2026-10-20 12:53:08.12732','ORKYpDK8lr0zlzGjVK9MhOLTSwmwVFBkpcFls3g+xkc=','Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) HeadlessChrome/151.0.7922.34 Safari/537.36');
INSERT INTO RefreshTokens VALUES(37,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','ORKYpDK8lr0zlzGjVK9MhOLTSwmwVFBkpcFls3g+xkc=','2026-10-27 12:53:08.127321','2026-10-20 12:53:08.127321','::1','2026-10-20 12:53:09.853925','3MEM2nqQ7TLjD1r09udgylazgOusTSypPsMOV4r8YwQ=','Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) HeadlessChrome/151.0.7922.34 Safari/537.36');
INSERT INTO RefreshTokens VALUES(38,'bf2ff9cc-2b08-431c-855d-934d0a96c1d9','3MEM2nqQ7TLjD1r09udgylazgOusTSypPsMOV4r8YwQ=','2026-10-27 12:53:09.853926','2026-10-20 12:53:09.853926','::1',NULL,NULL,'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) HeadlessChrome/151.0.7922.34 Safari/537.36');
CREATE TABLE IF NOT EXISTS "AspNetRoleClaims" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AspNetRoleClaims" PRIMARY KEY AUTOINCREMENT,
    "RoleId" TEXT NOT NULL,
    "ClaimType" TEXT NULL,
    "ClaimValue" TEXT NULL,
    CONSTRAINT "FK_AspNetRoleClaims_AspNetRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "AspNetRoles" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "AspNetUserClaims" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AspNetUserClaims" PRIMARY KEY AUTOINCREMENT,
    "UserId" TEXT NOT NULL,
    "ClaimType" TEXT NULL,
    "ClaimValue" TEXT NULL,
    CONSTRAINT "FK_AspNetUserClaims_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "AspNetUserLogins" (
    "LoginProvider" TEXT NOT NULL,
    "ProviderKey" TEXT NOT NULL,
    "ProviderDisplayName" TEXT NULL,
    "UserId" TEXT NOT NULL,
    CONSTRAINT "PK_AspNetUserLogins" PRIMARY KEY ("LoginProvider", "ProviderKey"),
    CONSTRAINT "FK_AspNetUserLogins_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "AspNetUserRoles" (
    "UserId" TEXT NOT NULL,
    "RoleId" TEXT NOT NULL,
    CONSTRAINT "PK_AspNetUserRoles" PRIMARY KEY ("UserId", "RoleId"),
    CONSTRAINT "FK_AspNetUserRoles_AspNetRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "AspNetRoles" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_AspNetUserRoles_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "AspNetUserTokens" (
    "UserId" TEXT NOT NULL,
    "LoginProvider" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "Value" TEXT NULL,
    CONSTRAINT "PK_AspNetUserTokens" PRIMARY KEY ("UserId", "LoginProvider", "Name"),
    CONSTRAINT "FK_AspNetUserTokens_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
);
INSERT INTO sqlite_sequence VALUES('Company',1);
INSERT INTO sqlite_sequence VALUES('Donors',2);
INSERT INTO sqlite_sequence VALUES('AccountClass',6);
INSERT INTO sqlite_sequence VALUES('Account',93);
INSERT INTO sqlite_sequence VALUES('FinancialYear',1);
INSERT INTO sqlite_sequence VALUES('PaymentTerm',5);
INSERT INTO sqlite_sequence VALUES('GeneralLedgerSetting',1);
INSERT INTO sqlite_sequence VALUES('ItemTaxGroup',2);
INSERT INTO sqlite_sequence VALUES('Tax',3);
INSERT INTO sqlite_sequence VALUES('TaxGroup',2);
INSERT INTO sqlite_sequence VALUES('ItemTaxGroupTax',2);
INSERT INTO sqlite_sequence VALUES('TaxGroupTax',3);
INSERT INTO sqlite_sequence VALUES('SequenceNumber',9);
INSERT INTO sqlite_sequence VALUES('Party',8);
INSERT INTO sqlite_sequence VALUES('Contact',4);
INSERT INTO sqlite_sequence VALUES('Vendor',2);
INSERT INTO sqlite_sequence VALUES('Customer',2);
INSERT INTO sqlite_sequence VALUES('Measurement',4);
INSERT INTO sqlite_sequence VALUES('ItemCategory',4);
INSERT INTO sqlite_sequence VALUES('Item',3);
INSERT INTO sqlite_sequence VALUES('Bank',2);
INSERT INTO sqlite_sequence VALUES('SecurityRole',2);
INSERT INTO sqlite_sequence VALUES('GeneralLedgerHeader',6);
INSERT INTO sqlite_sequence VALUES('InventoryControlJournal',4);
INSERT INTO sqlite_sequence VALUES('GeneralLedgerLine',18);
INSERT INTO sqlite_sequence VALUES('SalesInvoiceHeader',2);
INSERT INTO sqlite_sequence VALUES('SalesInvoiceLine',2);
INSERT INTO sqlite_sequence VALUES('SalesReceiptHeader',1);
INSERT INTO sqlite_sequence VALUES('SalesReceiptLine',1);
INSERT INTO sqlite_sequence VALUES('PurchaseInvoiceHeader',2);
INSERT INTO sqlite_sequence VALUES('PurchaseInvoiceLine',2);
INSERT INTO sqlite_sequence VALUES('PurchaseOrderHeader',2);
INSERT INTO sqlite_sequence VALUES('PurchaseOrderLine',2);
INSERT INTO sqlite_sequence VALUES('VendorPayment',1);
INSERT INTO sqlite_sequence VALUES('DonationInvoiceHeader',4);
INSERT INTO sqlite_sequence VALUES('User',1);
INSERT INTO sqlite_sequence VALUES('SecurityUserRole',2);
INSERT INTO sqlite_sequence VALUES('RefreshTokens',38);
INSERT INTO sqlite_sequence VALUES('SecurityGroup',8);
INSERT INTO sqlite_sequence VALUES('SecurityPermission',32);
CREATE INDEX "IX_Account_AccountClassId" ON "Account" ("AccountClassId");
CREATE INDEX "IX_Account_CompanyId" ON "Account" ("CompanyId");
CREATE INDEX "IX_Account_ParentAccountId" ON "Account" ("ParentAccountId");
CREATE INDEX "IX_AuditableAttribute_AuditableEntityId" ON "AuditableAttribute" ("AuditableEntityId");
CREATE INDEX "IX_Bank_AccountId" ON "Bank" ("AccountId");
CREATE INDEX "IX_CompanySetting_CompanyId" ON "CompanySetting" ("CompanyId");
CREATE INDEX "IX_Contact_PartyId" ON "Contact" ("PartyId");
CREATE INDEX "IX_Customer_AccountsReceivableAccountId" ON "Customer" ("AccountsReceivableAccountId");
CREATE INDEX "IX_Customer_CustomerAdvancesAccountId" ON "Customer" ("CustomerAdvancesAccountId");
CREATE INDEX "IX_Customer_PartyId" ON "Customer" ("PartyId");
CREATE INDEX "IX_Customer_PaymentTermId" ON "Customer" ("PaymentTermId");
CREATE INDEX "IX_Customer_PrimaryContactId" ON "Customer" ("PrimaryContactId");
CREATE INDEX "IX_Customer_PromptPaymentDiscountAccountId" ON "Customer" ("PromptPaymentDiscountAccountId");
CREATE INDEX "IX_Customer_SalesAccountId" ON "Customer" ("SalesAccountId");
CREATE INDEX "IX_Customer_SalesDiscountAccountId" ON "Customer" ("SalesDiscountAccountId");
CREATE INDEX "IX_Customer_TaxGroupId" ON "Customer" ("TaxGroupId");
CREATE INDEX "IX_CustomerAllocation_CustomerId" ON "CustomerAllocation" ("CustomerId");
CREATE INDEX "IX_CustomerAllocation_SalesInvoiceHeaderId" ON "CustomerAllocation" ("SalesInvoiceHeaderId");
CREATE INDEX "IX_CustomerAllocation_SalesReceiptHeaderId" ON "CustomerAllocation" ("SalesReceiptHeaderId");
CREATE INDEX "IX_CustomerContact_ContactId" ON "CustomerContact" ("ContactId");
CREATE INDEX "IX_CustomerContact_CustomerId" ON "CustomerContact" ("CustomerId");
CREATE INDEX "IX_DonationInvoiceHeader_DonorId" ON "DonationInvoiceHeader" ("DonorId");
CREATE INDEX "IX_DonationInvoiceHeader_GlAccountId" ON "DonationInvoiceHeader" ("GlAccountId");
CREATE INDEX "IX_DonationInvoiceLine_DonationInvoiceHeaderId" ON "DonationInvoiceLine" ("DonationInvoiceHeaderId");
CREATE INDEX "IX_DonationInvoiceLine_ItemId" ON "DonationInvoiceLine" ("ItemId");
CREATE INDEX "IX_DonationInvoiceLine_MeasurementId" ON "DonationInvoiceLine" ("MeasurementId");
CREATE INDEX "IX_GeneralLedgerLine_AccountId" ON "GeneralLedgerLine" ("AccountId");
CREATE INDEX "IX_GeneralLedgerLine_GeneralLedgerHeaderId" ON "GeneralLedgerLine" ("GeneralLedgerHeaderId");
CREATE INDEX "IX_GeneralLedgerSetting_CompanyId" ON "GeneralLedgerSetting" ("CompanyId");
CREATE INDEX "IX_GeneralLedgerSetting_GoodsReceiptNoteClearingAccountId" ON "GeneralLedgerSetting" ("GoodsReceiptNoteClearingAccountId");
CREATE INDEX "IX_GeneralLedgerSetting_IncomeSummaryAccountId" ON "GeneralLedgerSetting" ("IncomeSummaryAccountId");
CREATE INDEX "IX_GeneralLedgerSetting_PayableAccountId" ON "GeneralLedgerSetting" ("PayableAccountId");
CREATE INDEX "IX_GeneralLedgerSetting_PermanentAccountId" ON "GeneralLedgerSetting" ("PermanentAccountId");
CREATE INDEX "IX_GeneralLedgerSetting_PurchaseDiscountAccountId" ON "GeneralLedgerSetting" ("PurchaseDiscountAccountId");
CREATE INDEX "IX_GeneralLedgerSetting_SalesDiscountAccountId" ON "GeneralLedgerSetting" ("SalesDiscountAccountId");
CREATE INDEX "IX_GeneralLedgerSetting_ShippingChargeAccountId" ON "GeneralLedgerSetting" ("ShippingChargeAccountId");
CREATE INDEX "IX_InventoryControlJournal_ItemId" ON "InventoryControlJournal" ("ItemId");
CREATE INDEX "IX_InventoryControlJournal_MeasurementId" ON "InventoryControlJournal" ("MeasurementId");
CREATE INDEX "IX_Item_CostOfGoodsSoldAccountId" ON "Item" ("CostOfGoodsSoldAccountId");
CREATE INDEX "IX_Item_InventoryAccountId" ON "Item" ("InventoryAccountId");
CREATE INDEX "IX_Item_InventoryAdjustmentAccountId" ON "Item" ("InventoryAdjustmentAccountId");
CREATE INDEX "IX_Item_ItemCategoryId" ON "Item" ("ItemCategoryId");
CREATE INDEX "IX_Item_ItemTaxGroupId" ON "Item" ("ItemTaxGroupId");
CREATE INDEX "IX_Item_PreferredVendorId" ON "Item" ("PreferredVendorId");
CREATE INDEX "IX_Item_PurchaseMeasurementId" ON "Item" ("PurchaseMeasurementId");
CREATE INDEX "IX_Item_SalesAccountId" ON "Item" ("SalesAccountId");
CREATE INDEX "IX_Item_SellMeasurementId" ON "Item" ("SellMeasurementId");
CREATE INDEX "IX_Item_SmallestMeasurementId" ON "Item" ("SmallestMeasurementId");
CREATE INDEX "IX_ItemCategory_AdjustmentAccountId" ON "ItemCategory" ("AdjustmentAccountId");
CREATE INDEX "IX_ItemCategory_AssemblyAccountId" ON "ItemCategory" ("AssemblyAccountId");
CREATE INDEX "IX_ItemCategory_CostOfGoodsSoldAccountId" ON "ItemCategory" ("CostOfGoodsSoldAccountId");
CREATE INDEX "IX_ItemCategory_InventoryAccountId" ON "ItemCategory" ("InventoryAccountId");
CREATE INDEX "IX_ItemCategory_MeasurementId" ON "ItemCategory" ("MeasurementId");
CREATE INDEX "IX_ItemCategory_SalesAccountId" ON "ItemCategory" ("SalesAccountId");
CREATE INDEX "IX_ItemTaxGroupTax_ItemTaxGroupId" ON "ItemTaxGroupTax" ("ItemTaxGroupId");
CREATE INDEX "IX_ItemTaxGroupTax_TaxId" ON "ItemTaxGroupTax" ("TaxId");
CREATE INDEX "IX_JournalEntryHeader_GeneralLedgerHeaderId" ON "JournalEntryHeader" ("GeneralLedgerHeaderId");
CREATE INDEX "IX_JournalEntryHeader_PartyId" ON "JournalEntryHeader" ("PartyId");
CREATE INDEX "IX_JournalEntryLine_AccountId" ON "JournalEntryLine" ("AccountId");
CREATE INDEX "IX_JournalEntryLine_JournalEntryHeaderId" ON "JournalEntryLine" ("JournalEntryHeaderId");
CREATE UNIQUE INDEX "IX_MainContraAccount_MainAccountId" ON "MainContraAccount" ("MainAccountId");
CREATE INDEX "IX_MainContraAccount_RelatedContraAccountId" ON "MainContraAccount" ("RelatedContraAccountId");
CREATE INDEX "IX_PurchaseInvoiceHeader_GeneralLedgerHeaderId" ON "PurchaseInvoiceHeader" ("GeneralLedgerHeaderId");
CREATE INDEX "IX_PurchaseInvoiceHeader_VendorId" ON "PurchaseInvoiceHeader" ("VendorId");
CREATE INDEX "IX_PurchaseInvoiceLine_InventoryControlJournalId" ON "PurchaseInvoiceLine" ("InventoryControlJournalId");
CREATE INDEX "IX_PurchaseInvoiceLine_ItemId" ON "PurchaseInvoiceLine" ("ItemId");
CREATE INDEX "IX_PurchaseInvoiceLine_PurchaseInvoiceHeaderId" ON "PurchaseInvoiceLine" ("PurchaseInvoiceHeaderId");
CREATE INDEX "IX_PurchaseInvoiceLine_PurchaseOrderLineId" ON "PurchaseInvoiceLine" ("PurchaseOrderLineId");
CREATE INDEX "IX_PurchaseOrderHeader_PurchaseInvoiceHeaderId" ON "PurchaseOrderHeader" ("PurchaseInvoiceHeaderId");
CREATE INDEX "IX_PurchaseOrderHeader_VendorId" ON "PurchaseOrderHeader" ("VendorId");
CREATE INDEX "IX_PurchaseOrderLine_ItemId" ON "PurchaseOrderLine" ("ItemId");
CREATE INDEX "IX_PurchaseOrderLine_PurchaseOrderHeaderId" ON "PurchaseOrderLine" ("PurchaseOrderHeaderId");
CREATE INDEX "IX_PurchaseReceiptHeader_GeneralLedgerHeaderId" ON "PurchaseReceiptHeader" ("GeneralLedgerHeaderId");
CREATE INDEX "IX_PurchaseReceiptHeader_VendorId" ON "PurchaseReceiptHeader" ("VendorId");
CREATE INDEX "IX_PurchaseReceiptLine_InventoryControlJournalId" ON "PurchaseReceiptLine" ("InventoryControlJournalId");
CREATE INDEX "IX_PurchaseReceiptLine_ItemId" ON "PurchaseReceiptLine" ("ItemId");
CREATE INDEX "IX_PurchaseReceiptLine_MeasurementId" ON "PurchaseReceiptLine" ("MeasurementId");
CREATE INDEX "IX_PurchaseReceiptLine_PurchaseInvoiceLineId" ON "PurchaseReceiptLine" ("PurchaseInvoiceLineId");
CREATE INDEX "IX_PurchaseReceiptLine_PurchaseReceiptHeaderId" ON "PurchaseReceiptLine" ("PurchaseReceiptHeaderId");
CREATE INDEX "IX_SalesDeliveryHeader_CustomerId" ON "SalesDeliveryHeader" ("CustomerId");
CREATE INDEX "IX_SalesDeliveryHeader_GeneralLedgerHeaderId" ON "SalesDeliveryHeader" ("GeneralLedgerHeaderId");
CREATE INDEX "IX_SalesDeliveryHeader_PaymentTermId" ON "SalesDeliveryHeader" ("PaymentTermId");
CREATE INDEX "IX_SalesDeliveryLine_ItemId" ON "SalesDeliveryLine" ("ItemId");
CREATE INDEX "IX_SalesDeliveryLine_MeasurementId" ON "SalesDeliveryLine" ("MeasurementId");
CREATE INDEX "IX_SalesDeliveryLine_SalesDeliveryHeaderId" ON "SalesDeliveryLine" ("SalesDeliveryHeaderId");
CREATE INDEX "IX_SalesDeliveryLine_SalesInvoiceLineId" ON "SalesDeliveryLine" ("SalesInvoiceLineId");
CREATE INDEX "IX_SalesInvoiceHeader_CustomerId" ON "SalesInvoiceHeader" ("CustomerId");
CREATE INDEX "IX_SalesInvoiceHeader_GeneralLedgerHeaderId" ON "SalesInvoiceHeader" ("GeneralLedgerHeaderId");
CREATE INDEX "IX_SalesInvoiceLine_InventoryControlJournalId" ON "SalesInvoiceLine" ("InventoryControlJournalId");
CREATE INDEX "IX_SalesInvoiceLine_ItemId" ON "SalesInvoiceLine" ("ItemId");
CREATE INDEX "IX_SalesInvoiceLine_MeasurementId" ON "SalesInvoiceLine" ("MeasurementId");
CREATE INDEX "IX_SalesInvoiceLine_SalesInvoiceHeaderId" ON "SalesInvoiceLine" ("SalesInvoiceHeaderId");
CREATE INDEX "IX_SalesInvoiceLine_SalesOrderLineId" ON "SalesInvoiceLine" ("SalesOrderLineId");
CREATE INDEX "IX_SalesOrderHeader_CustomerId" ON "SalesOrderHeader" ("CustomerId");
CREATE INDEX "IX_SalesOrderHeader_PaymentTermId" ON "SalesOrderHeader" ("PaymentTermId");
CREATE INDEX "IX_SalesOrderLine_ItemId" ON "SalesOrderLine" ("ItemId");
CREATE INDEX "IX_SalesOrderLine_MeasurementId" ON "SalesOrderLine" ("MeasurementId");
CREATE INDEX "IX_SalesOrderLine_SalesOrderHeaderId" ON "SalesOrderLine" ("SalesOrderHeaderId");
CREATE INDEX "IX_SalesQuoteHeader_CustomerId" ON "SalesQuoteHeader" ("CustomerId");
CREATE INDEX "IX_SalesQuoteLine_ItemId" ON "SalesQuoteLine" ("ItemId");
CREATE INDEX "IX_SalesQuoteLine_MeasurementId" ON "SalesQuoteLine" ("MeasurementId");
CREATE INDEX "IX_SalesQuoteLine_SalesQuoteHeaderId" ON "SalesQuoteLine" ("SalesQuoteHeaderId");
CREATE INDEX "IX_SalesReceiptHeader_AccountToDebitId" ON "SalesReceiptHeader" ("AccountToDebitId");
CREATE INDEX "IX_SalesReceiptHeader_CustomerId" ON "SalesReceiptHeader" ("CustomerId");
CREATE INDEX "IX_SalesReceiptHeader_GeneralLedgerHeaderId" ON "SalesReceiptHeader" ("GeneralLedgerHeaderId");
CREATE INDEX "IX_SalesReceiptLine_AccountToCreditId" ON "SalesReceiptLine" ("AccountToCreditId");
CREATE INDEX "IX_SalesReceiptLine_ItemId" ON "SalesReceiptLine" ("ItemId");
CREATE INDEX "IX_SalesReceiptLine_MeasurementId" ON "SalesReceiptLine" ("MeasurementId");
CREATE INDEX "IX_SalesReceiptLine_SalesInvoiceLineId" ON "SalesReceiptLine" ("SalesInvoiceLineId");
CREATE INDEX "IX_SalesReceiptLine_SalesReceiptHeaderId" ON "SalesReceiptLine" ("SalesReceiptHeaderId");
CREATE INDEX "IX_SecurityPermission_SecurityGroupId" ON "SecurityPermission" ("SecurityGroupId");
CREATE INDEX "IX_SecurityRolePermission_SecurityPermissionId" ON "SecurityRolePermission" ("SecurityPermissionId");
CREATE INDEX "IX_SecurityRolePermission_SecurityRoleId" ON "SecurityRolePermission" ("SecurityRoleId");
CREATE INDEX "IX_SecurityUserRole_SecurityRoleId" ON "SecurityUserRole" ("SecurityRoleId");
CREATE INDEX "IX_SecurityUserRole_UserId" ON "SecurityUserRole" ("UserId");
CREATE INDEX "IX_Tax_PurchasingAccountId" ON "Tax" ("PurchasingAccountId");
CREATE INDEX "IX_Tax_SalesAccountId" ON "Tax" ("SalesAccountId");
CREATE INDEX "IX_TaxGroupTax_TaxGroupId" ON "TaxGroupTax" ("TaxGroupId");
CREATE INDEX "IX_TaxGroupTax_TaxId" ON "TaxGroupTax" ("TaxId");
CREATE INDEX "IX_Vendor_AccountsPayableAccountId" ON "Vendor" ("AccountsPayableAccountId");
CREATE INDEX "IX_Vendor_PartyId" ON "Vendor" ("PartyId");
CREATE INDEX "IX_Vendor_PaymentTermId" ON "Vendor" ("PaymentTermId");
CREATE INDEX "IX_Vendor_PrimaryContactId" ON "Vendor" ("PrimaryContactId");
CREATE INDEX "IX_Vendor_PurchaseAccountId" ON "Vendor" ("PurchaseAccountId");
CREATE INDEX "IX_Vendor_PurchaseDiscountAccountId" ON "Vendor" ("PurchaseDiscountAccountId");
CREATE INDEX "IX_Vendor_TaxGroupId" ON "Vendor" ("TaxGroupId");
CREATE INDEX "IX_VendorContact_ContactId" ON "VendorContact" ("ContactId");
CREATE INDEX "IX_VendorContact_VendorId" ON "VendorContact" ("VendorId");
CREATE INDEX "IX_VendorPayment_GeneralLedgerHeaderId" ON "VendorPayment" ("GeneralLedgerHeaderId");
CREATE INDEX "IX_VendorPayment_PurchaseInvoiceHeaderId" ON "VendorPayment" ("PurchaseInvoiceHeaderId");
CREATE INDEX "IX_VendorPayment_VendorId" ON "VendorPayment" ("VendorId");
CREATE INDEX "IX_AspNetRoleClaims_RoleId" ON "AspNetRoleClaims" ("RoleId");
CREATE UNIQUE INDEX "RoleNameIndex" ON "AspNetRoles" ("NormalizedName");
CREATE INDEX "IX_AspNetUserClaims_UserId" ON "AspNetUserClaims" ("UserId");
CREATE INDEX "IX_AspNetUserLogins_UserId" ON "AspNetUserLogins" ("UserId");
CREATE INDEX "IX_AspNetUserRoles_RoleId" ON "AspNetUserRoles" ("RoleId");
CREATE INDEX "EmailIndex" ON "AspNetUsers" ("NormalizedEmail");
CREATE UNIQUE INDEX "UserNameIndex" ON "AspNetUsers" ("NormalizedUserName");
CREATE INDEX "IX_RefreshTokens_TokenHash" ON "RefreshTokens" ("TokenHash");
CREATE INDEX "IX_RefreshTokens_UserId" ON "RefreshTokens" ("UserId");

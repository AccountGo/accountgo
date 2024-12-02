-- Add audit data for the Company table
INSERT INTO [dbo].[AuditableEntity] ([EntityName], [EnableAudit]) VALUES ('Company', 1);

DECLARE @auditableEntityId INT;
SELECT @auditableEntityId = [Id] FROM [dbo].[AuditableEntity] WHERE [EntityName] = 'Company';

-- Add attributes for the Company table
INSERT INTO [dbo].[AuditableAttribute] ([AuditableEntityId], [AttributeName], [EnableAudit])
VALUES
    (@auditableEntityId, 'CompanyCode', 1),
    (@auditableEntityId, 'Name', 1),
    (@auditableEntityId, 'ShortName', 1),
    (@auditableEntityId, 'CRA', 1);
CREATE TABLE [dbo].[CompanySetting] (
    [Id]         INT            IDENTITY (1, 1) NOT NULL,
    [CompanyId]  INT            NOT NULL,
    CONSTRAINT [PK_dbo.CompanySetting] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_dbo.CompanySetting_dbo.Company_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [dbo].[Company] ([Id]) ON DELETE CASCADE
);


GO
CREATE NONCLUSTERED INDEX [IX_CompanyId]
    ON [dbo].[CompanySetting]([CompanyId] ASC);


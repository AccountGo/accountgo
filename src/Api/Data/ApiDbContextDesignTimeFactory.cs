using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Api.Data;

/// <summary>Builds the SQL Server migration model without reading deployment configuration or contacting a database.</summary>
public sealed class ApiDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ApiDbContext>
{
    public ApiDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<ApiDbContext>()
        .UseSqlServer("Server=localhost;Database=accountgo_design;Trusted_Connection=True").Options);
}

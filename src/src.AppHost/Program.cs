using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<SqlServerDatabaseResource>? sqlServer = null;

// Get the architecture of the current process
Architecture arch = RuntimeInformation.ProcessArchitecture;

// Attempt to enable Azure SQL Edge Server for Arm64 development environment
if (builder.Environment.IsDevelopment() && arch == Architecture.Arm64)
{
    try
    {
        sqlServer = builder.AddSqlServer("gdb-sql-server")
                    .WithImageRegistry("mcr.microsoft.com")
                    .WithImage("azure-sql-edge")
                    .WithImageTag("latest")
                    .WithEnvironment("ACCEPT_EULA", "Y")
                    .WithEnvironment("MSSQL_PID", "Premium")
                    .WithImagePullPolicy(ImagePullPolicy.Missing)
                    .AddDatabase("gdb-db");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to create an ARM64-based SQL server container using Azure Edge Image: {ex.GetType().Name} - {ex.Message}");
        Console.WriteLine("Retrying to obtain a basic SQL server container");
        sqlServer = null;
    }
}

if (sqlServer == null)
{
    // Using SQL Server Enterprise Development Edition for developmental (non-commercial usage)
    if (builder.Environment.IsDevelopment())
    {
        sqlServer = builder.AddSqlServer("gdb-sql-server")
                .WithImageRegistry("mcr.microsoft.com")
                .WithImage("mssql/server") // Using Ubuntu based image
                .WithImageTag("latest")
                .WithEnvironment("ACCEPT_EULA", "Y")
                .WithEnvironment("MSSQL_PID", "Premium")
                .WithImagePullPolicy(ImagePullPolicy.Missing) // Only pull the image if Docker does not have the image locally
                .AddDatabase("gdb-db");
    }
    // Using SQL Server Express Edition for production usage (Express edition can be used for commercial usage at free cost, with limitation to table size)
    else
    {
        sqlServer = builder.AddSqlServer("gdb-sql-server")
                .WithImageRegistry("mcr.microsoft.com")
                .WithImage("mssql/server")
                .WithImageTag("latest")
                .WithEnvironment("ACCEPT_EULA", "Y")
                .WithEnvironment("MSSQL_PID", "Express")
                .WithImagePullPolicy(ImagePullPolicy.Missing)
                .AddDatabase("gdb-db");
    }
}
// Only try to create a container if sqlServer is null
sqlServer ??= builder.AddSqlServer("gdb-sql-server")
                .WithImageRegistry("mcr.microsoft.com")
                .WithImage("mssql/server") // Using Ubuntu based image
                .WithImageTag("latest")
                .WithEnvironment("ACCEPT_EULA", "Y")
                .WithImagePullPolicy(ImagePullPolicy.Missing) // Only pull the image if Docker does not have the image locally
                .AddDatabase("gdb-db");

// read environment variable for connection string
var apiService = builder.AddProject<Projects.Api>("api")
        .WithReference(sqlServer)
        .WithHttpEndpoint(port: 8001, name: "api-http")
        .WaitFor(sqlServer);

builder.AddProject<Projects.AccountGoWeb>("mvc")
        .WithHttpEndpoint(port: 8000, name: "mvc-http")
        .WithReference(apiService)
        .WaitFor(apiService);

builder.AddProject<Projects.BlazorGDB>("blazor")
        .WithHttpEndpoint(port: 8002, name: "blazor-http")
        .WithReference(apiService)
        .WaitFor(apiService);

builder.AddProject<Projects.MigrationService>("migrations")
    .WithReference(sqlServer)
    .WaitFor(sqlServer);

builder.Build().Run();

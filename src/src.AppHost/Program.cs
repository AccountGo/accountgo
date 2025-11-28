using System.Reflection;
using Google.Protobuf.WellKnownTypes;

var builder = DistributedApplication.CreateBuilder(args);

var sqlServer = builder.AddSqlServer("gdb-sql-server")
                 .AddDatabase("gdb-db");

// var sqlServer = builder.AddSqlServer("gdb-sql-server")
//     .WithImageRegistry("mcr.microsoft.com")
//     .WithImage("mssql/server", "2022-latest")
//     .WithEnvironment("ACCEPT_EULA", "Y")
//     .WithEnvironment("MSSQL_SA_PASSWORD", "YourStrong!Passw0rd")
//     .AddDatabase("gdb-db");

// read environment variable for connection string
var apiService = builder.AddProject<Projects.Api>("api")
        .WithReference(sqlServer)
        .WithHttpEndpoint(port: 8001, name: "api-http")
        .WaitFor(sqlServer);

builder.AddProject<Projects.AccountGoWeb>("mvc")
        .WithHttpEndpoint(port: 8000, name: "mvc-http")
        .WithReference(apiService)
        .WaitFor(apiService);

builder.AddProject<Projects.MigrationService>("migrations")
    .WithReference(sqlServer)
    .WaitFor(sqlServer);

builder.Build().Run();

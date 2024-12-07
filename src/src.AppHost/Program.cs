using System.Reflection;
using Google.Protobuf.WellKnownTypes;

var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("gdb-sql-server")
                 .AddDatabase("gdb-db");

// read environment variable for connection string
var api = builder.AddProject<Projects.Api>("api")
        .WithReference(sql)
        .WithHttpEndpoint(port: 8001);

builder.AddProject<Projects.AccountGoWeb>("mvc")
        .WithHttpEndpoint(port: 8000)
        .WithReference(api);

builder.AddProject<Projects.MigrationService>("migrations")
    .WithReference(sql);

builder.Build().Run();


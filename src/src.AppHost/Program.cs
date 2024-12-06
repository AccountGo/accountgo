using System.Reflection;
using Google.Protobuf.WellKnownTypes;

var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.Api>("api")
        .WithHttpEndpoint(port: 8001);

builder.AddProject<Projects.AccountGoWeb>("mvc")
        .WithHttpEndpoint(port: 8000)
        .WithReference(api);

builder.Build().Run();


using System.Reflection;
using Google.Protobuf.WellKnownTypes;

var builder = DistributedApplication.CreateBuilder(args);



var api = builder.AddProject<Projects.Api>("api");



builder.AddProject<Projects.AccountGoWeb>("mvc")
        .WithReference(api);
        

builder.Build().Run();


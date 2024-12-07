using Api.Data;
using MigrationService;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHostedService<Worker>();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(Worker.ActivitySourceName));

builder.AddSqlServerDbContext<ApiDbContext>("gdb-db");
builder.AddSqlServerDbContext<ApplicationIdentityDbContext>("gdb-db");

var host = builder.Build();
host.Run();

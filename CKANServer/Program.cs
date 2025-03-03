using CKAN;
using CKANServer.Services;
using log4net;
using log4net.Core;

Logging.Initialize();
foreach (var repository in LogManager.GetAllRepositories())
{
    repository.Threshold = Level.All;
}

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddGrpc();
builder.Services.AddSingleton<ICkanManager, CkanManager>();


var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapGrpcService<CKANServerService>();

app.MapGet("/",
    () =>
        "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

LogManager.GetRepository().Threshold = Level.All;
app.Run();
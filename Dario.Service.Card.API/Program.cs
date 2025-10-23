using Dario.Core.Abstraction.Card.Options;
using Dario.Core.Application.Card;
using EnvLoader;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

EnvLoader.EnvLoader.Load(builder.Environment.ContentRootPath); 
builder.Configuration.AddEnvironmentVariables();
// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var config = builder.Configuration.GetSection("CardServices");
builder.Services.AddDarioCardServices(config);
var serviceIp = config.GetValue<string>("ServiceIP") ?? throw new InvalidOperationException("CardServices:ServiceIP configuration is missing.");
var servicePort = config.GetValue<int?>("ServicePort") ?? throw new InvalidOperationException("CardServices:ServicePort configuration is missing.");
//builder.WebHost.ConfigureKestrel((context, serverOptions) =>
//{
//    serverOptions.Listen(IPAddress.Parse(config.GetSection("ServiceIP").Value)
//                       , Convert.ToInt32(config.GetSection("ServicePort").Value));
//    //serverOptions.Listen(IPAddress.Loopback, 5001, listenOptions =>
//    //{
//    //    listenOptions.UseHttps("testCert.pfx", "testPassword");
//    //});
//});
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource =>
        resource.AddService(serviceName: "card"))

    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        //.AddSqlClientInstrumentation()
        .AddOtlpExporter(otlpOptions =>
        {
            otlpOptions.Endpoint = new Uri("http://192.168.13.11:4317");
            otlpOptions.Protocol = OtlpExportProtocol.Grpc;
        }))

    .WithLogging(logging => logging
     .AddOtlpExporter(otlpOptions =>
     {
         otlpOptions.Endpoint = new Uri("http://192.168.13.11:4317");
         otlpOptions.Protocol = OtlpExportProtocol.Grpc;
     }))
    .WithMetrics(metric => metric
    .AddAspNetCoreInstrumentation()
    //.AddSqlClientInstrumentation()
    .AddRuntimeInstrumentation()
            .AddOtlpExporter(otlpOptions =>
            {
                otlpOptions.Endpoint = new Uri("http://192.168.13.11:4317");
                otlpOptions.Protocol = OtlpExportProtocol.Grpc;
            })
    );

builder.Services.AddWindowsService(options=>
{
   
});
var app = builder.Build();

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
app.UseSwagger();
app.UseSwaggerUI();
//}

app.UseAuthorization();

app.MapControllers();

app.Run();

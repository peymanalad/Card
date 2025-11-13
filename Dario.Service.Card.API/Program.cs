using Dario.Core.Abstraction.Card.Options;
using Dario.Core.Application.Card;
using EnvLoader;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Collections.Generic;
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
builder.Services.AddSingleton(CardServicesTelemetry.Meter);
var serviceIp = config.GetValue<string>("ServiceIP") ?? throw new InvalidOperationException("CardServices:ServiceIP configuration is missing.");
var servicePort = config.GetValue<int?>("ServicePort") ?? throw new InvalidOperationException("CardServices:ServicePort configuration is missing.");


var serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME")?.Trim();
if (string.IsNullOrWhiteSpace(serviceName))
{
    serviceName = builder.Configuration.GetValue<string>("OpenTelemetry:ServiceName")?.Trim();
}

if (string.IsNullOrWhiteSpace(serviceName))
{
    throw new InvalidOperationException("OpenTelemetry:ServiceName configuration is required. Provide it via configuration or the OTEL_SERVICE_NAME environment variable.");
}

var serviceNamespace = builder.Configuration.GetValue<string>("OpenTelemetry:ServiceNamespace")?.Trim();
var serviceVersion = builder.Configuration.GetValue<string>("OpenTelemetry:ServiceVersion")?.Trim();
var deploymentEnvironment = builder.Configuration.GetValue<string>("OpenTelemetry:DeploymentEnvironment")?.Trim();

var otlpEndpointValue = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")?.Trim();
if (string.IsNullOrEmpty(otlpEndpointValue))
{
    otlpEndpointValue = builder.Configuration.GetValue<string>("OpenTelemetry:OtlpEndpoint")?.Trim();
}

if (string.IsNullOrEmpty(otlpEndpointValue))
{
    throw new InvalidOperationException("An OTLP endpoint is required. Provide it via configuration or the OTEL_EXPORTER_OTLP_ENDPOINT environment variable.");
}

if (!Uri.TryCreate(otlpEndpointValue, UriKind.Absolute, out var otlpEndpoint))
{
    throw new InvalidOperationException($"The OTLP endpoint value '{otlpEndpointValue}' is not a valid absolute URI.");
}

var otlpProtocolValue = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL")?.Trim();
if (string.IsNullOrEmpty(otlpProtocolValue))
{
    otlpProtocolValue = builder.Configuration.GetValue<string>("OpenTelemetry:OtlpProtocol")?.Trim();
}

var otlpProtocol = otlpProtocolValue?.ToLowerInvariant() switch
{
    null or "" or "grpc" => OtlpExportProtocol.Grpc,
    "httpprotobuf" => OtlpExportProtocol.HttpProtobuf,
    var value => throw new InvalidOperationException($"Unsupported OpenTelemetry:OtlpProtocol value '{value}'. Use 'grpc' or 'httpProtobuf'.")
};

var otlpHeaders = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_HEADERS");
if (string.IsNullOrWhiteSpace(otlpHeaders))
{
    otlpHeaders = builder.Configuration.GetValue<string>("OpenTelemetry:Headers");
}
void ConfigureOtlpExporter(OtlpExporterOptions options)
{
    options.Endpoint = otlpEndpoint;
    options.Protocol = otlpProtocol;

    if (!string.IsNullOrWhiteSpace(otlpHeaders))
    {
        options.Headers = otlpHeaders;
    }
}

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
    {
        resource.AddService(serviceName: serviceName!, serviceNamespace: serviceNamespace, serviceVersion: serviceVersion);

        if (!string.IsNullOrWhiteSpace(deploymentEnvironment))
        {
            resource.AddAttributes(new[]
            {
                new KeyValuePair<string, object>("deployment.environment", deploymentEnvironment!)
            });
        }
    })
    .WithTracing(tracing => tracing
        .AddSource(CardServicesTelemetry.ActivitySourceName)
        .AddAspNetCoreInstrumentation(o =>
        {
            o.EnrichWithHttpRequest = (activity, req) =>
            {
                var endpoint = req.HttpContext.GetEndpoint();
                var name = endpoint?.Metadata?.GetMetadata<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>()?.ActionName
                           ?? req.RouteValues["action"]?.ToString()
                           ?? req.Path.ToString();
                activity.SetTag("endpoint", name);
            };
            o.RecordException = true;
        })        
        //.AddSqlClientInstrumentation()
        .AddOtlpExporter(ConfigureOtlpExporter))

    .WithLogging(logging => logging
    .AddOtlpExporter(ConfigureOtlpExporter))
    .WithMetrics(metric => metric
        .AddMeter(CardServicesTelemetry.MeterName)
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddProcessInstrumentation()
        .AddView("card.endpoint.request.duration", new ExplicitBucketHistogramConfiguration
        {
            Boundaries = new double[] { 5, 10, 20, 50, 100, 200, 300, 500, 750, 1000, 1500, 2000, 3000, 5000 }
        })
        .AddOtlpExporter(ConfigureOtlpExporter)
    );
builder.Logging.AddOpenTelemetry(o =>
{
    o.IncludeFormattedMessage = true;
    o.ParseStateValues = true;
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

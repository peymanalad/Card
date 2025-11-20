using Dario.Core.Application.Card;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Oracle.ManagedDataAccess.OpenTelemetry;
using System.Collections.Generic;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

var serviceName = builder.Environment.ApplicationName;
var serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
var deploymentEnvironment = builder.Environment.EnvironmentName;

var otelConfig = builder.Configuration.GetSection("OpenTelemetry");
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
                  ?? otelConfig.GetValue<string>("Endpoint")
                  ?? "http://signoz-otel-collector:4317";
var otlpProtocol = builder.Configuration["OTEL_EXPORTER_OTLP_PROTOCOL"]
                 ?? otelConfig.GetValue<string>("Protocol");
var otlpExportProtocol = string.Equals(otlpProtocol, "http/protobuf", StringComparison.OrdinalIgnoreCase)
    ? OtlpExportProtocol.HttpProtobuf
    : OtlpExportProtocol.Grpc;
var otelResourceAttributes = builder.Configuration["OTEL_RESOURCE_ATTRIBUTES"]
                           ?? otelConfig.GetValue<string>("ResourceAttributes");

var configureResource = (Action<ResourceBuilder>)(resourceBuilder =>
{
    resourceBuilder
        .AddEnvironmentVariableDetector()
        .AddService(serviceName: serviceName, serviceVersion: serviceVersion)
        .AddAttributes(new KeyValuePair<string, object>[]
        {
            new("deployment.environment", deploymentEnvironment)
        })
        .AddAttributes(ParseResourceAttributes(otelResourceAttributes));
});

var resourceBuilder = ResourceBuilder.CreateDefault();
configureResource(resourceBuilder);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(configureResource)
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOracleDataProviderInstrumentation()
            .AddSource("Oracle.ManagedDataAccess.Client")
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(otlpEndpoint);
                options.Protocol = otlpExportProtocol;
                options.ExportProcessorType = ExportProcessorType.Batch;
            });
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(otlpEndpoint);
                options.Protocol = otlpExportProtocol;
                options.ExportProcessorType = ExportProcessorType.Batch;
            });
    });

builder.Logging.ClearProviders();
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
    logging.ParseStateValues = true;
    logging.SetResourceBuilder(resourceBuilder);
    logging.AddOtlpExporter(options =>
    {
        options.Endpoint = new Uri(otlpEndpoint);
        options.Protocol = otlpExportProtocol;
        options.ExportProcessorType = ExportProcessorType.Batch;
    });
});
static IEnumerable<KeyValuePair<string, object>> ParseResourceAttributes(string? rawAttributes)
{
    if (string.IsNullOrWhiteSpace(rawAttributes))
    {
        yield break;
    }

    foreach (var attribute in rawAttributes.Split(',', StringSplitOptions.RemoveEmptyEntries))
    {
        var parts = attribute.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 2)
        {
            yield return new KeyValuePair<string, object>(parts[0].Trim(), parts[1].Trim());
        }
    }
}
// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var config = builder.Configuration.GetSection("CardServices");
builder.Services.AddDarioCardServices(config);
builder.WebHost.ConfigureKestrel((context, serverOptions) =>
{
    serverOptions.Listen(IPAddress.Parse(config.GetSection("ServiceIP").Value)
                       , Convert.ToInt32(config.GetSection("ServicePort").Value));
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

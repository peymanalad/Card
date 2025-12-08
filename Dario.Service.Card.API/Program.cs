using Dario.Core.Application.Card;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Oracle.ManagedDataAccess.OpenTelemetry;
using System.Diagnostics.Metrics;
using System.Diagnostics;
using System.Net;

var builder = WebApplication.CreateBuilder(args);
var serviceName = builder.Environment.ApplicationName;
var serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
var deploymentEnvironment = builder.Environment.EnvironmentName;

var meter = new Meter("Dario.Service.Card.API");
var process = Process.GetCurrentProcess();
var processStartTime = Process.GetCurrentProcess().StartTime.ToUniversalTime();
meter.CreateObservableGauge("service_uptime_seconds", () =>
{
    var uptime = DateTime.UtcNow - processStartTime;

    return new Measurement<double>(
        uptime.TotalSeconds,
        new KeyValuePair<string, object?>("service.name", serviceName),
        new KeyValuePair<string, object?>("deployment.environment", deploymentEnvironment)
    );
});
meter.CreateObservableGauge("process_cpu_seconds_total", () =>
{
    process.Refresh();

    return new Measurement<double>(
        process.TotalProcessorTime.TotalSeconds,
        new KeyValuePair<string, object?>("service.name", serviceName),
        new KeyValuePair<string, object?>("deployment.environment", deploymentEnvironment)
    );
});

meter.CreateObservableGauge("process_memory_bytes", () =>
{
    process.Refresh();

    return new Measurement<long>(
        process.WorkingSet64,
        new KeyValuePair<string, object?>("service.name", serviceName),
        new KeyValuePair<string, object?>("deployment.environment", deploymentEnvironment)
    );
});
var otelConfig = builder.Configuration.GetSection("OpenTelemetry");
var otlpProtocol = builder.Configuration["OTEL_EXPORTER_OTLP_PROTOCOL"];

var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
var otlpExportProtocol = string.Equals(otlpProtocol, "http/protobuf", StringComparison.OrdinalIgnoreCase)
    ? OtlpExportProtocol.HttpProtobuf
    : OtlpExportProtocol.Grpc;
var logsOtlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"];
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
            .AddMeter("Dario.Service.Card.API")
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
    logging.AddOtlpExporter(o =>
    {
        o.Endpoint = new Uri(logsOtlpEndpoint);
        o.Protocol = OtlpExportProtocol.Grpc;
        o.ExportProcessorType = ExportProcessorType.Batch;
    });
    logging.AddOtlpExporter(options =>
    {
        options.Endpoint = new Uri(otlpEndpoint);

        options.Protocol = otlpExportProtocol;
        options.ExportProcessorType = ExportProcessorType.Batch;
    });
});
builder.Services.AddScoped<ICardBinStatsService, OracleCardBinStatsService>();

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

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var config = builder.Configuration.GetSection("CardServices");
builder.Services.AddDarioCardServices(config);
builder.WebHost.ConfigureKestrel((context, serverOptions) =>
{
    serverOptions.ListenAnyIP(Convert.ToInt32(config.GetSection("ServicePort").Value));
});
var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseStaticFiles();

app.UseAuthorization();

app.MapControllers();

app.Run();

using Dario.Core.Application.Card;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Oracle.ManagedDataAccess.OpenTelemetry;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
var serviceName = builder.Environment.ApplicationName;
var serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
if (string.IsNullOrWhiteSpace(otlpEndpoint))
{
    throw new InvalidOperationException("'OTEL_EXPORTER_OTLP_ENDPOINT' is not configured.");
}

var otlpProtocolRaw = builder.Configuration["OTEL_EXPORTER_OTLP_PROTOCOL"]
                     ?? builder.Configuration["OpenTelemetry:Protocol"];
var resourceAttributesRaw = builder.Configuration["OTEL_RESOURCE_ATTRIBUTES"]
                         ?? builder.Configuration["OpenTelemetry:ResourceAttributes"];

var otlpExportProtocol = string.Equals(otlpProtocolRaw, "http/protobuf", StringComparison.OrdinalIgnoreCase)
    ? OtlpExportProtocol.HttpProtobuf
    : OtlpExportProtocol.Grpc;

var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddEnvironmentVariableDetector()
    .AddService(serviceName: serviceName, serviceVersion: serviceVersion)
    .AddAttributes(new KeyValuePair<string, object?>[]
    {
        new("deployment.environment", builder.Environment.EnvironmentName)
    })
    .AddAttributes(ParseResourceAttributes(resourceAttributesRaw));
var meter = new Meter("Dario.Service.Card.API");
var process = Process.GetCurrentProcess();
var processStartTime = Process.GetCurrentProcess().StartTime.ToUniversalTime();
meter.CreateObservableGauge("service_uptime_seconds", () =>
{
    var uptime = DateTime.UtcNow - processStartTime;

    return new Measurement<double>(
        uptime.TotalSeconds,
        new KeyValuePair<string, object?>("service.name", serviceName),
        new KeyValuePair<string, object?>("deployment.environment", builder.Environment.EnvironmentName)
        );
});
meter.CreateObservableGauge("process_cpu_seconds_total", () =>
{
    process.Refresh();

    return new Measurement<double>(
        process.TotalProcessorTime.TotalSeconds,
        new KeyValuePair<string, object?>("service.name", serviceName),
        new KeyValuePair<string, object?>("deployment.environment", builder.Environment.EnvironmentName)
        );
});

meter.CreateObservableGauge("process_memory_bytes", () =>
{
    process.Refresh();

    return new Measurement<long>(
        process.WorkingSet64,
        new KeyValuePair<string, object?>("service.name", serviceName),
        new KeyValuePair<string, object?>("deployment.environment", builder.Environment.EnvironmentName)
        );
});


builder.Services.AddOpenTelemetry()
    .ConfigureResource(_ => _.AddResourceBuilder(resourceBuilder))
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
    logging.AddOtlpExporter(options =>
    {
        options.Endpoint = new Uri(otlpEndpoint);

        options.Protocol = otlpExportProtocol;
        options.ExportProcessorType = ExportProcessorType.Batch;
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();
builder.Services.AddCardApplication(builder.Configuration);

var app = builder.Build();
app.UseSerilogRequestLogging();
app.UseSwagger();
app.UseSwaggerUI();

app.UseStaticFiles();

app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");
app.Run();
static IEnumerable<KeyValuePair<string, object?>> ParseResourceAttributes(string? rawAttributes)
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
            yield return new KeyValuePair<string, object?>(parts[0].Trim(), parts[1].Trim());
        }
    }
}
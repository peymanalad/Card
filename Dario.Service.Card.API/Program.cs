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
builder.Services.AddSingleton(CardServicesTelemetry.Meter);
var serviceIp = config.GetValue<string>("ServiceIP") ?? throw new InvalidOperationException("CardServices:ServiceIP configuration is missing.");
var servicePort = config.GetValue<int?>("ServicePort") ?? throw new InvalidOperationException("CardServices:ServicePort configuration is missing.");


var otlpEndpointValue = builder.Configuration.GetValue<string>("OpenTelemetry:OtlpEndpoint")?.Trim();
if (string.IsNullOrEmpty(otlpEndpointValue))
{
    throw new InvalidOperationException("OpenTelemetry:OtlpEndpoint configuration is required. Provide it via configuration or the OpenTelemetry__OtlpEndpoint environment variable.");
}

if (!Uri.TryCreate(otlpEndpointValue, UriKind.Absolute, out var otlpEndpoint))
{
    throw new InvalidOperationException($"OpenTelemetry:OtlpEndpoint value '{otlpEndpointValue}' is not a valid absolute URI.");
}

var otlpProtocolValue = builder.Configuration.GetValue<string>("OpenTelemetry:OtlpProtocol")?.Trim();
var otlpProtocol = otlpProtocolValue?.ToLowerInvariant() switch
{
    null or "" or "grpc" => OtlpExportProtocol.Grpc,
    "httpprotobuf" => OtlpExportProtocol.HttpProtobuf,
    var value => throw new InvalidOperationException($"Unsupported OpenTelemetry:OtlpProtocol value '{value}'. Use 'grpc' or 'httpProtobuf'.")
};

var otlpHeaders = builder.Configuration.GetValue<string>("OpenTelemetry:Headers");

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
        resource.AddService(serviceName: "card"))

    .WithTracing(tracing => tracing
        .AddSource(CardServicesTelemetry.ActivitySourceName)
        .AddAspNetCoreInstrumentation()
        //.AddSqlClientInstrumentation()
        .AddOtlpExporter(ConfigureOtlpExporter))

    .WithLogging(logging => logging
     .AddOtlpExporter(ConfigureOtlpExporter))
    .WithMetrics(metric => metric
        .AddMeter(CardServicesTelemetry.MeterName)
        .AddAspNetCoreInstrumentation()
        //.AddSqlClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter(ConfigureOtlpExporter)
    );

builder.Services.AddWindowsService(options =>
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

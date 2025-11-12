using Dario.Core.Abstraction.Card.Options;
using Dario.Core.Application.Card;
using Dario.Service.Card.API.Telemetry;
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
builder.Services.AddSingleton(HttpRequestMetrics.Meter);
var serviceIp = config.GetValue<string>("ServiceIP") ?? throw new InvalidOperationException("CardServices:ServiceIP configuration is missing.");
var servicePort = config.GetValue<int?>("ServicePort") ?? throw new InvalidOperationException("CardServices:ServicePort configuration is missing.");


//var otlpEndpointValue = builder.Configuration.GetValue<string>("OpenTelemetry:OtlpEndpoint")?.Trim();
const string DefaultOtlpHttpEndpoint = "http://otel-collector:4318";
const string DefaultOtlpGrpcEndpoint = "http://otel-collector:4317"; // For gRPC exporters set Protocol to Grpc and use this endpoint.

var otlpSection = builder.Configuration.GetSection("OpenTelemetry");
var otlpEndpointValue = otlpSection.GetValue<string>("OtlpEndpoint")?.Trim();
if (string.IsNullOrEmpty(otlpEndpointValue))
{
    //throw new InvalidOperationException("OpenTelemetry:OtlpEndpoint configuration is required. Provide it via configuration or the OpenTelemetry__OtlpEndpoint environment variable.");
    otlpEndpointValue = DefaultOtlpHttpEndpoint;
}

if (!Uri.TryCreate(otlpEndpointValue, UriKind.Absolute, out var otlpEndpoint))
{
    throw new InvalidOperationException($"OpenTelemetry:OtlpEndpoint value '{otlpEndpointValue}' is not a valid absolute URI.");
}

//var otlpProtocolValue = builder.Configuration.GetValue<string>("OpenTelemetry:OtlpProtocol")?.Trim();
var otlpProtocolValue = otlpSection.GetValue<string>("OtlpProtocol")?.Trim();
var otlpProtocol = otlpProtocolValue?.ToLowerInvariant() switch
{
    //null or "" or "grpc" => OtlpExportProtocol.Grpc,
    //"httpprotobuf" => OtlpExportProtocol.HttpProtobuf,
    null or "" or "httpprotobuf" => OtlpExportProtocol.HttpProtobuf,
    "grpc" => OtlpExportProtocol.Grpc,
    var value => throw new InvalidOperationException($"Unsupported OpenTelemetry:OtlpProtocol value '{value}'. Use 'grpc' or 'httpProtobuf'.")
};

//var otlpHeaders = builder.Configuration.GetValue<string>("OpenTelemetry:Headers");
var otlpHeaders = otlpSection.GetValue<string>("Headers");

var serviceName = otlpSection.GetValue<string>("ServiceName") ?? builder.Environment.ApplicationName ?? "card-webapi";
var serviceNamespace = otlpSection.GetValue<string>("ServiceNamespace") ?? "Card";
var serviceVersion = otlpSection.GetValue<string>("ServiceVersion") ?? "1.0.0-dev"; // Update during production releases.
var deploymentEnvironment = otlpSection.GetValue<string>("DeploymentEnvironment") ?? builder.Environment.EnvironmentName ?? "Development";
var hostName = Dns.GetHostName();

ResourceBuilder CreateServiceResourceBuilder() => ResourceBuilder
    .CreateDefault()
    .AddService(serviceName: serviceName, serviceNamespace: serviceNamespace, serviceVersion: serviceVersion)
    .AddAttributes(new[]
    {
        new KeyValuePair<string, object>("deployment.environment", deploymentEnvironment),
        new KeyValuePair<string, object>("host.name", hostName),
    });

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
//builder.Services.AddOpenTelemetry()
//    .ConfigureResource(resource =>
//        resource.AddService(serviceName: "card"))

//    .WithTracing(tracing => tracing
//        .AddSource(CardServicesTelemetry.ActivitySourceName)
//        .AddAspNetCoreInstrumentation()
//        //.AddSqlClientInstrumentation()
//        .AddOtlpExporter(ConfigureOtlpExporter))

//    .WithLogging(logging => logging
//     .AddOtlpExporter(ConfigureOtlpExporter))
//    .WithMetrics(metric => metric
//        .AddMeter(CardServicesTelemetry.MeterName)
//        .AddAspNetCoreInstrumentation()
//        //.AddSqlClientInstrumentation()
//        .AddRuntimeInstrumentation()
//        .AddOtlpExporter(ConfigureOtlpExporter)
//    );
var openTelemetryBuilder = builder.Services.AddOpenTelemetry();

openTelemetryBuilder.ConfigureResource(resource => resource.AddResource(CreateServiceResourceBuilder().Build()));

openTelemetryBuilder
    .WithTracing(tracing =>
        tracing
            .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(1.0))) // In production consider 0.05 (5%) to manage volume.
            .AddSource(CardServicesTelemetry.ActivitySourceName)
            .AddAspNetCoreInstrumentation(options =>
            {
                options.RecordException = true;
                options.Enrich = (activity, eventName, rawObject) =>
                {
                    if (eventName == "OnStartActivity" && rawObject is HttpContext context)
                    {
                        var route = HttpRouteMetadata.ResolveRouteTemplate(context);
                        var method = context.Request.Method;
                        activity.DisplayName = $"{method} {route}";
                        activity.SetTag("http.route", route);
                        activity.SetTag("http.method", method);
                    }
                };
            })
            .AddHttpClientInstrumentation()
            .AddSqlClientInstrumentation(options =>
            {
                options.RecordException = true;
                options.SetDbStatementForStoredProcedure = true;
                options.SetDbStatementForText = false; // Keep SQL text off spans (keep false in production).
            })
            .AddEntityFrameworkCoreInstrumentation(options =>
            {
                options.SetDbStatementForStoredProcedure = true;
                options.SetDbStatementForText = false; // Keep SQL text off spans (keep false in production).
            })
            .AddOtlpExporter(ConfigureOtlpExporter))
    .WithMetrics(metrics =>
        metrics
            .AddMeter(CardServicesTelemetry.MeterName)
            .AddMeter(HttpRequestMetrics.MeterName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddProcessInstrumentation()
            .AddOtlpExporter(ConfigureOtlpExporter));

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
    logging.ParseStateValues = true;
    logging.SetResourceBuilder(CreateServiceResourceBuilder());
    logging.AddOtlpExporter(ConfigureOtlpExporter);
});

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
app.UseHttpRequestMetrics();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/healthz", () => Results.Json(new { status = "ok" }))
    .WithName("Healthz")
    .WithTags("Health")
    .AllowAnonymous();

app.Run();

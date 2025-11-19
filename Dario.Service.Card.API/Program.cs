using Dario.Core.Abstraction.Card.Options;
using Dario.Core.Application.Card;
using Dario.Observability;
using EnvLoader;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Collections.Generic;
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

builder.AddDarioOpenTelemetry(new DarioOpenTelemetryOptions
{
    ActivitySourceName = CardServicesTelemetry.ActivitySourceName,
    Meter = CardServicesTelemetry.Meter,
    MeterName = CardServicesTelemetry.MeterName,
    ConfigureAspNetCoreInstrumentation = options =>
    {
        options.EnrichWithHttpRequest = (activity, req) =>
        {
            var endpoint = req.HttpContext.GetEndpoint();
            var name = endpoint?.Metadata?.GetMetadata<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>()?.ActionName
                       ?? req.RouteValues["action"]?.ToString()
                       ?? req.Path.ToString();
            activity.SetTag("endpoint", name);
        };
    },
    ConfigureMetrics = metric =>
    {
        metric.AddView("card.endpoint.request.duration", new ExplicitBucketHistogramConfiguration
        {
            Boundaries = new double[] { 5, 10, 20, 50, 100, 200, 300, 500, 750, 1000, 1500, 2000, 3000, 5000 }
        });
    }
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

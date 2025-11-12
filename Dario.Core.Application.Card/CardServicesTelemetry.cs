using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Dario.Core.Application.Card;

public static class CardServicesTelemetry
{
    public const string ActivitySourceName = "CardServices.Telemetry.ActivitySource";
    public const string MeterName = "CardServices.Telemetry.Meter";

    public static ActivitySource ActivitySource { get; } = new(ActivitySourceName);
    public static Meter Meter { get; } = new(MeterName);

    public static Counter<long> RequestCounter { get; } = Meter.CreateCounter<long>("card.endpoint.request.count");
    public static Counter<long> ErrorCounter { get; } = Meter.CreateCounter<long>("card.endpoint.request.error.count");
    public static Histogram<double> RequestDuration { get; } = Meter.CreateHistogram<double>("card.endpoint.request.duration", unit: "ms");

    public static TagList CreateOperationTags(string endpoint)
    {
        var tags = new TagList
        {
            { "endpoint", endpoint }
        };

        return tags;
    }
}
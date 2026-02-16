using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ImageProcessor.ApiService.Telemetry;

public static class ApiTelemetry
{
    public const string ServiceName = "ImageProcessor.ApiService";
    
    public static readonly ActivitySource ActivitySource = new(ServiceName);
    public static readonly Meter Meter = new(ServiceName);
    
    // Jobs
    public static readonly Counter<long> JobsCreated =
        Meter.CreateCounter<long>("jobs.created", "jobs", "Total number of jobs created");
    public static readonly Counter<long> JobsCleaned =
        Meter.CreateCounter<long>("jobs.cleaned", "jobs", "Total number of jobs removed by cleanup");
    
    // Cache
    public static readonly Counter<long> CacheMisses =
        Meter.CreateCounter<long>("cache.misses", "misses", "Number of cache misses");
}
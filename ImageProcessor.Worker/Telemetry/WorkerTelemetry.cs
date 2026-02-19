using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ImageProcessor.Worker.Telemetry;

public static class WorkerTelemetry
{
    public const string ServiceName = "ImageProcessor.Worker";
    
    public static readonly ActivitySource ActivitySource = new(ServiceName);
    public static readonly Meter Meter = new(ServiceName);
    
    // Jobs
    public static readonly Counter<long> JobsProcessed =
        Meter.CreateCounter<long>("jobs.processed", "jobs", "Total number of jobs successfully processed");
    public static readonly Counter<long> JobsFailed =
        Meter.CreateCounter<long>("jobs.failed", "jobs", "Total number of jobs failed processing");

    public static readonly Counter<long> AiJobsProcessed =
        Meter.CreateCounter<long>("ai_jobs.processed", "jobs", "Total number of AI jobs successfully processed");
    public static readonly Counter<long> AiJobsFailed =
        Meter.CreateCounter<long>("ai_jobs.failed", "jobs", "Total number of AI jobs failed processing");
    
    // Duration
    public static readonly Histogram<double> ProcessingDuration =
        Meter.CreateHistogram<double>("jobs.processing_duration", "ms", "Time taken to fully process a job");
    public static readonly Histogram<double> AiProcessingDuration =
        Meter.CreateHistogram<double>("ai_jobs.processing_duration", "ms", "Time taken to complete AI enrichment");
}

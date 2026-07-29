using System.Diagnostics;

namespace SperoFlow.Api;

/// <summary>
/// Adds correlation IDs and structured request timing for CloudWatch-friendly logs.
/// Skips high-frequency health probes to reduce noise.
/// </summary>
public sealed partial class RequestObservabilityMiddleware(RequestDelegate next, ILogger<RequestObservabilityMiddleware> logger)
{
    private static readonly PathString LivePath = new("/health/live");
    private static readonly PathString ReadyPath = new("/health/ready");
    private static readonly PathString MetricsPath = new("/metrics");

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        var isProbe = path.StartsWithSegments(LivePath)
            || path.StartsWithSegments(ReadyPath)
            || path.StartsWithSegments(MetricsPath);

        var traceHeader = context.Request.Headers["X-Request-Id"].FirstOrDefault()
            ?? context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
        var requestId = string.IsNullOrWhiteSpace(traceHeader)
            ? Guid.NewGuid().ToString("N")
            : traceHeader.Trim();

        context.TraceIdentifier = requestId;
        context.Response.Headers["X-Request-Id"] = requestId;

        if (isProbe)
        {
            await next(context);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            RequestMetrics.Record(context.Request.Method, context.Response.StatusCode, elapsedMs);
            LogCompleted(
                logger,
                context.Request.Method,
                context.Request.Path.Value,
                context.Response.StatusCode,
                elapsedMs,
                requestId);
        }
    }

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "HTTP {Method} {Path} => {StatusCode} in {ElapsedMs:0.0}ms request_id={RequestId}")]
    private static partial void LogCompleted(
        ILogger logger,
        string method,
        string? path,
        int statusCode,
        double elapsedMs,
        string requestId);
}

public static class RequestObservabilityMiddlewareExtensions
{
    public static IApplicationBuilder UseSperoFlowRequestObservability(this IApplicationBuilder app)
        => app.UseMiddleware<RequestObservabilityMiddleware>();
}

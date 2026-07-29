using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace SperoFlow.Api;

/// <summary>
/// Process-local Prometheus text metrics for pilot scraping on the private network.
/// Zero extra package dependencies; not a full OpenTelemetry stack.
/// </summary>
public static class RequestMetrics
{
    private static long _requestsTotal;
    private static long _errorsTotal;
    private static long _durationMsSum;
    private static long _durationMsCount;
    private static readonly long ProcessStartUnixSeconds =
        DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private static readonly ConcurrentDictionary<string, long> StatusCounts = new(StringComparer.Ordinal);

    public static void Record(string method, int statusCode, double elapsedMs)
    {
        Interlocked.Increment(ref _requestsTotal);
        if (statusCode >= 500)
        {
            Interlocked.Increment(ref _errorsTotal);
        }

        Interlocked.Add(ref _durationMsSum, (long)Math.Max(0, Math.Round(elapsedMs)));
        Interlocked.Increment(ref _durationMsCount);

        var key = string.Create(CultureInfo.InvariantCulture, $"{method.ToUpperInvariant()}:{statusCode}");
        StatusCounts.AddOrUpdate(key, 1, static (_, current) => current + 1);
    }

    public static string RenderPrometheus(string serviceName)
    {
        var sb = new StringBuilder(2048);
        sb.AppendLine("# HELP speroflow_http_requests_total Total HTTP requests handled by the process.");
        sb.AppendLine("# TYPE speroflow_http_requests_total counter");
        sb.Append("speroflow_http_requests_total{service=\"").Append(serviceName).Append("\"} ")
            .Append(Interlocked.Read(ref _requestsTotal).ToString(CultureInfo.InvariantCulture))
            .AppendLine();

        sb.AppendLine("# HELP speroflow_http_errors_total HTTP responses with status >= 500.");
        sb.AppendLine("# TYPE speroflow_http_errors_total counter");
        sb.Append("speroflow_http_errors_total{service=\"").Append(serviceName).Append("\"} ")
            .Append(Interlocked.Read(ref _errorsTotal).ToString(CultureInfo.InvariantCulture))
            .AppendLine();

        var count = Interlocked.Read(ref _durationMsCount);
        var sum = Interlocked.Read(ref _durationMsSum);
        sb.AppendLine("# HELP speroflow_http_request_duration_ms_sum Sum of request durations in milliseconds.");
        sb.AppendLine("# TYPE speroflow_http_request_duration_ms_sum counter");
        sb.Append("speroflow_http_request_duration_ms_sum{service=\"").Append(serviceName).Append("\"} ")
            .Append(sum.ToString(CultureInfo.InvariantCulture))
            .AppendLine();

        sb.AppendLine("# HELP speroflow_http_request_duration_ms_count Count of timed requests.");
        sb.AppendLine("# TYPE speroflow_http_request_duration_ms_count counter");
        sb.Append("speroflow_http_request_duration_ms_count{service=\"").Append(serviceName).Append("\"} ")
            .Append(count.ToString(CultureInfo.InvariantCulture))
            .AppendLine();

        sb.AppendLine("# HELP speroflow_http_responses_total HTTP responses by method and status.");
        sb.AppendLine("# TYPE speroflow_http_responses_total counter");
        foreach (var pair in StatusCounts.OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            var parts = pair.Key.Split(':', 2);
            var method = parts[0];
            var status = parts.Length > 1 ? parts[1] : "0";
            sb.Append("speroflow_http_responses_total{service=\"").Append(serviceName)
                .Append("\",method=\"").Append(method)
                .Append("\",status=\"").Append(status)
                .Append("\"} ")
                .Append(pair.Value.ToString(CultureInfo.InvariantCulture))
                .AppendLine();
        }

        sb.AppendLine("# HELP speroflow_process_start_time_seconds Unix time when metrics module loaded.");
        sb.AppendLine("# TYPE speroflow_process_start_time_seconds gauge");
        sb.Append("speroflow_process_start_time_seconds{service=\"").Append(serviceName).Append("\"} ")
            .Append(ProcessStartUnixSeconds.ToString(CultureInfo.InvariantCulture))
            .AppendLine();

        return sb.ToString();
    }
}

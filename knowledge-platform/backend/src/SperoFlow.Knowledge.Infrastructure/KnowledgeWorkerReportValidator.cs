using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SperoFlow.Knowledge.Domain;

namespace SperoFlow.Knowledge.Infrastructure;

public sealed record KnowledgeWorkerIngestionReport(
    string ReleaseKey,
    string SourceHash,
    int ContentUnits,
    int Entities,
    int Facts,
    int Vectors,
    int WarningCount);

public static class KnowledgeWorkerReportValidator
{
    public static KnowledgeWorkerIngestionReport ParseSuccessfulReport(
        string reportJson,
        KnowledgeIngestionState state,
        string expectedReleaseKey,
        string expectedSourceSha256)
    {
        if (state is not (KnowledgeIngestionState.Succeeded or KnowledgeIngestionState.SucceededWithWarnings))
        {
            throw new KnowledgeValidationException("Only successful worker states may provide a completion report.");
        }

        using var document = ParseObject(reportJson);
        var root = document.RootElement;
        var expectedStatus = state == KnowledgeIngestionState.Succeeded ? "succeeded" : "succeeded_with_warnings";
        var status = ReadRequiredString(root, "status");
        if (!string.Equals(status, expectedStatus, StringComparison.Ordinal))
        {
            throw new KnowledgeValidationException("The worker report status does not match the completion state.");
        }

        var releaseKey = ReadRequiredString(root, "release_key");
        if (!string.Equals(releaseKey, expectedReleaseKey, StringComparison.Ordinal))
        {
            throw new KnowledgeValidationException("The worker report release key does not match the assigned graph release.");
        }

        var sourceHash = ReadRequiredString(root, "source_hash").ToLowerInvariant();
        if (sourceHash.Length != 64 || !sourceHash.All(Uri.IsHexDigit) ||
            !string.Equals(sourceHash, expectedSourceSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new KnowledgeValidationException("The worker report source checksum does not match the approved source.");
        }

        var warningCount = ReadArray(root, "warnings").GetArrayLength();
        if (state == KnowledgeIngestionState.Succeeded && warningCount != 0)
        {
            throw new KnowledgeValidationException("A succeeded worker report cannot contain warnings.");
        }

        if (state == KnowledgeIngestionState.SucceededWithWarnings && warningCount == 0)
        {
            throw new KnowledgeValidationException("A succeeded-with-warnings report must contain at least one warning.");
        }
        return new KnowledgeWorkerIngestionReport(
            releaseKey,
            sourceHash,
            ReadNonNegativeInt(root, "content_units"),
            ReadNonNegativeInt(root, "entities"),
            ReadNonNegativeInt(root, "facts"),
            ReadNonNegativeInt(root, "vectors"),
            warningCount);
    }

    public static void EnsureCallbackCounters(
        int contentUnits,
        int entities,
        int facts,
        int vectors,
        KnowledgeWorkerIngestionReport report)
    {
        if (contentUnits != report.ContentUnits ||
            entities != report.Entities ||
            facts != report.Facts ||
            vectors != report.Vectors)
        {
            throw new KnowledgeValidationException("The worker callback counters do not match its provenance report.");
        }
    }

    public static void ValidateWaitingForOcrReport(string reportJson, string textractJobId)
    {
        using var document = ParseObject(reportJson);
        var root = document.RootElement;
        if (!string.Equals(ReadRequiredString(root, "status"), "waiting_for_ocr", StringComparison.Ordinal) ||
            !string.Equals(ReadRequiredString(root, "textract_job_id"), textractJobId, StringComparison.Ordinal))
        {
            throw new KnowledgeValidationException("The OCR waiting report does not match the callback payload.");
        }

        _ = ReadArray(root, "warnings");
    }

    public static string BuildReleaseValidationReport(
        KnowledgeGraphRelease release,
        IReadOnlyCollection<KnowledgeIngestionJob> jobs,
        IReadOnlyDictionary<Guid, KnowledgeSourceFile> sources)
    {
        if (jobs.Count == 0)
        {
            throw new KnowledgeValidationException("A graph release cannot validate without source ingestion jobs.");
        }

        var entries = jobs
            .OrderBy(job => job.SourceFileId)
            .Select(job =>
            {
                if (job.ReleaseId != release.Id ||
                    job.DatasetId != release.DatasetId ||
                    !sources.TryGetValue(job.SourceFileId, out var source) ||
                    source.DatasetId != release.DatasetId ||
                    !string.Equals(source.OwnerSubject, release.OwnerSubject, StringComparison.Ordinal))
                {
                    throw new KnowledgeValidationException("The graph release job set does not match its source provenance.");
                }

                var expectedHash = source.UploadedSha256 ?? source.ExpectedSha256;
                return new ReleaseEntry(
                    job.SourceFileId,
                    ParseSuccessfulReport(job.Report, job.State, release.ReleaseKey, expectedHash));
            })
            .ToArray();
        var sourceManifest = string.Join(
            "\n",
            entries.Select(entry =>
                entry.SourceId.ToString("D", System.Globalization.CultureInfo.InvariantCulture) +
                ":" +
                entry.Report.SourceHash));
        var sourceManifestSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceManifest))).ToLowerInvariant();

        return JsonSerializer.Serialize(new
        {
            schema_version = 1,
            status = "validated",
            release_key = release.ReleaseKey,
            source_count = entries.Length,
            source_manifest_sha256 = sourceManifestSha256,
            content_units = entries.Sum(entry => (long)entry.Report.ContentUnits),
            entities = entries.Sum(entry => (long)entry.Report.Entities),
            facts = entries.Sum(entry => (long)entry.Report.Facts),
            vectors = entries.Sum(entry => (long)entry.Report.Vectors),
            warning_source_count = entries.Count(entry => entry.Report.WarningCount > 0),
        });
    }

    private static JsonDocument ParseObject(string reportJson)
    {
        try
        {
            var document = JsonDocument.Parse(reportJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                throw new KnowledgeValidationException("Worker reports must be valid JSON objects.");
            }

            return document;
        }
        catch (JsonException)
        {
            throw new KnowledgeValidationException("Worker reports must be valid JSON objects.");
        }
    }

    private static string ReadRequiredString(JsonElement root, string name)
    {
        var value = ReadRequiredProperty(root, name);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new KnowledgeValidationException("The worker report field '" + name + "' must be a non-empty string.");
        }

        return value.GetString()!;
    }
    private static int ReadNonNegativeInt(JsonElement root, string name)
    {
        var value = ReadRequiredProperty(root, name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result) || result < 0)
        {
            throw new KnowledgeValidationException("The worker report field '" + name + "' must be a non-negative integer.");
        }

        return result;
    }

    private static JsonElement ReadArray(JsonElement root, string name)
    {
        var value = ReadRequiredProperty(root, name);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new KnowledgeValidationException("The worker report field '" + name + "' must be an array.");
        }

        return value;
    }

    private static JsonElement ReadRequiredProperty(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            throw new KnowledgeValidationException("The worker report is missing the required '" + name + "' field.");
        }

        return value;
    }

    private sealed record ReleaseEntry(Guid SourceId, KnowledgeWorkerIngestionReport Report);
}
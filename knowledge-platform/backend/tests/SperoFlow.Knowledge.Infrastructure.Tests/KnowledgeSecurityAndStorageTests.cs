using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using SperoFlow.Knowledge.Contracts;
using SperoFlow.Knowledge.Domain;
using SperoFlow.Knowledge.Infrastructure;

namespace SperoFlow.Knowledge.Infrastructure.Tests;

public sealed class KnowledgeSecurityAndStorageTests
{
    [Fact]
    public void Issue_creates_a_bounded_grant_with_identity_and_dataset_constraints()
    {
        var keyPath = CreatePrivateKeyFile();
        try
        {
            var options = Options.Create(new KnowledgeGrantOptions
            {
                Issuer = "knowledge-platform",
                Audience = "speroflow-ai",
                KeyId = "test-key",
                PrivateKeyPath = keyPath,
                PublicKeyPath = keyPath,
                LifetimeSeconds = 90,
            });
            var service = new KnowledgeAccessGrantService(options);
            var datasetId = Guid.CreateVersion7();
            var beforeIssue = DateTimeOffset.UtcNow;

            var issued = service.Issue(
                "owner-subject",
                [new KnowledgeGrantDataset(datasetId, "release-1", "owner-subject", KnowledgeVisibility.Private)]);

            var token = new JwtSecurityTokenHandler().ReadJwtToken(issued.Token);
            Assert.Equal("knowledge-platform", token.Issuer);
            Assert.Equal("speroflow-ai", token.Audiences.Single());
            Assert.Equal("owner-subject", token.Subject);
            Assert.True(Guid.TryParse(token.Id, out _));
            Assert.True(long.TryParse(token.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Iat).Value, out _));
            Assert.Equal("knowledge.query", token.Claims.Single(claim => claim.Type == "scope").Value);
            Assert.Contains(datasetId.ToString("D"), token.Claims.Single(claim => claim.Type == "dataset_grant").Value, StringComparison.Ordinal);
            Assert.InRange(issued.ExpiresAt - beforeIssue, TimeSpan.FromSeconds(80), TimeSpan.FromSeconds(95));
        }
        finally
        {
            File.Delete(keyPath);
        }
    }

    [Fact]
    public void Main_service_validation_does_not_load_the_worker_signing_key()
    {
        var directory = Path.Combine(Path.GetTempPath(), "speroflow-knowledge-token-test-" + Guid.CreateVersion7().ToString("N"));
        Directory.CreateDirectory(directory);
        using var mainSigningKey = RSA.Create(2048);
        using var workerValidationKey = RSA.Create(2048);
        try
        {
            var mainPublicPath = Path.Combine(directory, "main-public.pem");
            var workerPublicPath = Path.Combine(directory, "worker-public.pem");
            File.WriteAllText(mainPublicPath, mainSigningKey.ExportSubjectPublicKeyInfoPem());
            File.WriteAllText(workerPublicPath, workerValidationKey.ExportSubjectPublicKeyInfoPem());

            var service = new KnowledgeInternalTokenService(Options.Create(new KnowledgeInternalAuthOptions
            {
                MainIssuer = "speroflow-api",
                MainAudience = "speroflow-knowledge",
                MainPublicKeyPath = mainPublicPath,
                WorkerIssuer = "speroflow-knowledge-api",
                WorkerAudience = "speroflow-knowledge-api",
                WorkerPrivateKeyPath = Path.Combine(directory, "missing-worker-private.pem"),
                WorkerPublicKeyPath = workerPublicPath,
            }));
            var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
                issuer: "speroflow-api",
                audience: "speroflow-knowledge",
                claims: [new System.Security.Claims.Claim("scope", "knowledge.catalog")],
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.AddMinutes(1),
                signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(
                    new Microsoft.IdentityModel.Tokens.RsaSecurityKey(mainSigningKey),
                    Microsoft.IdentityModel.Tokens.SecurityAlgorithms.RsaSha256)));

            Assert.NotNull(service.ValidateMainServiceToken(token, "knowledge.catalog"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
    [Fact]
    public async Task Presigned_upload_uses_the_public_proxy_host_without_rewriting_the_signature()
    {
        var options = new KnowledgeStorageOptions
        {
            BucketName = "speroflow-knowledge",
            Region = "us-east-1",
            Endpoint = "http://knowledge-object-storage:9000",
            PublicEndpoint = "https://knowledge.example.com",
            AccessKey = "test-access-key",
            SecretKey = "test-secret-key",
            UseSsl = false,
            PublicUseSsl = true,
        };

        using var internalClient = S3KnowledgeObjectStorage.CreateClient(options);
        using var storage = new S3KnowledgeObjectStorage(internalClient, Options.Create(options));

        var upload = await storage.CreatePresignedUploadAsync(
            "sources/dataset/source.md",
            "text/markdown",
            TimeSpan.FromMinutes(5),
            CancellationToken.None);

        var uri = new Uri(upload.UploadUrl);
        Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
        Assert.Equal("knowledge.example.com", uri.Host);
        Assert.Equal("/speroflow-knowledge/sources/dataset/source.md", uri.AbsolutePath);
        Assert.Contains("X-Amz-Algorithm", uri.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("text/markdown", upload.RequiredHeaders["Content-Type"]);
    }

    [Fact]
    public void Ocr_retry_preserves_the_external_job_identifier()
    {
        var job = new KnowledgeIngestionJob(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "owner-subject");

        job.MarkProcessing();
        job.MarkWaitingForOcr("textract-123", "{}");
        job.Retry();

        Assert.Equal(KnowledgeIngestionState.Queued, job.State);
        Assert.Equal("textract-123", job.TextractJobId);
    }

    [Fact]
    public void Release_snapshot_schema_allows_only_one_draft_and_one_source_job_per_release()
    {
        var options = new DbContextOptionsBuilder<KnowledgeDbContext>()
            .UseNpgsql("Host=localhost;Database=speroflow_knowledge;Username=test;Password=test")
            .Options;
        using var db = new KnowledgeDbContext(options);

        var release = db.Model.FindEntityType(typeof(KnowledgeGraphRelease))!;
        var draftIndex = release.GetIndexes().Single(index => index.Properties.Select(property => property.Name).SequenceEqual([nameof(KnowledgeGraphRelease.DatasetId)]));
        Assert.True(draftIndex.IsUnique);
        Assert.Equal("\"State\" = 'Draft'", draftIndex.GetFilter());

        var job = db.Model.FindEntityType(typeof(KnowledgeIngestionJob))!;
        var snapshotJobIndex = job.GetIndexes().Single(index => index.Properties.Select(property => property.Name).SequenceEqual([nameof(KnowledgeIngestionJob.ReleaseId), nameof(KnowledgeIngestionJob.SourceFileId)]));
        Assert.True(snapshotJobIndex.IsUnique);
    }
    [Fact]
    public void Source_signatures_reject_disguised_binary_content()
    {
        KnowledgeSourceSignatureValidator.Validate("guide.md", "text/markdown", "# A valid guide"u8);
        KnowledgeSourceSignatureValidator.Validate("dataset.json", "application/json", "{\"items\":[]}"u8);
        KnowledgeSourceSignatureValidator.Validate("document.pdf", "application/pdf", "%PDF-1.7"u8);
        KnowledgeSourceSignatureValidator.Validate("document.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "PK\x03\x04"u8);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            KnowledgeSourceSignatureValidator.Validate("document.pdf", "application/pdf", "not a PDF"u8));

        Assert.Contains("signature", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void Worker_job_token_is_bound_to_a_single_dispatch_attempt()
    {
        var keyPath = CreatePrivateKeyFile();
        try
        {
            var service = new KnowledgeInternalTokenService(Options.Create(new KnowledgeInternalAuthOptions
            {
                MainPublicKeyPath = keyPath,
                WorkerPrivateKeyPath = keyPath,
                WorkerPublicKeyPath = keyPath,
                WorkerDeliveryTokenLifetimeMinutes = 5,
            }));
            var jobId = Guid.CreateVersion7();

            var token = service.CreateWorkerDeliveryToken(jobId, 2);
            var principal = service.ValidateWorkerDeliveryToken(token, jobId);

            Assert.NotNull(principal);
            Assert.True(KnowledgeInternalTokenService.MatchesWorkerAttempt(principal!, 2));
            Assert.False(KnowledgeInternalTokenService.MatchesWorkerAttempt(principal!, 1));
        }
        finally
        {
            File.Delete(keyPath);
        }
    }
    [Fact]
    public void Successful_worker_reports_must_match_release_source_and_callback_counters()
    {
        var hash = new string('a', 64);
        var report = SuccessfulReport("release-1", hash, 3, 2, 1, 3);

        var parsed = KnowledgeWorkerReportValidator.ParseSuccessfulReport(
            report,
            KnowledgeIngestionState.Succeeded,
            "release-1",
            hash);
        KnowledgeWorkerReportValidator.EnsureCallbackCounters(3, 2, 1, 3, parsed);

        Assert.Throws<KnowledgeValidationException>(() =>
            KnowledgeWorkerReportValidator.ParseSuccessfulReport(
                SuccessfulReport("other-release", hash, 3, 2, 1, 3),
                KnowledgeIngestionState.Succeeded,
                "release-1",
                hash));
        Assert.Throws<KnowledgeValidationException>(() =>
            KnowledgeWorkerReportValidator.EnsureCallbackCounters(4, 2, 1, 3, parsed));
    }
    [Fact]
    public void Release_validation_report_is_deterministic_and_retry_reopens_only_failed_releases()
    {
        var datasetId = Guid.CreateVersion7();
        var release = new KnowledgeGraphRelease(datasetId, "owner-subject", "release-1");
        var firstHash = new string('a', 64);
        var secondHash = new string('b', 64);
        var firstSource = new KnowledgeSourceFile(datasetId, "owner-subject", "first.md", "sources/first.md", "text/markdown", 1, firstHash);
        var secondSource = new KnowledgeSourceFile(datasetId, "owner-subject", "second.md", "sources/second.md", "text/markdown", 1, secondHash);
        var firstJob = new KnowledgeIngestionJob(datasetId, firstSource.Id, release.Id, "owner-subject");
        var secondJob = new KnowledgeIngestionJob(datasetId, secondSource.Id, release.Id, "owner-subject");
        firstJob.MarkProcessing();
        firstJob.MarkSucceeded(SuccessfulReport("release-1", firstHash, 3, 2, 1, 3), false);
        secondJob.MarkProcessing();
        secondJob.MarkSucceeded(SuccessfulReport("release-1", secondHash, 5, 4, 2, 5), false);

        var sources = new Dictionary<Guid, KnowledgeSourceFile>
        {
            [firstSource.Id] = firstSource,
            [secondSource.Id] = secondSource,
        };
        var report = KnowledgeWorkerReportValidator.BuildReleaseValidationReport(release, [firstJob, secondJob], sources);

        using var document = JsonDocument.Parse(report);
        Assert.Equal("validated", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("source_count").GetInt32());
        Assert.Equal(8, document.RootElement.GetProperty("content_units").GetInt64());
        Assert.Equal(report, KnowledgeWorkerReportValidator.BuildReleaseValidationReport(release, [secondJob, firstJob], sources));

        var failedRelease = new KnowledgeGraphRelease(datasetId, "owner-subject", "retry-release");
        failedRelease.MarkFailed("{\"status\":\"failed\"}");
        failedRelease.ReopenForRetry();
        Assert.Equal(KnowledgeReleaseState.Draft, failedRelease.State);
        Assert.Throws<KnowledgeValidationException>(() => failedRelease.ReopenForRetry());
    }
    private static string SuccessfulReport(string releaseKey, string sourceHash, int contentUnits, int entities, int facts, int vectors) =>
        JsonSerializer.Serialize(new
        {
            status = "succeeded",
            release_key = releaseKey,
            source_hash = sourceHash,
            content_units = contentUnits,
            entities,
            facts,
            vectors,
            warnings = Array.Empty<string>(),
        });
    private static string CreatePrivateKeyFile()
    {
        using var rsa = RSA.Create(2048);
        var path = Path.Combine(Path.GetTempPath(), "speroflow-knowledge-test-" + Guid.CreateVersion7().ToString("N") + ".pem");
        File.WriteAllText(path, rsa.ExportPkcs8PrivateKeyPem());
        return path;
    }
}
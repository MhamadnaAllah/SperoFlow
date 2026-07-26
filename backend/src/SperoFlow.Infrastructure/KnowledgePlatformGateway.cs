using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SperoFlow.Application;

namespace SperoFlow.Infrastructure;

public sealed class KnowledgePlatformGateway : IKnowledgePlatformGateway
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly IServiceTokenFactory _tokenFactory;
    private readonly KnowledgePlatformOptions _options;

    public KnowledgePlatformGateway(
        HttpClient httpClient,
        IServiceTokenFactory tokenFactory,
        IOptions<KnowledgePlatformOptions> options)
    {
        _httpClient = httpClient;
        _tokenFactory = tokenFactory;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<KnowledgeCatalogItem>> ListCatalogAsync(Guid userId, CancellationToken cancellationToken)
    {
        var subject = userId.ToString("D", System.Globalization.CultureInfo.InvariantCulture);
        using var request = CreateRequest(HttpMethod.Get, "/internal/v1/knowledge/catalog/" + Uri.EscapeDataString(subject), userId, "knowledge.catalog");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<List<KnowledgeCatalogItem>>(stream, SerializerOptions, cancellationToken)
            ?? [];
    }

    public async Task<KnowledgeAccessGrant> IssueAccessGrantAsync(Guid userId, IReadOnlyCollection<Guid> datasetIds, CancellationToken cancellationToken)
    {
        var selected = datasetIds.Where(id => id != Guid.Empty).Distinct().OrderBy(id => id).ToArray();
        if (selected.Length is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(datasetIds), "Select between one and twenty knowledge datasets.");
        }

        var subject = userId.ToString("D", System.Globalization.CultureInfo.InvariantCulture);
        using var request = CreateRequest(HttpMethod.Post, "/internal/v1/knowledge/access-grants", userId, "knowledge.grants");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { subject, datasetIds = selected }, SerializerOptions),
            Encoding.UTF8,
            "application/json");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<KnowledgeAccessGrant>(stream, SerializerOptions, cancellationToken)
            ?? throw new HttpRequestException("Knowledge platform returned an empty access-grant response.");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, Guid userId, string scope)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _tokenFactory.CreateToken(_options.Audience, scope, userId, TimeSpan.FromMinutes(2)));
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException("Knowledge platform returned " + (int)response.StatusCode + ": " + body[..Math.Min(body.Length, 512)], null, response.StatusCode);
    }
}
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SperoFlow.Application;
using SperoFlow.Contracts;

namespace SperoFlow.Infrastructure;

public sealed class AiGateway : IAiGateway
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly IServiceTokenFactory _tokenFactory;
    private readonly ServiceJwtOptions _tokenOptions;

    public AiGateway(
        HttpClient httpClient,
        IServiceTokenFactory tokenFactory,
        IOptions<ServiceJwtOptions> tokenOptions)
    {
        _httpClient = httpClient;
        _tokenFactory = tokenFactory;
        _tokenOptions = tokenOptions.Value;
    }

    public async Task<GraphQueryResponse> QueryGraphAsync(GraphQueryRequest request, Guid userId, string? knowledgeAccessGrant, CancellationToken cancellationToken)
    {
        using var payload = await InvokeAsync(
            "/api/query",
            new
            {
                question = request.Question,
                strategy = request.Strategy,
                top_k = request.TopK,
                scope = request.Scope,
                dataset_ids = request.DatasetIds?.Select(id => id.ToString("D", System.Globalization.CultureInfo.InvariantCulture)).ToArray() ?? Array.Empty<string>(),
                knowledge_access_grant = knowledgeAccessGrant,
            },
            userId,
            "ai.invoke",
            cancellationToken);
        return new GraphQueryResponse(payload.RootElement.Clone());
    }

    public async Task<JsonDocument> InvokeAsync(
        string path,
        object payload,
        Guid userId,
        string scope,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _tokenFactory.CreateToken(_tokenOptions.AiAudience, scope, userId, TimeSpan.FromMinutes(2)));

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException("AI service returned " + (int)response.StatusCode + ": " + body[..Math.Min(body.Length, 512)]);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }
}

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Tiro.Cli;

public interface IEmbeddingClient
{
    Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken);
}

public sealed record EmbeddingResult(float[] Vector, int Dimensions, string Provider, string Model);

public sealed class EmbeddingClientException : Exception
{
    public EmbeddingClientException(string message, string? responseBody, Exception? innerException)
        : base(message, innerException)
    {
        ResponseBody = responseBody;
    }

    public string? ResponseBody { get; }
}

/// <summary>
/// Calls OpenAI's embeddings endpoint. Mirrors GeminiPlannerClient's HTTP
/// patterns deliberately (same HttpClient injection, same exception
/// wrapping, same redacted-diagnostics discipline) since that class is the
/// only existing precedent for an outbound provider call in this codebase.
/// Never invoked unless EmbeddingConfig.Enabled is true and a key is
/// present — callers are responsible for that gate; this class assumes it
/// has already been authorized to run.
/// </summary>
public sealed class OpenAiEmbeddingClient : IEmbeddingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly string _model;
    private readonly string _endpoint;

    public OpenAiEmbeddingClient(HttpClient httpClient, string? apiKey, string model, string baseUrl)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _model = model;
        _endpoint = baseUrl.TrimEnd('/') + "/embeddings";
    }

    public async Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text to embed must not be empty.", nameof(text));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        }
        request.Content = JsonContent.Create(new OpenAiEmbeddingRequest(_model, text), options: JsonOptions);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new EmbeddingClientException(
                "Embedding HTTP request failed before a response was received.",
                null,
                ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new EmbeddingClientException(
                    $"Embedding HTTP response was not successful (status {(int)response.StatusCode}).",
                    LimitDiagnosticBody(body),
                    null);
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<OpenAiEmbeddingResponse>(body, JsonOptions)
                    ?? throw new InvalidOperationException("Embedding response was empty.");
                var vector = parsed.Data?.FirstOrDefault()?.Embedding
                    ?? throw new InvalidOperationException("Embedding response did not include a vector.");
                if (vector.Length == 0)
                {
                    throw new InvalidOperationException("Embedding response returned an empty vector.");
                }

                return new EmbeddingResult(vector, vector.Length, "openai", _model);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                throw new EmbeddingClientException("Embedding response could not be parsed.", LimitDiagnosticBody(body), ex);
            }
        }
    }

    private static string? LimitDiagnosticBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var compact = Regex.Replace(body, @"\s+", " ").Trim();
        return compact.Length > 600 ? compact[..600] : compact;
    }
}

internal sealed record OpenAiEmbeddingRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("input")] string Input);

internal sealed record OpenAiEmbeddingResponse(
    [property: JsonPropertyName("data")] IReadOnlyList<OpenAiEmbeddingDatum>? Data);

internal sealed record OpenAiEmbeddingDatum(
    [property: JsonPropertyName("embedding")] float[]? Embedding);

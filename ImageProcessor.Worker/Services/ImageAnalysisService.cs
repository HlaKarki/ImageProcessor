using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ImageProcessor.Worker.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImageProcessor.Worker.Services;

public class ImageAnalysisService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<ImageAnalysisService> logger)
{
    private readonly string? _apiKey = configuration["AI:OpenAI:ApiKey"];
    private readonly string _baseUrl = (configuration["AI:OpenAI:BaseUrl"] ?? "https://api.openai.com/v1").TrimEnd('/');
    private readonly string _model = configuration["AI:OpenAI:Model"] ?? "gpt-4.1-mini";
    private readonly int _maxImageDimension = Math.Clamp(configuration.GetValue<int?>("AI:OpenAI:MaxImageDimension") ?? 1024, 256, 2048);
    private readonly decimal _inputCostUsdPer1KTokens = configuration.GetValue<decimal?>("AI:OpenAI:InputCostUsdPer1KTokens") ?? 0.00015m;
    private readonly decimal _outputCostUsdPer1KTokens = configuration.GetValue<decimal?>("AI:OpenAI:OutputCostUsdPer1KTokens") ?? 0.0006m;

    public async Task<AiAnalysisResult> AnalyzeAsync(Stream imageStream, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException("AI:OpenAI:ApiKey is not configured.");
        }

        var stopwatch = Stopwatch.StartNew();
        var dataUrl = await BuildDataUrlAsync(imageStream, cancellationToken);

        var requestBody = new
        {
            model = _model,
            temperature = 0.2,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content =
                        "You are an image analysis assistant. Return strictly valid JSON with keys: " +
                        "summary (string, 1-2 sentences), ocrText (string), tags (array of {label:string,confidence:number 0..1}), " +
                        "safety ({adult:boolean,violence:boolean,selfHarm:boolean}). Keep tags concise and confidence realistic."
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = "Analyze this image for demo metadata." },
                        new
                        {
                            type = "image_url",
                            image_url = new
                            {
                                url = dataUrl,
                                detail = "low"
                            }
                        }
                    }
                }
            }
        };

        using var client = httpClientFactory.CreateClient(nameof(ImageAnalysisService));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var response = await client.PostAsJsonAsync(
            $"{_baseUrl}/chat/completions",
            requestBody,
            cancellationToken
        );

        var rawResponse = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("AI request failed with {StatusCode}: {Body}", (int)response.StatusCode, rawResponse);
            throw new InvalidOperationException($"AI request failed with status {(int)response.StatusCode}.");
        }

        using var document = JsonDocument.Parse(rawResponse);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("AI response did not include analysis content.");
        }

        var parsed = JsonSerializer.Deserialize<AiModelPayload>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (parsed is null || string.IsNullOrWhiteSpace(parsed.Summary))
        {
            throw new InvalidOperationException("AI response payload was malformed.");
        }

        var promptTokens = TryReadInt(document.RootElement, "usage", "prompt_tokens");
        var completionTokens = TryReadInt(document.RootElement, "usage", "completion_tokens");
        var resolvedModel = TryReadString(document.RootElement, "model") ?? _model;

        stopwatch.Stop();

        return new AiAnalysisResult(
            parsed.Summary.Trim(),
            string.IsNullOrWhiteSpace(parsed.OcrText) ? null : parsed.OcrText.Trim(),
            (parsed.Tags ?? [])
                .Where(tag => !string.IsNullOrWhiteSpace(tag.Label))
                .Select(tag => new AiTagResult(
                    tag.Label.Trim(),
                    Math.Round(Math.Clamp(tag.Confidence, 0, 1), 4)))
                .Take(12)
                .ToList(),
            new AiSafetyResult(
                parsed.Safety?.Adult ?? false,
                parsed.Safety?.Violence ?? false,
                parsed.Safety?.SelfHarm ?? false
            ),
            new AiMetaResult(
                resolvedModel,
                (int)stopwatch.ElapsedMilliseconds,
                promptTokens,
                completionTokens,
                EstimateCost(promptTokens, completionTokens)
            )
        );
    }

    private async Task<string> BuildDataUrlAsync(Stream sourceStream, CancellationToken cancellationToken)
    {
        sourceStream.Position = 0;
        using var image = await Image.LoadAsync<Rgba32>(sourceStream, cancellationToken);

        image.Mutate(context => context.Resize(new ResizeOptions
        {
            Size = new Size(_maxImageDimension, _maxImageDimension),
            Mode = ResizeMode.Max
        }));

        await using var output = new MemoryStream();
        await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = 85 }, cancellationToken);
        var bytes = output.ToArray();
        var base64 = Convert.ToBase64String(bytes);
        return $"data:image/jpeg;base64,{base64}";
    }

    private double? EstimateCost(int? inputTokens, int? outputTokens)
    {
        if (inputTokens is null && outputTokens is null)
        {
            return null;
        }

        var input = ((decimal)(inputTokens ?? 0) / 1000m) * _inputCostUsdPer1KTokens;
        var output = ((decimal)(outputTokens ?? 0) / 1000m) * _outputCostUsdPer1KTokens;
        return Math.Round((double)(input + output), 6);
    }

    private static int? TryReadInt(JsonElement root, params string[] path)
    {
        if (!TryReadElement(root, out var element, path))
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static string? TryReadString(JsonElement root, params string[] path)
    {
        if (!TryReadElement(root, out var element, path))
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    private static bool TryReadElement(JsonElement root, out JsonElement element, params string[] path)
    {
        element = root;
        foreach (var segment in path)
        {
            if (!element.TryGetProperty(segment, out var next))
            {
                element = default;
                return false;
            }

            element = next;
        }

        return true;
    }

    private sealed record AiModelPayload(
        string Summary,
        string? OcrText,
        List<AiTagPayload>? Tags,
        AiSafetyPayload? Safety
    );

    private sealed record AiTagPayload(
        string Label,
        double Confidence
    );

    private sealed record AiSafetyPayload(
        bool Adult,
        bool Violence,
        bool SelfHarm
    );
}

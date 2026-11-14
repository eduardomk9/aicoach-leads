using AiCoach.Leads.Domain;
using Microsoft.Extensions.Logging;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiCoach.Leads.Application;
using Microsoft.Extensions.Options;

namespace AiCoach.Leads.Infrastructure.Agent;

/// <summary>Configuração do miner (a seção "Agent"): endpoint OpenAI-compat + o papel
/// único "miner". O modelo por CONFIG (o dono escolheu por pesquisa: gpt-oss:20b
/// $0.07; alternativa barata nemotron-3-super $0.015; off-peak deepseek-v4.1-flash
/// $0.15 — trocar NÃO é código).</summary>
public sealed class MinerAgentOptions
{
    public const string Section = "Agent";

    public string Endpoint { get; init; } = "https://ollama.com/v1/chat/completions";
    public string? ApiKey { get; init; }
    public string Model { get; init; } = "gpt-oss:20b";
    public double Temperature { get; init; } = 0.2;
    public int MaxOutputTokens { get; init; } = 400;
    public int TimeoutSeconds { get; init; } = 90;
    public bool UseJsonMode { get; init; } = true;
}

/// <summary>
/// Cliente do LLM (padrão da casa: OpenAiCompatibleAgentClient da API, adaptado ao
/// papel "miner" do job). Transporte burro + JSON mode + telemetria de tokens da
/// resposta (usage.prompt_tokens/completion_tokens quando o provedor mandar).
/// </summary>
public sealed class OpenAiCompatibleMinerAgent(
    HttpClient httpClient,
    IOptions<MinerAgentOptions> options,
    ILogger<OpenAiCompatibleMinerAgent> logger) : IMinerAgent
{
    private static readonly JsonSerializerOptions PayloadOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public async Task<SinalJuizo> AnalisarAsync(string briefing, CancellationToken cancellationToken)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.ApiKey))
        {
            throw new InvalidOperationException("Agent:ApiKey não configurada (user-secrets/env).");
        }

        var payload = new Dictionary<string, object?>
        {
            ["model"] = o.Model,
            ["temperature"] = o.Temperature,
            ["max_tokens"] = o.MaxOutputTokens,
            ["messages"] = new object[]
            {
                new { role = "system", content = MinerPrompt.Sistema },
                new { role = "user", content = $"{MinerPrompt.RotuloBriefing}\n{briefing}" },
            },
            ["response_format"] = new { type = "json_object" },
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, o.TimeoutSeconds))).Token);

        using var request = new HttpRequestMessage(HttpMethod.Post, o.Endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, PayloadOptions), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                var erro = await response.Content.ReadAsStringAsync(timeout.Token);
                throw new InvalidOperationException($"Provedor respondeu {(int)response.StatusCode}: {erro[..Math.Min(200, erro.Length)]}");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Provedor estourou o timeout do miner.");
        }

        var json = await response.Content.ReadAsStringAsync(timeout.Token);
        var (conteudo, tokensIn, tokensOut) = Extrair(json);
        logger.LogInformation("Miner respondeu: tokens in={In} out={Out}", tokensIn, tokensOut);

        var juizo = AnalisadorDeSessoes.ParsearJuizo(conteudo);
        return new SinalJuizo
        {
            SinalTreino = juizo.SinalTreino,
            SinalDieta = juizo.SinalDieta,
            SinalRelatorio = juizo.SinalRelatorio,
            Intenção = juizo.Intenção,
            Resumo = juizo.Resumo,
            Motivo = juizo.Motivo,
            TokensIn = tokensIn,
            TokensOut = tokensOut,
        };
    }

    /// <summary>Extrai choices[0].message.content + usage do envelope OpenAI-compat.
    /// Sem usage: 0/0 (o custo daquele ficou fora do relatório — o provedor não mandou).</summary>
    private static (string Conteudo, int In, int Out) Extrair(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var conteudo = root
            .GetProperty("choices").EnumerateArray().First()
            .GetProperty("message").GetProperty("content").GetString() ?? string.Empty;

        int dentro = 0, fora = 0;
        if (root.TryGetProperty("usage", out var usage)
            && usage.ValueKind == JsonValueKind.Object)
        {
            dentro = usage.TryGetProperty("prompt_tokens", out var ti) && ti.TryGetInt32(out var t1) ? t1 : 0;
            fora = usage.TryGetProperty("completion_tokens", out var to) && to.TryGetInt32(out var t2) ? t2 : 0;
        }

        return (conteudo, dentro, fora);
    }
}

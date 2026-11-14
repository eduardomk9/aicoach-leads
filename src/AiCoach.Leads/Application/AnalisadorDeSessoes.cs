using AiCoach.Leads.Domain;
using Microsoft.Extensions.Logging;

using System.Text.Json;

namespace AiCoach.Leads.Application;

/// <summary>
/// O MINEIRO (R21): orquestra a rodada — carrega o marcador, puxa as sessões novas,
/// 1 chamada do LLM por sessão, grava os sinais, atualiza o marcador (a cadência
/// incremental). Eficiência de custo: só sessões com entrada NOVA; briefing pequeno;
/// papel "miner" barato. Fracasso de uma sessão não mata a rodada (log e segue).
/// </summary>
public sealed class AnalisadorDeSessoes(
    ISessionsPort sessionsPort,
    IMinerAgent minerAgent,
    ISinalRepository sinalRepository,
    IJobEstadoPort jobEstado,
    ILogger<AnalisadorDeSessoes> logger)
{
    /// <summary>Chave do marcador no JobEstado (uma cadência para o job inteiro).</summary>
    public const string ChaveEstado = "leads-miner";

    /// <summary>Padrão caso o JobEstado esteja vazio: mineirar as últimas 72h (barato e
    /// cobre o histórico recente; rodadas seguintes ficam incrementais).</summary>
    public static readonly TimeSpan JanelaInicial = TimeSpan.FromHours(72);

    public sealed record RodadaResumo(int SessoesAnalisadas, int SinaisGravados, int TokensIn, int TokensOut, int Falhas, DateTimeOffset MarcadorAnterior, DateTimeOffset MarcadorNovo);

    /// <summary>Rodada completa. <paramref name="maxSessoes"/> = teto de segurança
    /// (custo); 0 = sem teto.</summary>
    public async Task<RodadaResumo> RodarAsync(int maxSessoes, CancellationToken cancellationToken)
    {
        var agora = DateTimeOffset.UtcNow;
        var marcador = await jobEstado.CarregarAsync(ChaveEstado, agora - JanelaInicial, cancellationToken);
        logger.LogInformation("Rodada: marcador anterior {Marcador:o}", marcador);

        var sessoes = await sessionsPort.NovasDesdeAsync(marcador, maxSessoes, cancellationToken);
        logger.LogInformation("Sessões novas: {Total}", sessoes.Count);

        var sinais = new List<SinalLead>();
        var falhas = 0;
        var tokensIn = 0;
        var tokensOut = 0;

        foreach (var slice in sessoes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var briefing = BriefingBuilder.Montar(slice);
                logger.LogDebug("Briefing ({Chars} chars) p/ sessão {Sessao}", briefing.Length, slice.SessionId);
                var juizo = await minerAgent.AnalisarAsync(briefing, cancellationToken);
                tokensIn += juizo.TokensIn;
                tokensOut += juizo.TokensOut;

                var sinaisCsv = MontarSinaisCsv(juizo);
                sinais.Add(new SinalLead
                {
                    UserId = int.Parse(slice.UserId),
                    // O TELEFONE REAL vem do SQL (o Repos resolve por Join com Users — a
                    // session do Mongo guarda user_id = UserId do SQL, a mesma identidade).
                    Phone = string.Empty,
                    SessionId = Trunca(slice.SessionId, 80),
                    Sinais = sinaisCsv,
                    Intencao = juizo.Intenção,
                    ParaQuem = juizo.ParaQuem,
                    Resumo = Trunca(juizo.Resumo, 280),
                    Motivo = Trunca(juizo.Motivo, 240),
                    ModeloUsado = "gpt-oss:20b",
                    TokensIn = juizo.TokensIn,
                    TokensOut = juizo.TokensOut,
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                falhas++;
                logger.LogWarning(ex, "Sessão {Sessao} falhou na análise — segue a rodada.", slice.SessionId);
            }
        }

        var gravados = await sinalRepository.GravarAsync(sinais, cancellationToken);

        // O marcador só avança em rodada sem falha (uma falha = o retry na próxima rodada).
        var novoMarcador = falhas == 0 ? agora : marcador;
        await jobEstado.SalvarAsync(ChaveEstado, novoMarcador, $"sessoes={sessoes.Count} sinais={gravados}", cancellationToken);

        logger.LogInformation("Rodada fechou: {Sinais} sinais, {Falhas} falhas, tokens in/out {In}/{Out}",
            gravados, falhas, tokensIn, tokensOut);
        return new RodadaResumo(sinais.Count, gravados, tokensIn, tokensOut, falhas, marcador, novoMarcador);
    }

    /// <summary>Parseia o JSON do LLM com tolerância (campos ausentes = 0/vazio).</summary>
    public static SinalJuizo ParsearJuizo(string jsonBruto)
    {
        using var doc = JsonDocument.Parse(jsonBruto);
        var root = doc.RootElement;

        int GetInt(string nome)
        {
            if (root.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i))
            {
                return Math.Clamp(i, 0, 5);
            }
            return 0;
        }

        string GetString(string nome, int max)
        {
            var v = root.TryGetProperty(nome, out var prop) && prop.ValueKind == JsonValueKind.String
                ? prop.GetString() ?? string.Empty
                : string.Empty;
            return Truncar(v, max);
        }

        // A caixinha do dono: s aceita o vocabulrio canico (o resto vira indefinido).
        var validos = new[] { "personal", "nutri", "coach", "todos", "um-e-outro" };
        var paraquemRaw = GetString("paraquem", 30).Trim().ToLowerInvariant();
        var paraquem = validos.Contains(paraquemRaw) ? paraquemRaw : "indefinido";

        return new SinalJuizo
        {
            SinalTreino = Math.Clamp(GetInt("sinal_treino"), 0, 3),
            SinalDieta = Math.Clamp(GetInt("sinal_dieta"), 0, 3),
            SinalRelatorio = Math.Clamp(GetInt("sinal_relatorio"), 0, 3),
            Intenção = GetInt("intencao"),
            ParaQuem = paraquem,
            Resumo = GetString("resumo", 280),
            Motivo = GetString("motivo", 240),
        };
    }

    private static string MontarSinaisCsv(SinalJuizo juizo)
    {
        var partes = new List<string>();
        if (juizo.SinalTreino > 0)
        {
            partes.Add("treino");
        }
        if (juizo.SinalDieta > 0)
        {
            partes.Add("dieta");
        }
        if (juizo.SinalRelatorio > 0)
        {
            partes.Add("relatorio");
        }
        return string.Join(";", partes);
    }

    private static string Trunca(string valor, int maximo) =>
        valor.Length <= maximo ? valor : valor[..maximo] + "…";

    private static string Truncar(string valor, int maximo) => Trunca(valor, maximo);
}

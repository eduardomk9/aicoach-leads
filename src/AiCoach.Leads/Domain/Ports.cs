namespace AiCoach.Leads.Domain;

/// <summary>Fatia magra de uma sessão de conversa: SÓ o que tem sinal de venda.
/// As respostas do COACH (outputs.agent_response) ficam FORA — o peso maior da
/// sessão, zero sinal ("analise só o necessário, não perca o indispensável": o
/// indispensável é a voz do usuário + o estado dos fluxos).</summary>
public sealed class SessionSlice
{
    public string UserId { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public DateTimeOffset Criado { get; init; }
    public string? Fonte { get; init; }
    public string? Nome { get; init; }
    public IReadOnlyList<MensagemSlice> Mensagens { get; init; } = [];
    public IReadOnlyList<string> Fluxos { get; init; } = [];
    public IReadOnlyDictionary<string, string?> Extracted { get; init; } = new Dictionary<string, string?>();
    public int Midias { get; init; }
}

public sealed record MensagemSlice(DateTimeOffset Timestamp, string Texto);

/// <summary>Porta de leitura das sessões (Mongo): fatias magras, só as com
/// entrada nova depois do marcador incremental.</summary>
public interface ISessionsPort
{
    /// <summary>Sessões com inputs.timestamp > marcador (ou criadas depois dele).
    /// Máximo devolvido respeitado; projeção SEM os textos do coach.</summary>
    Task<IReadOnlyList<SessionSlice>> NovasDesdeAsync(DateTimeOffset desde, int maxSessoes, CancellationToken cancellationToken);
}

/// <summary>Porta de gravação dos sinais (SQL).</summary>
public interface ISinalRepository
{
    /// <summary>Grava os sinais novos (INSERT); devolve quantos.</summary>
    Task<int> GravarAsync(IReadOnlyList<SinalLead> sinais, CancellationToken cancellationToken);
}

/// <summary>Porta do marcador de cadência (JobEstado no SQL).</summary>
public interface IJobEstadoPort
{
    Task<DateTimeOffset> CarregarAsync(string chave, DateTimeOffset padrao, CancellationToken cancellationToken);
    Task SalvarAsync(string chave, DateTimeOffset ultimaExecucao, string? notas, CancellationToken cancellationToken);
}

/// <summary>Porta do LLM minerador (OpenAI-compat; papel "miner" — modelo por config).</summary>
public interface IMinerAgent
{
    /// <summary>Analisa UM briefing (texto montado) e devolve o veredito estruturado.</summary>
    Task<SinalJuizo> AnalisarAsync(string briefing, CancellationToken cancellationToken);
}

/// <summary>Veredito do LLM sobre uma sessão (parseado do JSON).</summary>
public sealed class SinalJuizo
{
    public int SinalTreino { get; init; }
    public int SinalDieta { get; init; }
    public int SinalRelatorio { get; init; }
    public int Intenção { get; init; }
    public string Resumo { get; init; } = string.Empty;
    public string Motivo { get; init; } = string.Empty;
    public int TokensIn { get; init; }
    public int TokensOut { get; init; }
}

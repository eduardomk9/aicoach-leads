namespace AiCoach.Leads.Domain;

/// <summary>
/// Sinal de prospecção extraído de uma conversa pelo mineiro (R21). O estoque
/// comercial: a feature de venda para coaches/nutricionistas consumirá isto —
/// e o gateway de pagamento futuro usa o Users.IsAllowed como o gate do acesso
/// (o mesmo mecanismo do canal demo hoje).
/// </summary>
public sealed class SinalLead
{
    public int Id { get; init; }
    public int UserId { get; init; }
    public string Phone { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public string Sinais { get; init; } = string.Empty;
    public int Intencao { get; init; }
    public string Resumo { get; init; } = string.Empty;
    public string Motivo { get; init; } = string.Empty;
    public string ModeloUsado { get; init; } = string.Empty;
    public int TokensIn { get; init; }
    public int TokensOut { get; init; }
}

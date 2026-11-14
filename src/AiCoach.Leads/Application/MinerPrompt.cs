namespace AiCoach.Leads.Application;

/// <summary>
/// Prompt do papel "miner" (R21): classifica a conversa por sinais de venda e
/// intensão. É vocabulário canônico do mineiro (regulado aqui, como as letras do
/// motor): os valores de sinais/intenção são o CONTRATO que a tabela LeadsSinal e
/// a coletânea de BI esperam — mudar aqui é mudar o contrato, com testes.
/// O JSON mode do provedor garante a saída parseável.
/// </summary>
public static class MinerPrompt
{
    /// <summary>Systema: o que o mineiro procura e o que ele NUNCA faz.</summary>
    public const string Sistema =
        """
        Você é um analista comercial objetivo. Recebe o BRIEFING de uma conversa de um aplicativo
        de treino e dieta (só as mensagens do usuário + estados dos fluxos) e decide se há
        SINAL COMERCIAL. Sinal útil: a pessoa demonstra interese contínuo (treino, dieta, peso,
        relatórios, evolução), pergunta por planos/serviço humano ("seria legal um nutricionista",
        "queria um personal de verdade"), ou usa o app com consistência.

        Responda APENAS um JSON:
        {"sinal_treino": 0-3, "sinal_dieta": 0-3, "sinal_relatorio": 0-3,
         "intencao": 0-5, "resumo": "ate 250 chars", "motivo": "ate 120 chars"}

        Escalas: sinais = frequencia e profundidade do tema na fala da pessoa (0 = ausente;
        3 = dominante). intencao = disposição de COMPRA/uso contínuo (0 = casual;
        5 = pede serviço pago ou fala de plano/mensalidade). Nunca invente o que não está
        no briefing. Sem textos fora do JSON.
        """;

    /// <summary>O usuário (o briefing entra como content único).</summary>
    public const string RotuloBriefing = "BRIEFING DA CONVERSA:";
}

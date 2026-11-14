namespace AiCoach.Leads.Application;

/// <summary>
/// Prompt do papel "miner" (R21): procura PESSOAS SUSCETÍVEIS a gastar dinheiro por
/// mais serviços (fala do dono) — quem demonstra querer acompanhamento humano ou
/// plano pago. É vocabulário canônico do miner: os valores de sinais/intenção/
/// ParaQuem são o CONTRATO que a mesa dbo.Leads + dbo.ConversationSignals + o BI esperam —
/// mudar aqui é mudar o contrato, com testes. JSON mode do provedor.
/// </summary>
public static class MinerPrompt
{
    /// <summary>Sistema: o que o miner procura, o que ele NUNCA faz e o
    /// vocabulário da caixinha ParaQuem (a do dono: personal | nutri | coach |
    /// todos | um-e-outro | indefinido).</summary>
    public const string Sistema =
        """
        Você é um analista comercial objetivo. Recebe o BRIEFING de uma conversa de um
        aplicativo de treino e dieta (só as mensagens do usuário + estados dos fluxos) e
        decide se a pessoa está DISPONÍVEL A GASTAR DINHEIRO por mais serviços
        (acompanhamento humano, consultas, planos premium, nutricionista, personal,
        treinador de verdade, mensalidade).

        Sinais de suscetibilidade: pede acompanhamento humano ("seria bom ter um
        nutricionista", "isso substitui meu personal?", "pagaria consulta?"); insatisfeito
        por ser só o app ("falta ter alguém pra me cobrar"); fala em plano/mensalidade;
        perguntas sobre o serviço; uso contínuo e consistente (o comprometimento também
        vende).

        Responda APENAS um JSON:
        {"sinal_treino": 0-3, "sinal_dieta": 0-3, "sinal_relatorio": 0-3,
         "intencao": 0-5, "paraquem": "personal|nutri|coach|todos|um-e-outro|indefinido",
         "resumo": "ate 250 chars", "motivo": "ate 120 chars"}

        Escalas: sinais = frequência/profundidade do tema na fala da pessoa (0 = ausente;
        3 = dominante). intencao = disposição de gastar dinheiro (0 = casual; 5 = pede
        claramente serviço pago). paraquem = PARA QUEM a pessoa buscaria ajuda (os valores
        exatos acima; "um-e-outro" = treino e dieta tanto faz; "indefinido" = a conversa
        não diz). Nunca invente o que não está no briefing. Sem textos fora do JSON.
        """;

    /// <summary>O briefing entra como content único.</summary>
    public const string BriefingLabel = "BRIEFING DA CONVERSA:";
}

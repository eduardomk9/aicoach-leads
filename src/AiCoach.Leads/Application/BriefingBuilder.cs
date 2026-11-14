namespace AiCoach.Leads.Application;

/// <summary>
/// Monta o BRIEFING de uma sessão — o só-o-necessário sem perder o indispensável
/// (R21, fala do dono). Cortes de custo: mensagem ≤ <see cref="CapMensagem"/>;
/// sessão > <see cref="JanelaTotal"/> mensagens = as 3 PRIMEIRAS (a intenção de
/// abertura nunca se perde) + as 9 ÚLTIMAS (a intenção atual); teto total do
/// briefing ~<see cref="CapTotal"/> chars. As respostas do COACH não entram
/// (a porta joga fora na projeção — aqui só defende o contrato).
/// </summary>
public static class BriefingBuilder
{
    /// <summary>Cada mensagem do usuário entra com até 400 chars (o resto é tagarelice).</summary>
    public const int CapMensagem = 400;

    /// <summary>Acima disto: as 3 primeiras + as últimas 9.</summary>
    public const int JanelaTotal = 12;

    /// <summary>Primeiras mantidas quando a sessão estoura a janela.</summary>
    public const int Cabeça = 3;

    /// <summary>Teto do briefing inteiro em chars (~2k tokens de entrada).</summary>
    public const int CapTotal = 6000;

    /// <summary>Extrai só as mensagens TEXTUAIS do usuário, já com o cap individual.</summary>
    public static IReadOnlyList<(DateTimeOffset Ts, string Texto)> MensagensUtilizaveis(Domain.SessionSlice slice) =>
        slice.Mensagens
            .Where(m => m.Texto.Length > 0)
            .Select(m => {
                var texto = m.Texto.Length <= CapMensagem ? m.Texto : m.Texto[..CapMensagem] + "…";
                return (Ts: m.Timestamp, Texto: texto);
            })
            .ToList();

    /// <summary>Monta o briefing: quem fala, a abertura, o presente e os estados.</summary>
    public static string Montar(Domain.SessionSlice slice)
    {
        var mensagens = MensagensUtilizaveis(slice);
        var usadas = mensagens.Count > JanelaTotal
            ? mensagens.Take(Cabeça).Concat(mensagens.TakeLast(JanelaTotal - Cabeça)).ToList()
            : mensagens;

        var sb = new System.Text.StringBuilder();
        AppendLinha(sb, $"USUARIO #{slice.UserId} fonte={slice.Fonte ?? "?"} sessao={Trunca(slice.SessionId, 40)} criada={slice.Criado:yyyy-MM-dd}");
        if (slice.Nome is { Length: > 0 })
        {
            AppendLinha(sb, $"NOME_INFORMADO={slice.Nome}");
        }

        if (slice.Fluxos.Count > 0)
        {
            AppendLinha(sb, "FLUXOS: " + string.Join("; ", slice.Fluxos));
        }

        if (slice.Extracted.Count > 0)
        {
            var extra = string.Join("; ", slice.Extracted
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .Select(kv => $"{kv.Key}={Trunca(kv.Value!, 60)}"));
            if (extra.Length > 0)
            {
                AppendLinha(sb, "EXTRACAO_COACH: " + Trunca(extra, 350));
            }
        }

        AppendLinha(sb, "MENSAGENS_DO_USUARIO (a voz do cliente):");
        foreach (var (ts, texto) in usadas)
        {
            AppendLinha(sb, $"- [{ts:MM-dd HH:mm}] {texto}");
        }

        // Teto total: corta da CAUDA (a abertura e o cabeçalho são indispensáveis).
        var bruto = sb.ToString();
        if (bruto.Length > CapTotal)
        {
            bruto = bruto[..CapTotal] + "…";
        }

        return bruto;
    }

    private static void AppendLinha(System.Text.StringBuilder sb, string linha) => sb.AppendLine(linha);

    private static string Trunca(string valor, int maximo) =>
        valor.Length <= maximo ? valor : valor[..maximo] + "…";
}

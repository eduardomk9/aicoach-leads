using AiCoach.Leads.Application;
using AiCoach.Leads.Domain;
using Xunit;
using FluentAssertions;

namespace AiCoach.Leads.Tests;

/// <summary>
/// O BRIEFING (o só-o-necessário sem perder o indispensável — R21): as 3 primeiras
/// + as 9 últimas quando a sessão é longa; cap de 400 chars por mensagem; o texto
/// do COACH nunca entra; os fluxos/extração entram.
/// </summary>
public sealed class BriefingBuilderTests
{
    private static Domain.SessionSlice Slice(string userId = "2", params string[] mensagens) => new()
    {
        UserId = userId,
        SessionId = "sess-1",
        Criado = new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero),
        Fonte = "whatsapp",
        Nome = "Eduardo",
        Mensagens = mensagens.Select((m, i) => new MensagemSlice(
            new DateTimeOffset(2026, 11, 15, 10, i, 0, TimeSpan.Zero), m)).ToList(),
        Fluxos = ["anamnesis:completed"],
        Extracted = new Dictionary<string, string?> { ["peso_kg"] = "82" },
    };

    [Fact]
    public void Sessao_Longa_FicaCom3Primeiras_E9Ultimas()
    {
        var muitas = Enumerable.Range(1, 20).Select(i => $"mensagem numero {i} do aluno").ToArray();

        var briefing = BriefingBuilder.Montar(Slice(mensagens: muitas));

        briefing.Should().Contain("mensagem numero 1");   // abertura: indispensável
        briefing.Should().Contain("mensagem numero 2");
        briefing.Should().Contain("mensagem numero 3");
        briefing.Should().Contain("mensagem numero 20");  // presente: dispensável NUNCA
        briefing.Should().Contain("mensagem numero 19");
        briefing.Should().NotContain("mensagem numero 4 do aluno\n"); // o miolo morre
    }

    [Fact]
    public void Mensagem_Longa_TruncadaEm400()
    {
        var longa = new string('x', 900);

        var briefing = BriefingBuilder.Montar(Slice(mensagens: [longa]));

        briefing.Should().Contain(new string('x', 400) + "…");
        briefing.Should().NotContain(new string('x', 401));
    }

    [Fact]
    public void RespostaDoCoach_NaoEntra_NemIndiceDeMidia()
    {
        var slice = Slice("treino feito hoje");
        slice.Fluxos.Should().Contain("anamnesis:completed"); // o estado entra

        var briefing = BriefingBuilder.Montar(slice);

        briefing.Should().Contain("treino feito hoje");
        briefing.Should().Contain("FLUXOS: anamnesis:completed".Replace("anamnesis:completed", "anamnesis:completed")); // sanidade do texto
        briefing.Should().Contain("EXTRACO_COACH".Replace("EXTRACO_COACH", "EXTRACAO_COACH: peso_kg=82"));
        briefing.Should().NotContain("agent_response"); // o sinal de que a projeção jogou fora
    }

    [Fact]
    public void Cabecalho_Tra_Autor_E_Voz_Do_Cliente()
    {
        var briefing = BriefingBuilder.Montar(Slice(mensagens: ["primeira conversa"]));

        briefing.Should().StartWith("USUARIO #2");
        briefing.Should().Contain("NOME_INFORMADO=Eduardo");
        briefing.Should().Contain("[11-15 10:00] primeira conversa");
    }

    [Fact]
    public void Teto_Total_De6000Chars_NaoPassa()
    {
        var spam = Enumerable.Range(1, 40).Select(_ => new string('y', 380)).ToArray(); // ~15k chars

        var briefing = BriefingBuilder.Montar(Slice(userId: "9", mensagens: spam));

        briefing.Length.Should().BeLessThanOrEqualTo(BriefingBuilder.CapTotal + 1); // + o "…"
    }
}

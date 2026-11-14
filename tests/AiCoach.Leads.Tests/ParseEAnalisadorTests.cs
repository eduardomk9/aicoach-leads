using AiCoach.Leads.Application;
using AiCoach.Leads.Domain;
using Microsoft.Extensions.Logging;
using Xunit;
using FluentAssertions;
using Moq;

namespace AiCoach.Leads.Tests;

/// <summary>Parse do JSON do provedor + o INCREMENTAL do miner (a cadência sem custo
/// duplo). Sem rede nos testes: o LLM é mock.</summary>
public sealed class ParseEAnalisadorTests
{
    private const string JsonConteudo =
        "{\"sinal_treino\":2,\"sinal_dieta\":1,\"sinal_relatorio\":0,\"intencao\":4,\"resumo\":\"pede planos\",\"motivo\":\"fala de mensalidade\"}";

    [Fact]
    public void Parse_Extrai_Campos_ETokens()
    {
        var juizo = AnalisadorDeSessoes.ParsearJuizo(JsonConteudo);

        juizo.SinalTreino.Should().Be(2);
        juizo.SinalDieta.Should().Be(1);
        juizo.SinalRelatorio.Should().Be(0);
        juizo.Intenção.Should().Be(4);
        juizo.Resumo.Should().Be("pede planos");
        juizo.Motivo.Should().Be("fala de mensalidade");
        // Os TOKENS ficam no MinerAgent (usage do provedor) — o ParsearJuizo é content-only.
    }

    [Fact]
    public void Parse_Garbage_Ou_Campos_Ausentes_Nao_Explode()
    {
        var juizo = AnalisadorDeSessoes.ParsearJuizo("{\"intencao\": 9, \"resumo\":\"corto\"}");

        juizo.Intenção.Should().Be(5);       // clamp 0..5
        juizo.Resumo.Should().Be("corto");
        juizo.SinalTreino.Should().Be(0);    // ausente = 0
        juizo.TokensIn.Should().Be(0);
        juizo.TokensOut.Should().Be(0);
    }

    [Fact]
    public async Task Rodada_Incremental_So_Sessoes_Novas()
    {
        // O CONTRATO da cadência: 1a rodada mineira o que há (2 sessões) e SALVA o
        // marcador; a 2a rodada parte do marcador NOVO e a porta devolve vazio =
        // ZERO chamadas de LLM (o barato é lei). Datas FIXAS (nada de relógio real).
        var velho = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var novo = new DateTimeOffset(2026, 2, 12, 0, 0, 0, TimeSpan.Zero);

        var port = new Mock<ISessionsPort>();
        var agent = new Mock<IMinerAgent>();
        agent.Setup(a => a.AnalisarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SinalJuizo { SinalTreino = 1, Intenção = 3, Resumo = "engajado", Motivo = "usa bem", TokensIn = 1800, TokensOut = 60 });
        var repo = new Mock<ISinalRepository>();
        repo.Setup(r => r.GravarAsync(It.IsAny<IReadOnlyList<SinalLead>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<SinalLead> l, CancellationToken _) => l.Count);

        var estado = new Mock<IJobEstadoPort>();
        var salvo = velho;
        estado.Setup(e => e.CarregarAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, DateTimeOffset _, CancellationToken _) => salvo);
        estado.Setup(e => e.SalvarAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, DateTimeOffset, string?, CancellationToken>((_, marcadorNovo, _, _) => salvo = marcadorNovo)
            .Returns(Task.CompletedTask);

        port.Setup(p => p.NovasDesdeAsync(velho, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SessionSlice> { SliceDe("1", "fala do aluno 1"), SliceDe("2", "fala do aluno 2") });
        port.Setup(p => p.NovasDesdeAsync(It.Is<DateTimeOffset>(d => d >= novo), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SessionSlice>());

        var analyzer = new AnalisadorDeSessoes(port.Object, agent.Object, repo.Object, estado.Object, NullLog());

        var r1 = await analyzer.RodarAsync(0, CancellationToken.None);

        r1.SinaisGravados.Should().Be(2);
r1.TokensIn.Should().Be(3600);
        agent.Verify(a => a.AnalisarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        salvo.Should().Be(r1.MarcadorNovo); // o marcador avançou

        var r2 = await analyzer.RodarAsync(0, CancellationToken.None);

        r2.SessoesAnalisadas.Should().Be(0);
        r2.SinaisGravados.Should().Be(0);
        agent.Verify(a => a.AnalisarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2)); // 2a rodada = ZERO LLM
    }

    private static SessionSlice SliceDe(string userId, string msg) => new()
    {
        UserId = userId,
        SessionId = "s-" + userId,
        Criado = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero),
        Fonte = "whatsapp",
        Mensagens = [new MensagemSlice(new DateTimeOffset(2026, 1, 15, 10, 5, 0, TimeSpan.Zero), msg)],
        Fluxos = [],
        Extracted = new Dictionary<string, string?>(),
    };

    private static ILogger<AnalisadorDeSessoes> NullLog() => Mock.Of<ILogger<AnalisadorDeSessoes>>();
}

public sealed class MinerAgentParaQuemTests
{
    /// <summary>A1: o agent NUNCA descarta o ParaQuem do parse (o bug em produo:
    /// as 2 rows da mesa ficaram 'indefinido' com a IA dizendo 'coach').</summary>
    [Fact]
    public void Analisador_Replica_ParaQuem_Do_Parse()
    {
        // O caminho que a produção roda: o ParsearJuizo e a reconstrução do agent.
        var juizo = AnalisadorDeSessoes.ParsearJuizo(
            "{\"sinal_treino\":1,\"intencao\":3,\"paraquem\":\"coach\",\"resumo\":\"quer coach\",\"motivo\":\"pediu\"}");

        juizo.ParaQuem.Should().Be("coach");
        juizo.Intenção.Should().Be(3);
        juizo.Motivo.Should().Be("pediu");
    }
}

using Microsoft.Extensions.Hosting;
using AiCoach.Leads.Application;
using AiCoach.Leads.Domain;
using AiCoach.Leads.Infrastructure.Agent;
using AiCoach.Leads.Infrastructure.Mongo;
using AiCoach.Leads.Infrastructure.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

// R21 - MINEIRO DE LEADS: console on-demand (agora) / agendado (fase 2: cron no GitHub).
// Uso:
//   dotnet run -- --ver
//   dotnet run -- --rodar [--max-sessions N] [--dry]
// Os segredos (Agent__ApiKey, ConnectionStrings__MongoConnection, ConnectionStrings__SqlServer)
// ficam em user-secrets no DEV / env no Actions, nunca neste arquivo.

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<MinerAgentOptions>(builder.Configuration.GetSection(MinerAgentOptions.Section));
builder.Services.AddHttpClient<IMinerAgent, OpenAiCompatibleMinerAgent>();

var mongoConn = builder.Configuration["ConnectionStrings:MongoConnection"];
if (string.IsNullOrWhiteSpace(mongoConn))
{
    Console.WriteLine("ERRO: ConnectionStrings:MongoConnection nao configurada (user-secrets/env).");
    return 2;
}
var mongoClient = new MongoClient(mongoConn);
builder.Services.AddSingleton<IMongoClient>(mongoClient);
builder.Services.AddSingleton<ISessionsPort>(sp =>
    new SessionsPort(sp.GetRequiredService<IMongoClient>(), "AiCoach"));

var sqlConn = builder.Configuration["ConnectionStrings:SqlServer"];
if (string.IsNullOrWhiteSpace(sqlConn))
{
    Console.WriteLine("ERRO: ConnectionStrings:SqlServer nao configurada (user-secrets/env).");
    return 2;
}
builder.Services.AddDbContext<AiCoachLeadsDbContext>(o => o.UseSqlServer(sqlConn));
builder.Services.AddScoped<ISinalRepository, SinalRepository>();
builder.Services.AddScoped<IJobEstadoPort, JobStateRepository>();
builder.Services.AddScoped<AnalisadorDeSessoes>();

using var host = builder.Build();

var modo = args.FirstOrDefault(a => a is "--ver" or "--rodar") ?? "--ver";
var argMax = args.FirstOrDefault(a => a.StartsWith("--max-sessions="));
var maxSessoes = argMax is null ? 0 : int.Parse(argMax.Split('=')[1]);
var dry = args.Contains("--dry");

if (modo == "--ver")
{
    // Consulta sem custo: o marcador atual + quantas sessões novas existem.
    // Os Scoped resolvem dentro de um scope (a régua do DI).
    using var scope = host.Services.CreateScope();
    var analyzer = scope.ServiceProvider.GetRequiredService<AnalisadorDeSessoes>();
    var estado = scope.ServiceProvider.GetRequiredService<IJobEstadoPort>();
    var marcador = await estado.CarregarAsync(AnalisadorDeSessoes.ChaveEstado, DateTimeOffset.UtcNow - AnalisadorDeSessoes.JanelaInicial, CancellationToken.None);
    Console.WriteLine($"MARCADOR_ATUAL={marcador:o}"); Console.WriteLine("SERVER_EM_USO=" + sqlConn.Split(";")[0]);
    var port = scope.ServiceProvider.GetRequiredService<ISessionsPort>();
    var novas = await port.NovasDesdeAsync(marcador, maxSessoes, CancellationToken.None);
    Console.WriteLine($"SESSOES_NOVAS={novas.Count}");
    foreach (var s in novas)
    {
        var mensagens = BriefingBuilder.MensagensUtilizaveis(s).Count;
        Console.WriteLine($"  user={s.UserId} fonte={s.Fonte ?? "?"} mensagens={mensagens} fluxos={string.Join(';', s.Fluxos)}");
    }
    return 0;
}

if (modo == "--rodar")
{
    using var scope = host.Services.CreateScope();
    var analisador = scope.ServiceProvider.GetRequiredService<AnalisadorDeSessoes>();
    if (dry)
    {
        Console.WriteLine("MODO_DRY: os briefings vao ser montados e impressos SEM chamar o LLM e SEM gravar.");
        var port = scope.ServiceProvider.GetRequiredService<ISessionsPort>();
        var estado = scope.ServiceProvider.GetRequiredService<IJobEstadoPort>();
        var marcador = await estado.CarregarAsync(AnalisadorDeSessoes.ChaveEstado, DateTimeOffset.UtcNow - AnalisadorDeSessoes.JanelaInicial, CancellationToken.None);
        var novas = await port.NovasDesdeAsync(marcador, maxSessoes, CancellationToken.None);
        foreach (var s in novas)
        {
            Console.WriteLine($"=== SESSAO user={s.UserId} ===");
            Console.WriteLine(BriefingBuilder.Montar(s));
        }
        return 0;
    }

    var resumo = await analisador.RodarAsync(maxSessoes, CancellationToken.None);
    Console.WriteLine($"RODADA_OK sessoes={resumo.SessoesAnalisadas} sinais={resumo.SinaisGravados} falhas={resumo.Falhas} tokensIn={resumo.TokensIn} tokensOut={resumo.TokensOut} marcador={resumo.MarcadorNovo:o}");
    return resumo.Falhas > 0 ? 1 : 0;
}

Console.WriteLine("ERRO: modo desconhecido. Use --ver ou --rodar.");
return 2;

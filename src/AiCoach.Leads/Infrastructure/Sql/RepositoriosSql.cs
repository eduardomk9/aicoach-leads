using Microsoft.Extensions.Logging;
using AiCoach.Leads.Domain;
using Microsoft.EntityFrameworkCore;

namespace AiCoach.Leads.Infrastructure.Sql;

/// <summary>Repositório SQL: grava a EVIDÊNCIA por conversa (SinalConversa 1:N) e faz
/// o UPSERT na MESA DE VENDAS (Leads — a análise mais recente por pessoa). O estoque
/// só aceita usuário REAL: as sessões de ids que não existem em Users (as personas
/// legadas do R18 no Atlas) ficam fora (sem FK violation, o log conta). O NOME do
/// lead vem de Users (a identidade limpa) — o miner não toca em Onboardings.</summary>
public sealed class SinalRepository(AiCoachLeadsDbContext db, ILogger<SinalRepository> logger) : ISinalRepository
{
    public async Task<int> GravarAsync(IReadOnlyList<SinalLead> sinais, CancellationToken cancellationToken)
    {
        if (sinais.Count == 0)
        {
            return 0;
        }

        var ids = sinais.Select(s => s.UserId).Distinct().ToList();
        var users = await db.Users
            .Where(u => ids.Contains(u.UserId))
            .Select(u => new { u.UserId, u.Phone, u.Name })
            .ToDictionaryAsync(x => x.UserId, x => (x.Phone, x.Name), cancellationToken);

        var gravados = 0;
        foreach (var sinal in sinais)
        {
            if (!users.TryGetValue(sinal.UserId, out var user) || string.IsNullOrWhiteSpace(user.Phone))
            {
                logger.LogInformation("Sinal ignorado: user {UserId} nao existe em Users (sessao legada).", sinal.UserId);
                continue;
            }

            var phone = user.Phone;
            var nome = string.IsNullOrWhiteSpace(user.Name) ? "(sem nome)" : user.Name;

            // 1) A EVIDÊNCIA por conversa (nunca duplica por (UserId, SessionId)). O
            // LeadId ligaa mesa quando ela já existe; nasce null e a mesa-upscreve.
            var evidenciaExiste = await db.SinalConversa
                .AnyAsync(l => l.UserId == sinal.UserId && l.SessionId == sinal.SessionId, cancellationToken);
            var mesa = await db.Leads.FirstOrDefaultAsync(l => l.UserId == sinal.UserId, cancellationToken);

            if (!evidenciaExiste)
            {
                db.SinalConversa.Add(new SinalConversaEntity
                {
                    UserId = sinal.UserId,
                    LeadId = mesa?.Id,
                    SessionId = sinal.SessionId,
                    AnalisadoEm = DateTime.UtcNow,
                    Sinais = sinal.Sinais,
                    Intencao = sinal.Intencao,
                    Resumo = sinal.Resumo,
                    Motivo = sinal.Motivo,
                    ModeloUsado = sinal.ModeloUsado,
                    TokensIn = sinal.TokensIn,
                    TokensOut = sinal.TokensOut,
                    ParaQuem = sinal.ParaQuem,
                });
                gravados++;
            }

            // 2) A MESA (o upsert da análise mais recente por pessoa: desce nunca).
            if (mesa is null)
            {
                db.Leads.Add(new MesaLeadEntity
                {
                    UserId = sinal.UserId,
                    Nome = nome,
                    Telefone = phone,
                    Intencao = sinal.Intencao,
                    Sinais = sinal.Sinais,
                    Contexto = sinal.Resumo,
                    Estagio = "CAPTURADO",
                    Origem = "miner",
                    SessionId = sinal.SessionId,
                    ParaQuem = sinal.ParaQuem,
                    UltimaMuda = DateTime.UtcNow,
                });
                gravados++;
            }
            else if (sinal.Intencao >= mesa.Intencao || sinal.SessionId == mesa.SessionId)
            {
                mesa.Intencao = sinal.Intencao;
                mesa.Sinais = sinal.Sinais;
                mesa.Contexto = sinal.Resumo;
                mesa.SessionId = sinal.SessionId;
                mesa.ParaQuem = sinal.ParaQuem;
                mesa.UltimaMuda = DateTime.UtcNow;
                gravados++;
            }
        }

        return await db.SaveChangesAsync(cancellationToken);
    }
}

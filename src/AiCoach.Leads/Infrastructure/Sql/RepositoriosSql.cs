using AiCoach.Leads.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiCoach.Leads.Infrastructure.Sql;

/// <summary>Repositório SQL: APPEND na evidência (ConversationSignals, LeadId) e o
/// UPSERT na mesa (Leads - a análise mais recente). O estoque só aceita usuário REAL
/// (o skip das sessões legidas). O nome do lead vem de Users; o miner no toca em
/// Onboardings.</summary>
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

            // 0) A MESA: o lead j existe? (1 row por pessoa - o upsert da recente)
            var lead = await db.Leads.FirstOrDefaultAsync(l => l.UserId == sinal.UserId, cancellationToken);
            if (lead is null)
            {
                db.Leads.Add(new LeadEntity
                {
                    UserId = sinal.UserId,
                    Name = nome,
                    Phone = phone,
                    Intent = sinal.Intencao,
                    Signals = sinal.Sinais,
                    Context = sinal.Resumo,
                    Stage = "CAPTURADO",
                    Origin = "miner",
                    SessionId = sinal.SessionId,
                    DesiredProfile = sinal.ParaQuem,
                    CreatedAt = DateTime.UtcNow,
                    LastModified = DateTime.UtcNow,
                });
                await db.SaveChangesAsync(cancellationToken);
                gravados++;

                // A evidncia liga ao lead novo (o LeadId nasceu no save).
                db.ConversationSignals.Add(new ConversationSignalEntity
                {
                    LeadId = db.Leads.Local.First(l => l.UserId == sinal.UserId).LeadId,
                    UserId = sinal.UserId,
                    Phone = phone,
                    SessionId = sinal.SessionId,
                    AnalyzedAt = DateTime.UtcNow,
                    Signals = sinal.Sinais,
                    Intent = sinal.Intencao,
                    Summary = sinal.Resumo,
                    Reason = sinal.Motivo,
                    ModelUsed = sinal.ModeloUsado,
                    TokensIn = sinal.TokensIn,
                    TokensOut = sinal.TokensOut,
                });
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            // 1) A EVIDNCIA (append por rodada; nunca duplica por (LeadId, SessionId)).
            var evidenciaExiste = await db.ConversationSignals
                .AnyAsync(l => l.LeadId == lead.LeadId && l.SessionId == sinal.SessionId, cancellationToken);
            if (!evidenciaExiste)
            {
                db.ConversationSignals.Add(new ConversationSignalEntity
                {
                    LeadId = lead.LeadId,
                    UserId = sinal.UserId,
                    Phone = phone,
                    SessionId = sinal.SessionId,
                    AnalyzedAt = DateTime.UtcNow,
                    Signals = sinal.Sinais,
                    Intent = sinal.Intencao,
                    Summary = sinal.Resumo,
                    Reason = sinal.Motivo,
                    ModelUsed = sinal.ModeloUsado,
                    TokensIn = sinal.TokensIn,
                    TokensOut = sinal.TokensOut,
                });
                gravados++;
            }

            // 2) A MESA (o upsert da anlise mais recente: sobe, desce nunca).
            if (sinal.Intencao >= lead.Intent || sinal.SessionId == lead.SessionId)
            {
                lead.Intent = sinal.Intencao;
                lead.Signals = sinal.Sinais;
                lead.Context = sinal.Resumo;
                lead.SessionId = sinal.SessionId;
                lead.DesiredProfile = sinal.ParaQuem;
                lead.LastModified = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                gravados++;
            }
        }

        return gravados;
    }
}

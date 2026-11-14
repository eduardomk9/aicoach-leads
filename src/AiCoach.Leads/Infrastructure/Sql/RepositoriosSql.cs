using AiCoach.Leads.Domain;
using AiCoach.Leads.Infrastructure.Sql;
using Microsoft.EntityFrameworkCore;

namespace AiCoach.Leads.Infrastructure.Sql;

/// <summary>Repositório SQL dos sinais: grava em Lote resolvendo o TELEFONE por join
/// (Users) — a sessão do Mongo traz só UserId, a mesma identidade.</summary>
public sealed class SinalRepository(AiCoachLeadsDbContext db) : ISinalRepository
{
    public async Task<int> GravarAsync(IReadOnlyList<SinalLead> sinais, CancellationToken cancellationToken)
    {
        if (sinais.Count == 0)
        {
            return 0;
        }

        foreach (var sinal in sinais)
        {
            // Os sinais NUNCA duplicam: 1 linha por (UserId, SessionId) — o rerun após
            // falha parcial não cria gêmeo.
            var existe = await db.LeadsSinal
                .AnyAsync(l => l.UserId == sinal.UserId && l.SessionId == sinal.SessionId, cancellationToken);
            if (existe)
            {
                continue;
            }

            var phone = await db.Users
                .Where(u => u.UserId == sinal.UserId)
                .Select(u => u.Phone)
                .FirstOrDefaultAsync(cancellationToken) ?? sinal.UserId.ToString();

            db.LeadsSinal.Add(new LeadSinalEntity
            {
                UserId = sinal.UserId,
                Phone = phone,
                SessionId = sinal.SessionId,
                AnalisadoEm = DateTime.UtcNow,
                Sinais = sinal.Sinais,
                Intencao = sinal.Intencao,
                Resumo = sinal.Resumo,
                Motivo = sinal.Motivo,
                ModeloUsado = sinal.ModeloUsado,
                TokensIn = sinal.TokensIn,
                TokensOut = sinal.TokensOut,
            });
        }

        return await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Marcador de cadência (JobEstado) — o incremental do miner.</summary>
public sealed class JobEstadoRepository(AiCoachLeadsDbContext db) : IJobEstadoPort
{
    public async Task<DateTimeOffset> CarregarAsync(string chave, DateTimeOffset padrao, CancellationToken cancellationToken)
    {
        var e = await db.JobEstado.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Chave == chave, cancellationToken);
        return e is null
            ? padrao
            : new DateTimeOffset(DateTime.SpecifyKind(e.UltimaExecucao, DateTimeKind.Utc));
    }

    public async Task SalvarAsync(string chave, DateTimeOffset ultimaExecucao, string? notas, CancellationToken cancellationToken)
    {
        var e = await db.JobEstado.FirstOrDefaultAsync(x => x.Chave == chave, cancellationToken);
        if (e is null)
        {
            db.JobEstado.Add(new JobEstadoEntity { Chave = chave, UltimaExecucao = ultimaExecucao.UtcDateTime, Notas = notas });
        }
        else
        {
            e.UltimaExecucao = ultimaExecucao.UtcDateTime;
            e.Notas = notas;
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}

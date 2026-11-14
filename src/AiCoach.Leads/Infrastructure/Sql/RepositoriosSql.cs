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

        // O ESTOQUE SO ACEITA USUARIO REAL: sessoes de ids que nao existem em Users
        // (as personas legadas do R18 no Atlas) ficam DE FORA (sem FK violation).
        var ids = sinais.Select(s => s.UserId).Distinct().ToList();
        var phones = await db.Users
            .Where(u => ids.Contains(u.UserId))
            .Select(u => new { u.UserId, u.Phone })
            .ToDictionaryAsync(x => x.UserId, x => x.Phone, cancellationToken);

        foreach (var sinal in sinais)
        {
            if (!phones.TryGetValue(sinal.UserId, out var phone) || string.IsNullOrWhiteSpace(phone))
            {
                continue;
            }

            // Os sinais NUNCA duplicam: 1 linha por (UserId, SessionId) — o rerun aps
            // falha parcial no cria gmeo.
            var existe = await db.LeadsSinal
                .AnyAsync(l => l.UserId == sinal.UserId && l.SessionId == sinal.SessionId, cancellationToken);
            if (existe)
            {
                continue;
            }

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

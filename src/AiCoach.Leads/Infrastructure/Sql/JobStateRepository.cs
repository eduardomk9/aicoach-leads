using AiCoach.Leads.Domain;
using Microsoft.EntityFrameworkCore;

namespace AiCoach.Leads.Infrastructure.Sql;

/// <summary>O marcador de cadncia (JobEstado via JobStates): o EF do miner.</summary>
public sealed class JobStateRepository(AiCoachLeadsDbContext db) : IJobEstadoPort
{
    public async Task<DateTimeOffset> CarregarAsync(string chave, DateTimeOffset padrao, CancellationToken cancellationToken)
    {
        var e = await db.JobStates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.JobStateId == chave, cancellationToken);
        return e is null
            ? padrao
            : new DateTimeOffset(DateTime.SpecifyKind(e.LastRunAt, DateTimeKind.Utc));
    }

    public async Task SalvarAsync(string chave, DateTimeOffset ultimaExecucao, string? notas, CancellationToken cancellationToken)
    {
        var e = await db.JobStates.FirstOrDefaultAsync(x => x.JobStateId == chave, cancellationToken);
        if (e is null)
        {
            db.JobStates.Add(new JobStateEntity { JobStateId = chave, LastRunAt = ultimaExecucao.UtcDateTime, Notes = notas });
        }
        else
        {
            e.LastRunAt = ultimaExecucao.UtcDateTime;
            e.Notes = notas;
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}

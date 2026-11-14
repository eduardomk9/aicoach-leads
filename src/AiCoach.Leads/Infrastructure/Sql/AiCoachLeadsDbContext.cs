using AiCoach.Leads.Domain;
using Microsoft.EntityFrameworkCore;

namespace AiCoach.Leads.Infrastructure.Sql;

/// <summary>Entidade EF da EVIDÊNCIA por conversa (tabela física "SinalConversa" —
/// o miner grava 1 linha por análise; substitui a antiga "LeadsSinal", ddl-006).
/// A coluna física é "Id" (IDENTITY) — mapeamento explícito (a régua).</summary>
public class SinalConversaEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }

    /// <summary>O lead da mesa (dbo.Leads.Id) que esta análise alimentou (null = antigo).</summary>
    public int? LeadId { get; set; }

    /// <summary>A caixinha do dono: personal | nutri | coach | todos | um-e-outro | indefinido.</summary>
    public string? ParaQuem { get; set; }

    public string Phone { get; set; } = null!;
    public string SessionId { get; set; } = null!;
    public DateTime AnalisadoEm { get; set; }
    public string Sinais { get; set; } = null!;
    public int Intencao { get; set; }
    public string Resumo { get; set; } = null!;
    public string Motivo { get; set; } = null!;
    public string ModeloUsado { get; set; } = null!;
    public int TokensIn { get; set; }
    public int TokensOut { get; set; }
}

/// <summary>Entidade EF de JobEstado (o marcador da cadência do miner).</summary>
public class JobEstadoEntity
{
    public string Chave { get; set; } = null!;
    public DateTime UltimaExecucao { get; set; }
    public string? Notas { get; set; }
}

/// <summary>A MESA DE VENDAS (dbo.Leats→Leads, ddl-006): 1 row por pessoa que pode
/// gastar; o miner faz upsert da análise mais recente nela.</summary>
public class MesaLeadEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Nome { get; set; } = null!;
    public string Telefone { get; set; } = null!;

    /// <summary>0-5: a disposição de gastar (o miner atualiza).</summary>
    public int Intencao { get; set; }

    /// <summary>Os sinais livres da conversa (CSV).</summary>
    public string Sinais { get; set; } = null!;

    /// <summary>O contexto do que a pessoa quer (o resumo comercial).</summary>
    public string Contexto { get; set; } = null!;

    /// <summary>CAPTURADO | OFERECIDO | VENDIDO | EXPIRADO (o dono gerencia).</summary>
    public string Estagio { get; set; } = null!;

    /// <summary>miner | manual | indicacao.</summary>
    public string Origem { get; set; } = null!;

    public string SessionId { get; set; } = null!;

    /// <summary>A caixinha do dono: personal | nutri | coach | todos | um-e-outro | indefinido.</summary>
    public string ParaQuem { get; set; } = null!;

    public DateTime UltimaMuda { get; set; }
}

/// <summary>Contexto SQL do job (TABELAS DO MINER: SinalConversa + Leads + JobEstado;
/// Users em projeção).</summary>
public sealed class AiCoachLeadsDbContext(DbContextOptions<AiCoachLeadsDbContext> options) : DbContext(options)
{
    public DbSet<SinalConversaEntity> SinalConversa => Set<SinalConversaEntity>();
    public DbSet<MesaLeadEntity> Leads => Set<MesaLeadEntity>();
    public DbSet<JobEstadoEntity> JobEstado => Set<JobEstadoEntity>();
    public DbSet<UserPhoneEntity> Users => Set<UserPhoneEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SinalConversaEntity>(e =>
        {
            e.ToTable("SinalConversa");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("Id");
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.SessionId).HasMaxLength(80);
            e.Property(x => x.Sinais).HasMaxLength(120);
            e.Property(x => x.Resumo).HasMaxLength(280);
            e.Property(x => x.Motivo).HasMaxLength(240);
            e.Property(x => x.ModeloUsado).HasMaxLength(64);
            e.Property(x => x.ParaQuem).HasMaxLength(30);
        });

        modelBuilder.Entity<MesaLeadEntity>(e =>
        {
            e.ToTable("Leads");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("Id");
            e.Property(x => x.Nome).HasMaxLength(160);
            e.Property(x => x.Telefone).HasMaxLength(20);
            e.Property(x => x.Sinais).HasMaxLength(120);
            e.Property(x => x.Contexto).HasMaxLength(600);
            e.Property(x => x.Estagio).HasMaxLength(20);
            e.Property(x => x.Origem).HasMaxLength(16);
            e.Property(x => x.SessionId).HasMaxLength(80);
            e.Property(x => x.ParaQuem).HasMaxLength(30);
        });

        modelBuilder.Entity<JobEstadoEntity>(e =>
        {
            e.ToTable("JobEstado");
            e.HasKey(x => x.Chave);
            e.Property(x => x.Chave).HasMaxLength(64);
            e.Property(x => x.UltimaExecucao).HasColumnType("datetime2");
            e.Property(x => x.Notas).HasMaxLength(200);
        });


        // Users em PROJEÇÃO DE LEITURA (UserId/Phone/Name p/ o upsert — a governança
        // da tabela fica na API; o DDL vive na régua externa).
        modelBuilder.Entity<UserPhoneEntity>(e =>
        {
            e.ToTable("Users");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).HasColumnName("UserId");
            e.Property(x => x.Phone).HasColumnName("Phone").HasMaxLength(20);
            e.Property(x => x.Name).HasColumnName("Name").HasMaxLength(255);
        });
    }
}

/// <summary>Projeção de leitura mínima de Users (UserId + Phone + Name) para o upsert.</summary>
public class UserPhoneEntity
{
    public int UserId { get; set; }
    public string Phone { get; set; } = null!;
    public string Name { get; set; } = null!;
}

/// <summary>O marcador de cadência (JobEstado) — o EF do miner.</summary>
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


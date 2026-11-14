using AiCoach.Leads.Domain;
using Microsoft.EntityFrameworkCore;

namespace AiCoach.Leads.Infrastructure.Sql;

/// <summary>Entidade EF da EVIDÊNCIA por conversa. Tabela física: dbo.ConversationSignals.
/// Coluna física "SignalId" (IDENTITY) — o nome da tabela nos ids (a régua do dono).</summary>
public class ConversationSignalEntity
{
    public int SignalId { get; set; }
    public int LeadId { get; set; }
    public int UserId { get; set; }
    public string Phone { get; set; } = null!;
    public string SessionId { get; set; } = null!;
    public DateTime AnalyzedAt { get; set; }
    public string Signals { get; set; } = null!;
    public int Intent { get; set; }
    public string Summary { get; set; } = null!;
    public string Reason { get; set; } = null!;
    public string ModelUsed { get; set; } = null!;
    public int TokensIn { get; set; }
    public int TokensOut { get; set; }
}

/// <summary>Entidade EF de JobStates: o marcador da cadência do miner. Coluna física
/// "JobStateId" (o nome da tabela nos ids).</summary>
public class JobStateEntity
{
    public string JobStateId { get; set; } = null!;
    public DateTime LastRunAt { get; set; }
    public string? Notes { get; set; }
}

/// <summary>A MESA DE VENDAS (dbo.Leads, ddl-007): 1 row por pessoa. O miner faz o
/// UPSERT da análise mais recente; o ESTÁGIO é gerenciado pelo dono.</summary>
public class LeadEntity
{
    public int LeadId { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = null!;
    public string Phone { get; set; } = null!;

    /// <summary>0-5: a disposição de gastar (o miner atualiza).</summary>
    public int Intent { get; set; }

    /// <summary>Os sinais livres da conversa (CSV).</summary>
    public string Signals { get; set; } = null!;

    /// <summary>O contexto comercial (o resumo do que a pessoa quer).</summary>
    public string Context { get; set; } = null!;

    /// <summary>CAPTURADO | OFERECIDO | VENDIDO | DESCARTADO (o dono gerencia).</summary>
    public string Stage { get; set; } = null!;

    /// <summary>miner | manual | indicacao.</summary>
    public string Origin { get; set; } = null!;

    public string SessionId { get; set; } = null!;

    /// <summary>A caixinha do dono: personal | nutri | coach | todos | um-e-outro | indefinido.</summary>
    public string DesiredProfile { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
    public DateTime LastModified { get; set; }
}

/// <summary>Contexto SQL do job: ConversationSignals + Leads + JobState + a projeção
/// de Users. O gate (Onboardings) fica na API.</summary>
public sealed class AiCoachLeadsDbContext(DbContextOptions<AiCoachLeadsDbContext> options) : DbContext(options)
{
    public DbSet<ConversationSignalEntity> ConversationSignals => Set<ConversationSignalEntity>();
    public DbSet<LeadEntity> Leads => Set<LeadEntity>();
    public DbSet<JobStateEntity> JobStates => Set<JobStateEntity>();
    public DbSet<UserProjectionEntity> Users => Set<UserProjectionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConversationSignalEntity>(e =>
        {
            e.ToTable("ConversationSignals");
            e.HasKey(x => x.SignalId);
            e.Property(x => x.SignalId).HasColumnName("SignalId");
            e.Property(x => x.LeadId).HasColumnName("LeadId");
            e.Property(x => x.UserId).HasColumnName("UserId");
            e.Property(x => x.Phone).HasColumnName("Phone").HasMaxLength(20);
            e.Property(x => x.SessionId).HasColumnName("SessionId").HasMaxLength(80);
            e.Property(x => x.AnalyzedAt).HasColumnName("AnalyzedAt").HasColumnType("datetime2");
            e.Property(x => x.Signals).HasColumnName("Signals").HasMaxLength(120);
            e.Property(x => x.Intent).HasColumnName("Intent");
            e.Property(x => x.Summary).HasColumnName("Summary").HasMaxLength(280);
            e.Property(x => x.Reason).HasColumnName("Reason").HasMaxLength(240);
            e.Property(x => x.ModelUsed).HasColumnName("ModelUsed").HasMaxLength(64);
            e.Property(x => x.TokensIn).HasColumnName("TokensIn");
            e.Property(x => x.TokensOut).HasColumnName("TokensOut");
        });

        modelBuilder.Entity<LeadEntity>(e =>
        {
            e.ToTable("Leads");
            e.HasKey(x => x.LeadId);
            e.Property(x => x.LeadId).HasColumnName("LeadId");
            e.Property(x => x.UserId).HasColumnName("UserId");
            e.Property(x => x.Name).HasColumnName("Name").HasMaxLength(160);
            e.Property(x => x.Phone).HasColumnName("Phone").HasMaxLength(20);
            e.Property(x => x.Intent).HasColumnName("Intent");
            e.Property(x => x.Signals).HasColumnName("Signals").HasMaxLength(120);
            e.Property(x => x.Context).HasColumnName("Context").HasMaxLength(600);
            e.Property(x => x.Stage).HasColumnName("Stage").HasMaxLength(20);
            e.Property(x => x.Origin).HasColumnName("Origin").HasMaxLength(16);
            e.Property(x => x.SessionId).HasColumnName("SessionId").HasMaxLength(80);
            e.Property(x => x.DesiredProfile).HasColumnName("DesiredProfile").HasMaxLength(30);
            e.Property(x => x.CreatedAt).HasColumnName("CreatedAt").HasColumnType("datetime2");
            e.Property(x => x.LastModified).HasColumnName("LastModified").HasColumnType("datetime2");
        });

        modelBuilder.Entity<JobStateEntity>(e =>
        {
            e.ToTable("JobStates");
            e.HasKey(x => x.JobStateId);
            e.Property(x => x.JobStateId).HasColumnName("JobStateId").HasMaxLength(64);
            e.Property(x => x.LastRunAt).HasColumnName("LastRunAt").HasColumnType("datetime2");
            e.Property(x => x.Notes).HasColumnName("Notes").HasMaxLength(200);
        });

        modelBuilder.Entity<UserProjectionEntity>(e =>
        {
            e.ToTable("Users");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).HasColumnName("UserId");
            e.Property(x => x.Phone).HasColumnName("Phone").HasMaxLength(20);
            e.Property(x => x.Name).HasColumnName("Name").HasMaxLength(255);
        });
    }
}

/// <summary>Projeção de leitura de Users (UserId/Phone/Name) para o upsert pelo miner.</summary>
public class UserProjectionEntity
{
    public int UserId { get; set; }
    public string Phone { get; set; } = null!;
    public string Name { get; set; } = null!;
}

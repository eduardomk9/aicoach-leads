using Microsoft.EntityFrameworkCore;

namespace AiCoach.Leads.Infrastructure.Sql;

/// <summary>Entidade EF de LeadsSinal (não scaffoldada: mapping explícito — a coluna
/// física é "Id" (IDENTITY), a régua dos HasColumnName do scaffold).</summary>
public class LeadSinalEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }
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

/// <summary>Contexto SQL do job (AQUI SÓ as tabelas do mineiro; Users/Leads ficam na API).</summary>
public sealed class AiCoachLeadsDbContext(DbContextOptions<AiCoachLeadsDbContext> options) : DbContext(options)
{
    public DbSet<LeadSinalEntity> LeadsSinal => Set<LeadSinalEntity>();
    public DbSet<JobEstadoEntity> JobEstado => Set<JobEstadoEntity>();
    public DbSet<UserPhoneEntity> Users => Set<UserPhoneEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LeadSinalEntity>(e =>
        {
            // A coluna física é "Id" (IDENTITY) — sem HasColumnName o EF geraria "Id" certo,
            // MAS a régua pede explícito (o caso do Lead/Id da outra noite).
            e.Property(x => x.Id).HasColumnName("Id");
            e.ToTable("LeadsSinal");
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.SessionId).HasMaxLength(80);
            e.Property(x => x.Sinais).HasMaxLength(64);
            e.Property(x => x.Resumo).HasMaxLength(280);
            e.Property(x => x.Motivo).HasMaxLength(240);
            e.Property(x => x.ModeloUsado).HasMaxLength(64);
        });

        modelBuilder.Entity<JobEstadoEntity>(e =>
        {
            e.ToTable("JobEstado");
            e.HasKey(x => x.Chave);
            e.Property(x => x.Chave).HasColumnName("Chave").HasMaxLength(64);
            e.Property(x => x.UltimaExecucao).HasColumnType("datetime2");
            e.Property(x => x.Notas).HasMaxLength(200);
        });

        // Users em PROJEÇÃO LEITO (só Phone p/ resolver no INSERT...SELECT — o contexto
        // não governa a tabela: o DDL vive na API).
        modelBuilder.Entity<UserPhoneEntity>(e =>
        {
            e.ToTable("Users");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).HasColumnName("UserId");
            e.Property(x => x.Phone).HasColumnName("Phone").HasMaxLength(20);
        });
    }
}

/// <summary>Projeção de leitura mínima de Users (UserId + Phone) para o INSERT...SELECT.</summary>
public class UserPhoneEntity
{
    public int UserId { get; set; }
    public string Phone { get; set; } = null!;
}

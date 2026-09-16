using Microsoft.EntityFrameworkCore;
using SimuladorPix.Core.Dominio;
using SimuladorPix.Core.Portas;

namespace SimuladorPix.Infra;

public sealed class PixDbContext : DbContext, IUnidadeDeTrabalho
{
    public PixDbContext(DbContextOptions<PixDbContext> options) : base(options) { }

    public DbSet<Cobranca> Cobrancas => Set<Cobranca>();
    public DbSet<Pagamento> Pagamentos => Set<Pagamento>();
    public DbSet<Webhook> Webhooks => Set<Webhook>();
    public DbSet<EntregaWebhook> Entregas => Set<EntregaWebhook>();

    public Task ConfirmarAsync(CancellationToken ct = default) => SaveChangesAsync(ct);

    /// <summary>SQLite não compara DateTimeOffset em texto; guardamos como ticks UTC para filtrar e ordenar no banco.</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder cb)
    {
        cb.Properties<DateTimeOffset>().HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
        cb.Properties<decimal>().HaveConversion<double>();
    }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Cobranca>(e =>
        {
            e.ToTable("cobranca");
            e.HasKey(c => c.Txid);
            e.Property(c => c.Txid).HasMaxLength(35);
            e.Property(c => c.Valor).HasPrecision(18, 2);
            e.Property(c => c.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(c => c.SolicitacaoPagador).HasMaxLength(140);
            e.Property(c => c.Location).HasMaxLength(200);
            e.Property(c => c.EndToEndIdPagamento).HasMaxLength(32);
            e.OwnsOne(c => c.Chave, ch =>
            {
                ch.Property(x => x.Tipo).HasColumnName("chave_tipo").HasConversion<string>().HasMaxLength(16);
                ch.Property(x => x.Valor).HasColumnName("chave").HasMaxLength(77);
            });
            e.OwnsOne(c => c.Devedor, d =>
            {
                d.Property(x => x.Nome).HasColumnName("devedor_nome").HasMaxLength(200);
                d.Property(x => x.Documento).HasColumnName("devedor_documento").HasMaxLength(14);
            });
            e.HasIndex(c => c.Criacao);
            e.HasIndex(c => c.Status);
        });

        mb.Entity<Pagamento>(e =>
        {
            e.ToTable("pagamento");
            e.HasKey(p => p.EndToEndId);
            e.Property(p => p.EndToEndId).HasMaxLength(32);
            e.Property(p => p.Txid).HasMaxLength(35);
            e.Property(p => p.Valor).HasPrecision(18, 2);
            e.Property(p => p.InfoPagador).HasMaxLength(140);
            e.OwnsOne(p => p.Pagador, d =>
            {
                d.Property(x => x.Nome).HasColumnName("pagador_nome").HasMaxLength(200);
                d.Property(x => x.Documento).HasColumnName("pagador_documento").HasMaxLength(14);
            });
            e.OwnsMany(p => p.Devolucoes, d =>
            {
                d.ToTable("devolucao");
                d.WithOwner().HasForeignKey("end_to_end_id");
                d.HasKey("end_to_end_id", nameof(Devolucao.Id));
                d.Property(x => x.Id).HasMaxLength(35);
                d.Property(x => x.RtrId).HasMaxLength(32);
                d.Property(x => x.Valor).HasPrecision(18, 2);
                d.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
                d.Property(x => x.Motivo).HasMaxLength(200);
            });
            e.Navigation(p => p.Devolucoes).AutoInclude();
            e.HasIndex(p => p.Txid);
            e.HasIndex(p => p.Horario);
        });

        mb.Entity<Webhook>(e =>
        {
            e.ToTable("webhook");
            e.HasKey(w => w.Chave);
            e.Property(w => w.Chave).HasMaxLength(77);
            e.Property(w => w.Url).HasMaxLength(500);
            e.Property(w => w.Segredo).HasMaxLength(200);
        });

        mb.Entity<EntregaWebhook>(e =>
        {
            e.ToTable("entrega_webhook");
            e.HasKey(x => x.Id);
            e.Property(x => x.Chave).HasMaxLength(77);
            e.Property(x => x.Url).HasMaxLength(500);
            e.Property(x => x.Evento).HasMaxLength(30);
            e.Property(x => x.Assinatura).HasMaxLength(64);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.UltimoErro).HasMaxLength(500);
            e.HasIndex(x => new { x.Status, x.ProximaTentativa });
            e.HasIndex(x => x.Chave);
        });
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SimuladorPix.Core.Aplicacao;
using SimuladorPix.Core.Dominio;
using SimuladorPix.Core.Portas;

namespace SimuladorPix.Infra;

public static class Extensoes
{
    public const string ConexaoPadrao = "Data Source=dados/pix.db";

    /// <summary>
    /// Registra o banco SQLite, os repositórios e os serviços de aplicação. A configuração é lida no momento
    /// de resolver os serviços, e não no registro, para que fontes adicionadas depois (testes) sejam respeitadas.
    /// </summary>
    public static IServiceCollection AddSimuladorPix(this IServiceCollection services)
    {
        services.AddDbContext<PixDbContext>((sp, o) => o.UseSqlite(ConexaoDe(sp.GetRequiredService<IConfiguration>())));
        services.AddSingleton(sp => sp.GetRequiredService<IConfiguration>().GetSection(ParticipanteOptions.Secao).Get<ParticipanteOptions>() ?? new ParticipanteOptions());
        services.AddScoped<IUnidadeDeTrabalho>(sp => sp.GetRequiredService<PixDbContext>());
        services.AddScoped<ICobrancaRepositorio, CobrancaRepositorio>();
        services.AddScoped<IPagamentoRepositorio, PagamentoRepositorio>();
        services.AddScoped<IWebhookRepositorio, WebhookRepositorio>();
        services.AddScoped<IEntregaRepositorio, EntregaRepositorio>();
        services.AddSingleton<IRelogio, RelogioDoSistema>();
        services.AddScoped<CobrancaService>();
        services.AddScoped<PixService>();
        services.AddHttpClient<WebhookService>();
        return services;
    }

    public static string ConexaoDe(IConfiguration config) => config.GetConnectionString("Pix") ?? ConexaoPadrao;

    /// <summary>Cria a pasta do arquivo SQLite (se houver) e o esquema do banco.</summary>
    public static async Task CriarBancoAsync(this IServiceProvider sp, CancellationToken ct = default)
    {
        await using var escopo = sp.CreateAsyncScope();
        var conexao = ConexaoDe(escopo.ServiceProvider.GetRequiredService<IConfiguration>());
        var arquivo = conexao.Split(';').Select(p => p.Trim()).FirstOrDefault(p => p.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))?["Data Source=".Length..];
        if (arquivo is not null && arquivo != ":memory:" && Path.GetDirectoryName(arquivo) is { Length: > 0 } pasta) Directory.CreateDirectory(pasta);
        await escopo.ServiceProvider.GetRequiredService<PixDbContext>().Database.EnsureCreatedAsync(ct);
    }
}

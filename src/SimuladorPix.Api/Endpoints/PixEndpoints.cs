using SimuladorPix.Api.Contratos;
using SimuladorPix.Core.Aplicacao;
using SimuladorPix.Core.Dominio;

namespace SimuladorPix.Api.Endpoints;

public static class PixEndpoints
{
    public static IEndpointRouteBuilder MapPix(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/pix").WithTags("Pix recebidos e devoluções");

        g.MapGet("/{e2eid}", async (string e2eid, PixService svc, CancellationToken ct) => Results.Ok(PixResponse.De(await svc.ObterAsync(e2eid, ct))))
            .WithSummary("Consulta um Pix recebido pelo endToEndId");

        g.MapGet("/", async (DateTimeOffset inicio, DateTimeOffset fim, string? txid, int? pagina, int? tamanho, PixService svc, CancellationToken ct) =>
        {
            var lista = await svc.ListarAsync(inicio, fim, txid, pagina ?? 0, tamanho ?? 100, ct);
            return Results.Ok(new { parametros = new { inicio, fim, paginacao = new { paginaAtual = pagina ?? 0, itensPorPagina = tamanho ?? 100 } }, pix = lista.Select(PixResponse.De) });
        }).WithSummary("Lista Pix recebidos por período (?inicio&fim&txid&pagina&tamanho)");

        g.MapPut("/{e2eid}/devolucao/{id}", async (string e2eid, string id, DevolucaoRequest req, PixService svc, CancellationToken ct) =>
        {
            var dev = await svc.SolicitarDevolucaoAsync(e2eid, id, Conversoes.Valor(req.Valor), ct);
            return Results.Created($"/api/pix/{e2eid}/devolucao/{id}", DevolucaoResponse.De(dev));
        }).WithSummary("Solicita devolução total ou parcial (id escolhido pelo recebedor; idempotente)");

        g.MapGet("/{e2eid}/devolucao/{id}", async (string e2eid, string id, PixService svc, CancellationToken ct) =>
            Results.Ok(DevolucaoResponse.De(await svc.ObterDevolucaoAsync(e2eid, id, ct))))
            .WithSummary("Consulta uma devolução");

        var sim = app.MapGroup("/api/simulacao").WithTags("Simulação do pagador");

        sim.MapPost("/pagar/{txid}", async (string txid, PagarRequest req, PixService svc, CancellationToken ct) =>
        {
            var pagador = Pessoa.Criar(req.Pagador?.Nome, req.Pagador?.Cpf ?? req.Pagador?.Cnpj);
            var pag = await svc.PagarAsync(new PagarCobranca(txid, pagador, req.InfoPagador, req.Valor is null ? null : Conversoes.Valor(req.Valor)), ct);
            return Results.Created($"/api/pix/{pag.EndToEndId}", PixResponse.De(pag));
        }).WithSummary("Simula o pagamento de uma cobrança pelo cliente pagador (liquidação no SPI)");

        return app;
    }
}

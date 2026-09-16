using SimuladorPix.Api.Contratos;
using SimuladorPix.Core.Aplicacao;
using SimuladorPix.Core.Dominio;

namespace SimuladorPix.Api.Endpoints;

public static class CobrancaEndpoints
{
    public static IEndpointRouteBuilder MapCobrancas(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/cob").WithTags("Cobrança imediata (cob)");

        g.MapPut("/{txid}", async (string txid, CobrancaRequest req, CobrancaService svc, CancellationToken ct) =>
        {
            var (cob, criada) = await svc.CriarAsync(txid, Comando(req), ct);
            var corpo = CobrancaResponse.De(cob, svc.BrCodeDe(cob));
            return criada ? Results.Created($"/api/cob/{cob.Txid}", corpo) : Results.Ok(corpo);
        }).WithSummary("Cria uma cobrança com txid definido pelo recebedor (idempotente)");

        g.MapPost("/", async (CobrancaRequest req, CobrancaService svc, CancellationToken ct) =>
        {
            var (cob, _) = await svc.CriarComTxidGeradoAsync(Comando(req), ct);
            return Results.Created($"/api/cob/{cob.Txid}", CobrancaResponse.De(cob, svc.BrCodeDe(cob)));
        }).WithSummary("Cria uma cobrança com txid gerado pelo PSP");

        g.MapGet("/{txid}", async (string txid, CobrancaService svc, PixService pix, CancellationToken ct) =>
        {
            var cob = await svc.ObterAsync(txid, ct);
            Pagamento? pag = cob.EndToEndIdPagamento is null ? null : await pix.ObterAsync(cob.EndToEndIdPagamento, ct);
            return Results.Ok(CobrancaResponse.De(cob, svc.BrCodeDe(cob), pag));
        }).WithSummary("Consulta uma cobrança, com o Pix que a liquidou quando houver");

        g.MapGet("/", async (DateTimeOffset inicio, DateTimeOffset fim, string? chave, string? status, int? pagina, int? tamanho, CobrancaService svc, CancellationToken ct) =>
        {
            StatusCobranca? st = status?.ToUpperInvariant() switch
            {
                null => null,
                "ATIVA" => StatusCobranca.Ativa,
                "CONCLUIDA" => StatusCobranca.Concluida,
                "REMOVIDA_PELO_USUARIO_RECEBEDOR" => StatusCobranca.RemovidaPeloUsuarioRecebedor,
                "EXPIRADA" => StatusCobranca.Expirada,
                _ => throw new DominioException("status inválido"),
            };
            var lista = await svc.ListarAsync(inicio, fim, chave, st, pagina ?? 0, tamanho ?? 100, ct);
            return Results.Ok(new { parametros = new { inicio, fim, paginacao = new { paginaAtual = pagina ?? 0, itensPorPagina = tamanho ?? 100 } }, cobs = lista.Select(c => CobrancaResponse.De(c, null)) });
        }).WithSummary("Lista cobranças por período de criação (?inicio&fim&chave&status&pagina&tamanho)");

        g.MapPatch("/{txid}", async (string txid, RevisaoRequest req, CobrancaService svc, CancellationToken ct) =>
        {
            if (string.Equals(req.Status, "REMOVIDA_PELO_USUARIO_RECEBEDOR", StringComparison.OrdinalIgnoreCase))
                return Results.Ok(CobrancaResponse.De(await svc.RemoverAsync(txid, ct), null));
            if (req.Status is not null) throw new DominioException("o único status aceito no PATCH é REMOVIDA_PELO_USUARIO_RECEBEDOR");
            var cob = await svc.RevisarAsync(txid, new RevisarCobranca(req.Valor is null ? null : Conversoes.Valor(req.Valor.Original), req.Devedor?.ParaPessoa(), req.SolicitacaoPagador, req.Calendario?.Expiracao), ct);
            return Results.Ok(CobrancaResponse.De(cob, svc.BrCodeDe(cob)));
        }).WithSummary("Revisa (valor, devedor, solicitação, expiração) ou remove a cobrança");

        g.MapGet("/{txid}/qrcode", async (string txid, CobrancaService svc, CancellationToken ct) =>
        {
            var cob = await svc.ObterAsync(txid, ct);
            var payload = svc.BrCodeDe(cob);
            return Results.Ok(new { txid = cob.Txid, pixCopiaECola = payload, campos = BrCode.Ler(payload) });
        }).WithSummary("Payload EMV (copia e cola) do QR dinâmico, já decomposto em campos");

        return app;
    }

    private static CriarCobranca Comando(CobrancaRequest req) =>
        new(ChavePix.Parse(req.Chave), req.ValorDecimal(), req.Calendario?.Expiracao, req.Devedor?.ParaPessoa(), req.SolicitacaoPagador);
}

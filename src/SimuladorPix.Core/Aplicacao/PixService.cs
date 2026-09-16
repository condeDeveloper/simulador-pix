using System.Text.Json;
using System.Text.Json.Serialization;
using SimuladorPix.Core.Dominio;
using SimuladorPix.Core.Portas;

namespace SimuladorPix.Core.Aplicacao;

public sealed record PagarCobranca(string Txid, Pessoa Pagador, string? InfoPagador, decimal? Valor = null);

/// <summary>Pix recebidos: liquidação de cobranças (simulando o SPI) e devoluções, com notificação por webhook.</summary>
public sealed class PixService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ICobrancaRepositorio _cobrancas;
    private readonly IPagamentoRepositorio _pagamentos;
    private readonly IWebhookRepositorio _webhooks;
    private readonly IEntregaRepositorio _entregas;
    private readonly IUnidadeDeTrabalho _uow;
    private readonly IRelogio _relogio;
    private readonly ParticipanteOptions _participante;

    public PixService(ICobrancaRepositorio cobrancas, IPagamentoRepositorio pagamentos, IWebhookRepositorio webhooks, IEntregaRepositorio entregas,
        IUnidadeDeTrabalho uow, IRelogio relogio, ParticipanteOptions participante)
    {
        _cobrancas = cobrancas;
        _pagamentos = pagamentos;
        _webhooks = webhooks;
        _entregas = entregas;
        _uow = uow;
        _relogio = relogio;
        _participante = participante;
    }

    /// <summary>Simula o pagamento de uma cobrança pelo pagador: gera o endToEndId, conclui a cobrança e enfileira o webhook "pix".</summary>
    public async Task<Pagamento> PagarAsync(PagarCobranca cmd, CancellationToken ct = default)
    {
        var agora = _relogio.Agora;
        var cob = await _cobrancas.ObterAsync(cmd.Txid, ct) ?? throw new NaoEncontradoException("cobrança", cmd.Txid);
        var pagamento = new Pagamento(EndToEndId.Gerar(_participante.Ispb, agora), cob.Txid, cmd.Valor ?? cob.Valor, agora, cmd.Pagador, cmd.InfoPagador);
        cob.Concluir(pagamento, agora);
        await _pagamentos.AdicionarAsync(pagamento, ct);
        await EnfileirarAsync(cob.Chave.Valor, "pix", new
        {
            pix = new[] { new { endToEndId = pagamento.EndToEndId, txid = pagamento.Txid, valor = pagamento.Valor.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                chave = cob.Chave.Valor, horario = pagamento.Horario, infoPagador = pagamento.InfoPagador, pagador = new { nome = pagamento.Pagador.Nome, documento = pagamento.Pagador.Documento } } },
        }, agora, ct);
        await _uow.ConfirmarAsync(ct);
        return pagamento;
    }

    public async Task<Pagamento> ObterAsync(string endToEndId, CancellationToken ct = default) =>
        await _pagamentos.ObterAsync(endToEndId, ct) ?? throw new NaoEncontradoException("pix", endToEndId);

    public Task<IReadOnlyList<Pagamento>> ListarAsync(DateTimeOffset inicio, DateTimeOffset fim, string? txid, int pagina, int tamanho, CancellationToken ct = default)
    {
        if (fim < inicio) throw new DominioException("fim anterior ao início");
        return _pagamentos.ListarAsync(inicio, fim, txid, Math.Max(0, pagina), Math.Clamp(tamanho, 1, 1000), ct);
    }

    /// <summary>PUT /pix/{e2eid}/devolucao/{id}: solicita devolução; fica em processamento até o worker liquidar.</summary>
    public async Task<Devolucao> SolicitarDevolucaoAsync(string endToEndId, string id, decimal valor, CancellationToken ct = default)
    {
        var agora = _relogio.Agora;
        var pag = await ObterAsync(endToEndId, ct);
        var dev = pag.SolicitarDevolucao(id, valor, EndToEndId.GerarDevolucao(_participante.Ispb, agora), agora);
        await _uow.ConfirmarAsync(ct);
        return dev;
    }

    public async Task<Devolucao> ObterDevolucaoAsync(string endToEndId, string id, CancellationToken ct = default) => (await ObterAsync(endToEndId, ct)).ObterDevolucao(id);

    /// <summary>Worker: liquida devoluções em processamento e notifica o webhook "devolucao".</summary>
    public async Task<int> LiquidarDevolucoesPendentesAsync(int limite = 200, CancellationToken ct = default)
    {
        var agora = _relogio.Agora;
        var lista = await _pagamentos.ComDevolucoesEmProcessamentoAsync(limite, ct);
        var n = 0;
        foreach (var pag in lista)
        {
            var cob = await _cobrancas.ObterAsync(pag.Txid, ct);
            foreach (var dev in pag.Devolucoes.Where(d => d.Status == StatusDevolucao.EmProcessamento).ToList())
            {
                pag.LiquidarDevolucao(dev.Id, agora);
                n++;
                if (cob is not null)
                {
                    await EnfileirarAsync(cob.Chave.Valor, "devolucao", new
                    {
                        pix = new[] { new { endToEndId = pag.EndToEndId, txid = pag.Txid, valor = pag.Valor.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                            devolucoes = new[] { new { id = dev.Id, rtrId = dev.RtrId, valor = dev.Valor.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), status = "DEVOLVIDO", horario = new { solicitacao = dev.Solicitacao, liquidacao = agora } } } } },
                    }, agora, ct);
                }
            }
        }
        if (n > 0) await _uow.ConfirmarAsync(ct);
        return n;
    }

    private async Task EnfileirarAsync(string chave, string evento, object corpo, DateTimeOffset agora, CancellationToken ct)
    {
        var wh = await _webhooks.ObterAsync(chave, ct);
        if (wh is null) return;
        var json = JsonSerializer.Serialize(corpo, Json);
        await _entregas.AdicionarAsync(new EntregaWebhook(wh, evento, json, agora), ct);
    }
}

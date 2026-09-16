# Simulador Pix

[![CI](https://github.com/condeDeveloper/simulador-pix/actions/workflows/ci.yml/badge.svg)](https://github.com/condeDeveloper/simulador-pix/actions/workflows/ci.yml)

Simulador de um PSP recebedor Pix em C# e .NET 8, com a API no formato da API Pix do Banco Central: cobranças imediatas com QR dinâmico, pagamentos com endToEndId, devoluções e webhooks assinados. Serve para desenvolver e testar integrações Pix sem depender de homologação em banco.

O que ele implementa de verdade, e não de fachada:

- **BR Code EMV** (o "copia e cola" do QR): montagem TLV completa, campo 26 com GUI `br.gov.bcb.pix`, CRC-16/CCITT-FALSE no campo 63, leitura e validação do payload. O QR estático gerado é idêntico ao exemplo do manual do BCB.
- **Chaves Pix**: CPF e CNPJ validados pelos dígitos verificadores, e-mail, telefone `+55` e chave aleatória (UUID), sempre normalizadas.
- **Cobrança (cob)**: `PUT` idempotente por txid, `POST` com txid gerado, revisão com incremento de revisão, remoção, expiração automática e conclusão por um único pagamento com o valor exato.
- **Pix recebido**: endToEndId no formato do SPI (`E` + ISPB + data-hora + 11 caracteres), consulta por período.
- **Devoluções**: total ou parcial, idempotente por id, saldo devolvível controlado, prazo de 90 dias, liquidação assíncrona por worker.
- **Webhooks**: por chave, exigem https (http só em localhost), corpo assinado com HMAC-SHA256 no header `x-pix-assinatura`, reentrega com backoff exponencial (5 s, 10 s, 20 s ... até 1 h, 8 tentativas), histórico de entregas.
- Persistência com EF Core e SQLite; workers em segundo plano para expirar, liquidar devoluções e entregar webhooks.

## Rodar

```bash
dotnet run --project src/SimuladorPix.Api
```

- Documentação interativa: http://localhost:5000/docs (clique em Authorize e informe a api key)
- Api key padrão: `troque-esta-chave` (header `x-api-key`), configurável em `Seguranca:ApiKey`
- Banco em `dados/pix.db`, criado automaticamente

```bash
K='x-api-key: troque-esta-chave'
TXID=$(openssl rand -hex 16)

# 1. cria uma cobrança de R$ 150,00 com QR dinâmico
curl -s -X PUT localhost:5000/api/cob/$TXID -H "$K" -H 'Content-Type: application/json' \
  -d '{"calendario":{"expiracao":3600},"devedor":{"cpf":"52998224725","nome":"Fulano de Tal"},"valor":{"original":"150.00"},"chave":"loja@exemplo.com","solicitacaoPagador":"Pedido 42"}'

# 2. configura um webhook apontando para o receptor de demonstração
curl -s -X PUT localhost:5000/api/webhook/loja@exemplo.com -H "$K" -H 'Content-Type: application/json' \
  -d '{"webhookUrl":"http://localhost:5000/api/simulacao/receptor","segredo":"meu-segredo"}'

# 3. simula o cliente pagando
curl -s -X POST localhost:5000/api/simulacao/pagar/$TXID -H "$K" -H 'Content-Type: application/json' \
  -d '{"pagador":{"nome":"Fulano de Tal","cpf":"52998224725"},"infoPagador":"obrigado"}'

# 4. em até 2 s o worker entrega o webhook; veja o que chegou e confira a assinatura
curl -s localhost:5000/api/simulacao/receptor
```

## Testes

```bash
dotnet test
```

Cobrem CRC16 com vetor conhecido, BR Code contra o exemplo oficial, todos os tipos de chave, o ciclo de vida da cobrança, devoluções com saldo e idempotência, assinatura HMAC e agenda de reentrega, o worker de entrega com HttpClient falso e SQLite em memória, e a API de ponta a ponta com `WebApplicationFactory`.

## Arquitetura

```
src/SimuladorPix.Core      # domínio, portas e casos de uso, sem dependência de framework
  Dominio/                 # ChavePix, BrCode, Crc16, Cobranca, Pagamento, Devolucao, Webhook, EntregaWebhook
  Aplicacao/               # CobrancaService, PixService, WebhookService
  Portas/                  # interfaces de repositório e unidade de trabalho
src/SimuladorPix.Infra     # EF Core + SQLite: DbContext, mapeamentos e repositórios
src/SimuladorPix.Api       # minimal API, api key, Swagger, workers, receptor de demonstração
tests/SimuladorPix.Tests   # xUnit + FluentAssertions
```

Valores monetários trafegam como string `"0.00"` na API, como no padrão do BCB, e são `decimal` por dentro. Estados das cobranças e devoluções seguem os nomes da API Pix (`ATIVA`, `CONCLUIDA`, `REMOVIDA_PELO_USUARIO_RECEBEDOR`, `EXPIRADA`, `EM_PROCESSAMENTO`, `DEVOLVIDO`).

## Licença

MIT

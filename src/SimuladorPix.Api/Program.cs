using System.Text.Json.Serialization;
using Microsoft.OpenApi.Models;
using SimuladorPix.Api.Endpoints;
using SimuladorPix.Api.Seguranca;
using SimuladorPix.Api.Workers;
using SimuladorPix.Core.Dominio;
using SimuladorPix.Infra;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSimuladorPix();
builder.Services.AddHostedService<ProcessadorPeriodico>();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Simulador Pix",
        Version = "v1",
        Description = "Simulador de PSP recebedor Pix: cobranças imediatas com QR dinâmico (BR Code EMV com CRC16), pagamentos com endToEndId, "
                      + "devoluções e webhooks assinados com HMAC-SHA256 e reentrega com backoff. Envie o header x-api-key nas rotas /api.",
    });
    o.AddSecurityDefinition("apiKey", new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header, Name = ApiKeyMiddleware.Header });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement { [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "apiKey" } }] = Array.Empty<string>() });
});

var app = builder.Build();
await app.Services.CriarBancoAsync();

app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (NaoEncontradoException e) { await Problema(ctx, 404, e.Message); }
    catch (ConflitoException e) { await Problema(ctx, 409, e.Message); }
    catch (DominioException e) { await Problema(ctx, 422, e.Message); }
    catch (BadHttpRequestException e) { await Problema(ctx, 400, e.Message); }
});
app.UseMiddleware<ApiKeyMiddleware>();
app.UseSwagger();
app.UseSwaggerUI(o => { o.RoutePrefix = "docs"; o.DocumentTitle = "Simulador Pix"; });

app.MapCobrancas();
app.MapPix();
app.MapWebhooks();
app.MapGet("/saude", () => Results.Ok(new { status = "ok", agora = DateTimeOffset.UtcNow }));

app.Run();

static async Task Problema(HttpContext ctx, int status, string detalhe)
{
    if (ctx.Response.HasStarted) return;
    ctx.Response.StatusCode = status;
    await ctx.Response.WriteAsJsonAsync(new { title = detalhe, status });
}

public partial class Program { }

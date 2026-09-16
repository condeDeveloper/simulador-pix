namespace SimuladorPix.Api.Seguranca;

/// <summary>Exige o header x-api-key nas rotas /api, exceto documentação, saúde e o receptor de webhooks de demonstração.</summary>
public sealed class ApiKeyMiddleware
{
    public const string Header = "x-api-key";
    private readonly RequestDelegate _next;
    private readonly string _chave;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _chave = config["Seguranca:ApiKey"] ?? throw new InvalidOperationException("Seguranca:ApiKey não configurada");
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var caminho = ctx.Request.Path;
        var protegido = caminho.StartsWithSegments("/api") && !caminho.StartsWithSegments("/api/simulacao/receptor");
        if (protegido && !string.Equals(ctx.Request.Headers[Header], _chave, StringComparison.Ordinal))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsJsonAsync(new { title = "informe o header x-api-key", status = 401 });
            return;
        }
        await _next(ctx);
    }
}

using Webhook.Api;

namespace Tests.Webhook;

public class ReceberMetricaRequestValidatorTests
{
    private readonly ReceberMetricaRequestValidator _validator = new();

    private static ReceberMetricaRequest RequestValido() => new()
    {
        Project = "checkout-service",
        Provider = "openai",
        Model = "gpt-4o-mini",
        PromptTokens = 100,
        CompletionTokens = 50,
        TotalTokens = 150,
        CostUsd = 0.0001m,
        LatencyMs = 320,
        Status = "success",
        Timestamp = DateTime.UtcNow,
    };

    [Fact]
    public void PayloadValido_Passa()
    {
        Assert.True(_validator.Validate(RequestValido()).IsValid);
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("OpenAI")]
    [InlineData("anthropic")]
    public void ProvedorConhecido_IndependenteDeCaixa_Passa(string provider)
    {
        var request = RequestValido();
        request.Provider = provider;

        Assert.True(_validator.Validate(request).IsValid);
    }

    [Theory]
    [InlineData("google")]
    [InlineData("")]
    public void ProvedorDesconhecidoOuVazio_Falha(string provider)
    {
        var request = RequestValido();
        request.Provider = provider;

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ReceberMetricaRequest.Provider));
    }

    [Theory]
    [InlineData("success")]
    [InlineData("error")]
    [InlineData("ERROR")]
    public void StatusValido_Passa(string status)
    {
        var request = RequestValido();
        request.Status = status;

        Assert.True(_validator.Validate(request).IsValid);
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("")]
    public void StatusInvalido_Falha(string status)
    {
        var request = RequestValido();
        request.Status = status;

        Assert.False(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void ProjetoVazio_Falha()
    {
        var request = RequestValido();
        request.Project = "";

        Assert.False(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void ModeloVazio_Falha()
    {
        var request = RequestValido();
        request.Model = "";

        Assert.False(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void TokensNegativos_Falham()
    {
        foreach (var mutate in new Action<ReceberMetricaRequest>[]
                 {
                     r => r.PromptTokens = -1,
                     r => r.CompletionTokens = -1,
                     r => r.TotalTokens = -1,
                     r => r.LatencyMs = -1,
                 })
        {
            var request = RequestValido();
            mutate(request);
            Assert.False(_validator.Validate(request).IsValid);
        }
    }

    [Fact]
    public void CustoNegativo_Falha()
    {
        var request = RequestValido();
        request.CostUsd = -0.01m;

        Assert.False(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void CustoNulo_Passa_PorqueSignificaPrecoDesconhecido()
    {
        var request = RequestValido();
        request.CostUsd = null;

        Assert.True(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void ZeroEmTudo_Passa()
    {
        var request = RequestValido();
        request.PromptTokens = 0;
        request.CompletionTokens = 0;
        request.TotalTokens = 0;
        request.CostUsd = 0;
        request.LatencyMs = 0;

        Assert.True(_validator.Validate(request).IsValid);
    }

    // JSON com "provider": null / "status": null chega ao validator como null (o default "" do
    // DTO so vale quando o campo esta ausente). Validacao deve reprovar, nao lancar excecao (500).
    [Fact]
    public void ProvedorNulo_ReprovaSemLancarExcecao()
    {
        var request = RequestValido();
        request.Provider = null!;

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void StatusNulo_ReprovaSemLancarExcecao()
    {
        var request = RequestValido();
        request.Status = null!;

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
    }
}

using FluentValidation;

namespace Webhook.Api;

public class ReceberMetricaRequestValidator : AbstractValidator<ReceberMetricaRequest>
{
    private static readonly string[] ProvedoresConhecidos = ["openai", "anthropic"];
    private static readonly string[] StatusValidos = ["success", "error"];

    public ReceberMetricaRequestValidator()
    {
        RuleFor(x => x.Project).NotEmpty();

        RuleFor(x => x.Provider)
            .NotEmpty()
            .Must(provider => ProvedoresConhecidos.Contains(provider.ToLowerInvariant()))
            .WithMessage("Provedor desconhecido.");

        RuleFor(x => x.Model).NotEmpty();
        RuleFor(x => x.PromptTokens).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CompletionTokens).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TotalTokens).GreaterThanOrEqualTo(0);

        RuleFor(x => x.CostUsd)
            .GreaterThanOrEqualTo(0)
            .When(x => x.CostUsd.HasValue);

        RuleFor(x => x.LatencyMs).GreaterThanOrEqualTo(0);

        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(status => StatusValidos.Contains(status.ToLowerInvariant()))
            .WithMessage("Status deve ser 'success' ou 'error'.");
    }
}

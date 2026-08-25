using FluentValidation;
using Webhook.Application.ReceberMetrica;

namespace Webhook.Api.Endpoints;

public static class UsageEventsEndpoint
{
    public static void MapUsageEventsEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/events", HandleAsync)
            .RequireRateLimiting("PerIp");
    }

    private static async Task<IResult> HandleAsync(
        ReceberMetricaRequest request,
        HttpRequest httpRequest,
        IValidator<ReceberMetricaRequest> validator,
        ReceberMetricaUseCase useCase,
        CancellationToken cancellationToken)
    {
        if (!httpRequest.Headers.TryGetValue("X-Api-Key", out var apiKeyHeader) || string.IsNullOrWhiteSpace(apiKeyHeader))
        {
            return Results.Unauthorized();
        }

        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Results.ValidationProblem(validationResult.ToDictionary());
        }

        var command = new ReceberMetricaCommand(
            ApiKeyRaw: apiKeyHeader.ToString(),
            Provider: request.Provider,
            Model: request.Model,
            PromptTokens: request.PromptTokens,
            CompletionTokens: request.CompletionTokens,
            TotalTokens: request.TotalTokens,
            CostUsd: request.CostUsd,
            LatencyMs: request.LatencyMs,
            Status: request.Status,
            Timestamp: request.Timestamp);

        var result = await useCase.ExecutarAsync(command, cancellationToken);

        return result.Status switch
        {
            ReceberMetricaStatus.Aceito => Results.Accepted(),
            ReceberMetricaStatus.ApiKeyInvalida => Results.Unauthorized(),
            ReceberMetricaStatus.FilaCheia => Results.StatusCode(StatusCodes.Status503ServiceUnavailable),
            _ => Results.Problem(),
        };
    }
}

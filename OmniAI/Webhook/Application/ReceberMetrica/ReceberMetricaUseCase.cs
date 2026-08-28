using Microsoft.Extensions.Options;
using Shared.Messaging;
using Shared.Security;
using Webhook.Domain.Abstractions;

namespace Webhook.Application.ReceberMetrica;

public class ReceberMetricaUseCase
{
    private readonly IApiKeyRepository _apiKeyRepository;
    private readonly IUsageEventPublisher _publisher;
    private readonly ApiKeyHashingOptions _hashingOptions;

    public ReceberMetricaUseCase(
        IApiKeyRepository apiKeyRepository,
        IUsageEventPublisher publisher,
        IOptions<ApiKeyHashingOptions> hashingOptions)
    {
        _apiKeyRepository = apiKeyRepository;
        _publisher = publisher;
        _hashingOptions = hashingOptions.Value;
    }

    public async Task<ReceberMetricaResult> ExecutarAsync(ReceberMetricaCommand command, CancellationToken cancellationToken)
    {
        var keyHash = ApiKeyHasher.Hash(command.ApiKeyRaw, _hashingOptions.PepperSecret);

        var apiKey = await _apiKeyRepository.FindActiveByHashAsync(keyHash, cancellationToken);
        if (apiKey is null)
        {
            return new ReceberMetricaResult(ReceberMetricaStatus.ApiKeyInvalida);
        }

        var message = new UsageEventMessage
        {
            Project = apiKey.Project.Name,
            Provider = command.Provider,
            Model = command.Model,
            PromptTokens = command.PromptTokens,
            CompletionTokens = command.CompletionTokens,
            TotalTokens = command.TotalTokens,
            CostUsd = command.CostUsd,
            LatencyMs = command.LatencyMs,
            Status = command.Status,
            Timestamp = command.Timestamp,
        };

        var outcome = await _publisher.PublishAsync(message, cancellationToken);

        return outcome switch
        {
            PublishOutcome.Published => new ReceberMetricaResult(ReceberMetricaStatus.Aceito),
            PublishOutcome.QueueFull => new ReceberMetricaResult(ReceberMetricaStatus.FilaCheia),
            _ => throw new InvalidOperationException($"Publish outcome desconhecido: {outcome}"),
        };
    }
}

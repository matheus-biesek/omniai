using System.Text.Json;
using Consumer.Domain.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Entities;
using Shared.Messaging;

namespace Consumer.Application.ProcessarEventoDeUso;

public class ProcessarEventoDeUsoUseCase
{
    private readonly IProjectLookup _projectLookup;
    private readonly IUsageRecordRepository _usageRecordRepository;
    private readonly IUsageEventLogRepository _eventLogRepository;
    private readonly IUsageNotifier _notifier;
    private readonly RetryOptions _retryOptions;
    private readonly ILogger<ProcessarEventoDeUsoUseCase> _logger;

    public ProcessarEventoDeUsoUseCase(
        IProjectLookup projectLookup,
        IUsageRecordRepository usageRecordRepository,
        IUsageEventLogRepository eventLogRepository,
        IUsageNotifier notifier,
        IOptions<RetryOptions> retryOptions,
        ILogger<ProcessarEventoDeUsoUseCase> logger)
    {
        _projectLookup = projectLookup;
        _usageRecordRepository = usageRecordRepository;
        _eventLogRepository = eventLogRepository;
        _notifier = notifier;
        _retryOptions = retryOptions.Value;
        _logger = logger;
    }

    public async Task ExecutarAsync(UsageEventLog log, CancellationToken cancellationToken)
    {
        try
        {
            if (await _usageRecordRepository.ExistsForEventLogAsync(log.Id, cancellationToken))
            {
                await _eventLogRepository.MarkProcessedAsync(log.Id, cancellationToken);
                return;
            }

            var message = JsonSerializer.Deserialize<UsageEventMessage>(log.Payload)
                ?? throw new FalhaPermanenteException("Payload nao pode ser desserializado.");

            var projectId = await _projectLookup.FindProjectIdByNameAsync(message.Project, cancellationToken)
                ?? throw new FalhaPermanenteException($"Projeto '{message.Project}' nao encontrado.");

            var record = new UsageRecord
            {
                ProjectId = projectId,
                SourceEventLogId = log.Id,
                Provider = message.Provider,
                Model = message.Model,
                PromptTokens = message.PromptTokens,
                CompletionTokens = message.CompletionTokens,
                TotalTokens = message.TotalTokens,
                CostUsd = message.CostUsd,
                LatencyMs = message.LatencyMs,
                Status = message.Status,
                OccurredAt = message.Timestamp,
            };

            var saved = await _usageRecordRepository.AddAsync(record, cancellationToken);
            await _eventLogRepository.MarkProcessedAsync(log.Id, cancellationToken);

            try
            {
                await _notifier.NotifyAsync(saved, message.Project, cancellationToken);
            }
            catch (Exception notifyEx)
            {
                _logger.LogWarning(notifyEx, "Falha ao notificar o dashboard sobre o evento {EventLogId} - dado ja persistido, seguindo.", log.Id);
            }
        }
        catch (FalhaPermanenteException ex)
        {
            _logger.LogError(ex, "Falha permanente processando evento {EventLogId}, nao sera reprocessado.", log.Id);
            await _eventLogRepository.MarkPermanentFailureAsync(log.Id, ex.Message, cancellationToken);
        }
        catch (Exception ex)
        {
            var nextAttempt = log.AttemptCount + 1;
            if (nextAttempt >= _retryOptions.MaxAttempts)
            {
                _logger.LogError(ex, "Evento {EventLogId} esgotou tentativas ({Attempts}), marcando falha permanente.", log.Id, nextAttempt);
                await _eventLogRepository.MarkPermanentFailureAsync(log.Id, ex.Message, cancellationToken);
                return;
            }

            var delay = TimeSpan.FromSeconds(_retryOptions.BaseDelaySeconds * Math.Pow(2, nextAttempt - 1));
            _logger.LogWarning(ex, "Falha transiente processando evento {EventLogId}, tentativa {Attempt}, novo retry em {Delay}.", log.Id, nextAttempt, delay);
            await _eventLogRepository.MarkTransientFailureAsync(log.Id, nextAttempt, ex.Message, DateTime.UtcNow.Add(delay), cancellationToken);
        }
    }
}

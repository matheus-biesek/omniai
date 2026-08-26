using Consumer.Application.ProcessarEventoDeUso;
using Consumer.Application.RegistrarEvento;
using Consumer.Domain.Abstractions;
using Microsoft.Extensions.Options;

namespace Consumer.Workers;

public class UsageEventConsumerWorker : BackgroundService
{
    private readonly IUsageEventReader _reader;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkerOptions _options;
    private readonly ILogger<UsageEventConsumerWorker> _logger;

    public UsageEventConsumerWorker(
        IUsageEventReader reader,
        IServiceScopeFactory scopeFactory,
        IOptions<WorkerOptions> options,
        ILogger<UsageEventConsumerWorker> logger)
    {
        _reader = reader;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _reader.EnsureConsumerGroupAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RegistrarNovosEventosAsync(stoppingToken);
            await ProcessarEventosPendentesAsync(stoppingToken);

            await Task.Delay(_options.PollIntervalMs, stoppingToken);
        }
    }

    private async Task RegistrarNovosEventosAsync(CancellationToken cancellationToken)
    {
        var pendentesDoProcesso = await _reader.ReadOwnPendingAsync(cancellationToken);
        var novos = await _reader.ReadNewAsync(cancellationToken);

        var entradas = pendentesDoProcesso.Concat(novos).ToList();
        if (entradas.Count == 0)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var useCase = scope.ServiceProvider.GetRequiredService<RegistrarEventoUseCase>();

        foreach (var entrada in entradas)
        {
            try
            {
                await useCase.ExecutarAsync(entrada.EntryId, entrada.Payload, cancellationToken);
                await _reader.AcknowledgeAsync(entrada.EntryId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao registrar evento {EntryId}, permanece pendente no Redis.", entrada.EntryId);
            }
        }
    }

    private async Task ProcessarEventosPendentesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var eventLogRepository = scope.ServiceProvider.GetRequiredService<IUsageEventLogRepository>();
        var useCase = scope.ServiceProvider.GetRequiredService<ProcessarEventoDeUsoUseCase>();

        var pendentes = await eventLogRepository.GetDueForProcessingAsync(maxBatchSize: 50, cancellationToken);

        foreach (var log in pendentes)
        {
            try
            {
                await useCase.ExecutarAsync(log, cancellationToken);
            }
            catch (Exception ex)
            {
                // Rede de seguranca: o Use Case ja trata falha transiente/permanente internamente.
                // Se algo ainda assim escapar (bug, exception ao tentar marcar a falha), isso nao
                // pode derrubar o worker inteiro - um evento problematico nao pode parar todos os outros.
                _logger.LogCritical(ex, "Falha inesperada processando evento {EventLogId}, worker continua.", log.Id);
            }
        }
    }
}

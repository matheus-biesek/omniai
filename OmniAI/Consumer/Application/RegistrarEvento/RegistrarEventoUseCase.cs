using Consumer.Domain.Abstractions;
using Shared.Entities;

namespace Consumer.Application.RegistrarEvento;

public class RegistrarEventoUseCase
{
    private readonly IUsageEventLogRepository _repository;

    public RegistrarEventoUseCase(IUsageEventLogRepository repository)
    {
        _repository = repository;
    }

    /// <returns>
    /// true se o evento foi registrado agora; false se essa entrada do Redis ja estava registrada
    /// (releitura depois de um XACK que falhou) - nos dois casos a entrada pode ser confirmada.
    /// </returns>
    public Task<bool> ExecutarAsync(string redisEntryId, string payload, CancellationToken cancellationToken)
    {
        var log = new UsageEventLog
        {
            Id = Guid.NewGuid(),
            RedisEntryId = redisEntryId,
            Payload = payload,
            Status = UsageEventLogStatus.Pendente,
            AttemptCount = 0,
            ReceivedAt = DateTime.UtcNow,
        };

        return _repository.TryAddAsync(log, cancellationToken);
    }
}

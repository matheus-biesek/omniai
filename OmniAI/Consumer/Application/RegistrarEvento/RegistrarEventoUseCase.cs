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

    public async Task ExecutarAsync(string redisEntryId, string payload, CancellationToken cancellationToken)
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

        await _repository.AddAsync(log, cancellationToken);
    }
}

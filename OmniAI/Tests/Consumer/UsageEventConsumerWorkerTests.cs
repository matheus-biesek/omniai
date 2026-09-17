using Consumer.Application.ProcessarEventoDeUso;
using Consumer.Application.RegistrarEvento;
using Consumer.Domain.Abstractions;
using Consumer.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Entities;

namespace Tests.Consumer;

public class UsageEventConsumerWorkerTests
{
    private sealed class FakeReader : IUsageEventReader
    {
        private readonly object _lock = new();
        private readonly List<PendingRedisEntry> _novos;
        public HashSet<string> Acked { get; } = new();
        public int ReadNewCalls { get; private set; }
        public bool ThrowOnReadNew { get; set; }

        public FakeReader(IEnumerable<PendingRedisEntry> novos) => _novos = novos.ToList();

        public Task EnsureConsumerGroupAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        // Simula o comportamento de XREADGROUP com "0": tudo que ja foi entregue e nao foi confirmado.
        private readonly List<PendingRedisEntry> _entregues = new();

        public Task<IReadOnlyList<PendingRedisEntry>> ReadOwnPendingAsync(CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                return Task.FromResult<IReadOnlyList<PendingRedisEntry>>(
                    _entregues.Where(e => !Acked.Contains(e.EntryId)).ToList());
            }
        }

        public Task<IReadOnlyList<PendingRedisEntry>> ReadNewAsync(CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                ReadNewCalls++;
                if (ThrowOnReadNew && ReadNewCalls == 1)
                {
                    throw new TimeoutException("redis caiu");
                }

                var batch = _novos.ToList();
                _novos.Clear();
                _entregues.AddRange(batch);
                return Task.FromResult<IReadOnlyList<PendingRedisEntry>>(batch);
            }
        }

        public HashSet<string> FailAckOnceFor { get; } = new();
        public int RemoveAcknowledgedCalls { get; private set; }
        public bool ThrowOnRemoveAcknowledged { get; set; }

        public Task AcknowledgeAsync(string entryId, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                if (FailAckOnceFor.Remove(entryId))
                {
                    throw new RedisConnectionExceptionSimulada();
                }

                Acked.Add(entryId);
            }

            return Task.CompletedTask;
        }

        public Task RemoveAcknowledgedAsync(CancellationToken cancellationToken)
        {
            RemoveAcknowledgedCalls++;
            return ThrowOnRemoveAcknowledged
                ? Task.FromException(new RedisConnectionExceptionSimulada())
                : Task.CompletedTask;
        }
    }

    private sealed class RedisConnectionExceptionSimulada() : Exception("redis caiu no XACK");

    private static async Task RodarWorkerPor(FakeReader reader, FakeUsageEventLogRepository logs, TimeSpan duracao)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IUsageEventLogRepository>(logs);
        services.AddSingleton<IProjectLookup>(new FakeProjectLookup());
        services.AddSingleton<IUsageRecordRepository>(new FakeUsageRecordRepository());
        services.AddSingleton<IUsageNotifier>(new FakeNotifier());
        services.AddSingleton<IOptions<RetryOptions>>(Options.Create(new RetryOptions()));
        services.AddScoped<RegistrarEventoUseCase>();
        services.AddScoped<ProcessarEventoDeUsoUseCase>();
        var provider = services.BuildServiceProvider();

        var worker = new UsageEventConsumerWorker(
            reader,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new WorkerOptions { PollIntervalMs = 20 }),
            NullLogger<UsageEventConsumerWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(duracao);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RegistraCadaEntradaComoPendente_EConfirmaNoRedisSoDepois()
    {
        var reader = new FakeReader(new[]
        {
            new PendingRedisEntry("1-0", "{\"a\":1}"),
            new PendingRedisEntry("2-0", "{\"a\":2}"),
        });
        var logs = new FakeUsageEventLogRepository();

        await RodarWorkerPor(reader, logs, TimeSpan.FromMilliseconds(300));

        Assert.Equal(new[] { "1-0", "2-0" }, logs.Added.Select(l => l.RedisEntryId));
        Assert.All(logs.Added, l => Assert.Equal(UsageEventLogStatus.Pendente, l.Status));
        Assert.Equal(new[] { "{\"a\":1}", "{\"a\":2}" }, logs.Added.Select(l => l.Payload));
        Assert.Equal(new HashSet<string> { "1-0", "2-0" }, reader.Acked);
    }

    [Fact]
    public async Task FalhaAoRegistrarUmaEntrada_NaoConfirma_NemBloqueiaAsOutras_ERetentaDepois()
    {
        var reader = new FakeReader(new[]
        {
            new PendingRedisEntry("1-0", "p1"),
            new PendingRedisEntry("2-0", "p2"),
            new PendingRedisEntry("3-0", "p3"),
        });
        var falhas = 0;
        var logs = new FakeUsageEventLogRepository
        {
            // "2-0" falha so na primeira vez (ex: Postgres oscilou)
            FailAddWhen = l => l.RedisEntryId == "2-0" && falhas++ == 0,
        };

        await RodarWorkerPor(reader, logs, TimeSpan.FromMilliseconds(400));

        Assert.Equal(new HashSet<string> { "1-0", "2-0", "3-0" }, reader.Acked);
        Assert.Equal(1, logs.Added.Count(l => l.RedisEntryId == "2-0"));
        Assert.Equal(3, logs.Added.Count);
    }

    [Fact]
    public async Task AckFalhaDepoisDoLogGravado_EntradaRelida_NaoDuplicaLog_EEhConfirmadaDepois()
    {
        var reader = new FakeReader(new[]
        {
            new PendingRedisEntry("1-0", "p1"),
            new PendingRedisEntry("2-0", "p2"),
        });
        reader.FailAckOnceFor.Add("1-0");
        var logs = new FakeUsageEventLogRepository();

        await RodarWorkerPor(reader, logs, TimeSpan.FromMilliseconds(400));

        Assert.Equal(1, logs.Added.Count(l => l.RedisEntryId == "1-0"));
        Assert.Equal(2, logs.Added.Count);
        Assert.Equal(new HashSet<string> { "1-0", "2-0" }, reader.Acked);
    }

    [Fact]
    public async Task AoIniciar_RemoveEntradasJaConfirmadasDaStream_EFalhaNissoNaoDerrubaOWorker()
    {
        var reader = new FakeReader(new[] { new PendingRedisEntry("1-0", "p1") })
        {
            ThrowOnRemoveAcknowledged = true,
        };
        var logs = new FakeUsageEventLogRepository();

        await RodarWorkerPor(reader, logs, TimeSpan.FromMilliseconds(300));

        Assert.Equal(1, reader.RemoveAcknowledgedCalls);
        Assert.Contains("1-0", reader.Acked);
    }

    [Fact]
    public async Task ExcecaoDeInfraNoCiclo_NaoDerrubaOWorker()
    {
        var reader = new FakeReader(new[] { new PendingRedisEntry("1-0", "p1") })
        {
            ThrowOnReadNew = true,
        };
        var logs = new FakeUsageEventLogRepository();

        await RodarWorkerPor(reader, logs, TimeSpan.FromMilliseconds(300));

        Assert.True(reader.ReadNewCalls > 1);
        Assert.Contains("1-0", reader.Acked);
    }
}

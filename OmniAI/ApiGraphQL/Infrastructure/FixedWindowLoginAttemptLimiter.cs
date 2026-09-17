using System.Threading.RateLimiting;
using ApiGraphQL.Domain.Abstractions;
using Microsoft.Extensions.Options;

namespace ApiGraphQL.Infrastructure;

// O rate limiter por endpoint do ASP.NET Core (usado no Webhook) nao serve aqui: o GraphQL tem um
// endpoint so, e limitar o /graphql inteiro travaria o dashboard, que dispara uma query a cada tecla
// no filtro. Por isso o limite e aplicado so ao login, por dentro do LoginUseCase.
public sealed class FixedWindowLoginAttemptLimiter : ILoginAttemptLimiter, IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public FixedWindowLoginAttemptLimiter(IOptions<LoginRateLimitOptions> options)
    {
        var permitLimit = options.Value.PermitLimit;
        var window = TimeSpan.FromSeconds(options.Value.WindowSeconds);

        _limiter = PartitionedRateLimiter.Create<string, string>(clientKey =>
            RateLimitPartition.GetFixedWindowLimiter(clientKey, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0,
            }));
    }

    public bool TryAcquire(string clientKey)
    {
        using var lease = _limiter.AttemptAcquire(clientKey);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}

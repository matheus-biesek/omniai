namespace ApiGraphQL.Domain.Abstractions;

public interface ILoginAttemptLimiter
{
    /// <summary>Consome uma tentativa de login para o cliente; false se o limite da janela ja foi atingido.</summary>
    bool TryAcquire(string clientKey);
}

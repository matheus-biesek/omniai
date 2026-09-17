using System.Security.Cryptography;
using System.Text;
using ApiGraphQL.Domain.Abstractions;
using Microsoft.Extensions.Options;

namespace ApiGraphQL.Application.Login;

public class LoginUseCase
{
    private readonly DashboardCredentialsOptions _credentials;
    private readonly IJwtTokenGenerator _tokenGenerator;
    private readonly ILoginAttemptLimiter _attemptLimiter;

    public LoginUseCase(
        IOptions<DashboardCredentialsOptions> credentials,
        IJwtTokenGenerator tokenGenerator,
        ILoginAttemptLimiter attemptLimiter)
    {
        _credentials = credentials.Value;
        _tokenGenerator = tokenGenerator;
        _attemptLimiter = attemptLimiter;
    }

    public string Executar(string username, string password, string clientKey)
    {
        // Toda tentativa conta, certa ou errada, e o limite vale antes de olhar a senha: se a senha
        // certa passasse durante o bloqueio, quem esta chutando continuaria descobrindo quando acertou.
        if (!_attemptLimiter.TryAcquire(clientKey))
        {
            throw new TooManyLoginAttemptsException();
        }

        // "&" (sem curto-circuito) e comparacao de tempo constante: o tempo de resposta nao revela se
        // o usuario estava certo, nem quantos caracteres da senha batem.
        var usernameMatches = FixedTimeEquals(username, _credentials.Username);
        var passwordMatches = FixedTimeEquals(password, _credentials.Password);
        if (!(usernameMatches & passwordMatches))
        {
            throw new InvalidCredentialsException();
        }

        return _tokenGenerator.GenerateToken(username);
    }

    // Compara os hashes, nao as strings: FixedTimeEquals exige tamanhos iguais, e comparar o tamanho
    // antes vazaria o tamanho da senha.
    private static bool FixedTimeEquals(string informed, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(informed)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}

using ApiGraphQL.Domain.Abstractions;
using Microsoft.Extensions.Options;

namespace ApiGraphQL.Application.Login;

public class LoginUseCase
{
    private readonly DashboardCredentialsOptions _credentials;
    private readonly IJwtTokenGenerator _tokenGenerator;

    public LoginUseCase(IOptions<DashboardCredentialsOptions> credentials, IJwtTokenGenerator tokenGenerator)
    {
        _credentials = credentials.Value;
        _tokenGenerator = tokenGenerator;
    }

    public string Executar(string username, string password)
    {
        if (username != _credentials.Username || password != _credentials.Password)
        {
            throw new InvalidCredentialsException();
        }

        return _tokenGenerator.GenerateToken(username);
    }
}

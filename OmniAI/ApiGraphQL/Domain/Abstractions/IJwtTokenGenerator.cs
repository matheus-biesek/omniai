namespace ApiGraphQL.Domain.Abstractions;

public interface IJwtTokenGenerator
{
    string GenerateToken(string username);
}

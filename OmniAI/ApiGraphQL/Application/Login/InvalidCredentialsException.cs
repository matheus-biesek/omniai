namespace ApiGraphQL.Application.Login;

public class InvalidCredentialsException : Exception
{
    public InvalidCredentialsException()
        : base("Usuário ou senha inválidos.")
    {
    }
}

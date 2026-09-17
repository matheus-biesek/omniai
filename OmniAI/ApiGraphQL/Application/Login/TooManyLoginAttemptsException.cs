namespace ApiGraphQL.Application.Login;

public class TooManyLoginAttemptsException : Exception
{
    public TooManyLoginAttemptsException()
        : base("Muitas tentativas de login. Aguarde um minuto e tente novamente.")
    {
    }
}

using ApiGraphQL.Application.Login;

namespace ApiGraphQL.Types;

public class Mutation
{
    public LoginPayload Login(string username, string password, LoginUseCase useCase)
    {
        var token = useCase.Executar(username, password);
        return new LoginPayload(token);
    }
}

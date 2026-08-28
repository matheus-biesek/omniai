namespace ApiGraphQL.Domain.Exceptions;

public class ProjectNameAlreadyExistsException : Exception
{
    public ProjectNameAlreadyExistsException(string name)
        : base($"Já existe um projeto chamado '{name}'.")
    {
    }
}

namespace ApiGraphQL.Domain.Exceptions;

public class ProjectNotFoundException : Exception
{
    public ProjectNotFoundException(Guid projectId)
        : base($"Projeto '{projectId}' não encontrado.")
    {
    }
}

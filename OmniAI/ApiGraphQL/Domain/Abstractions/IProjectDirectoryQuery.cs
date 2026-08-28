namespace ApiGraphQL.Domain.Abstractions;

public interface IProjectDirectoryQuery
{
    Task<IReadOnlyList<ProjectSummary>> GetAllAsync(CancellationToken cancellationToken);
}

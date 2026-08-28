using ApiGraphQL.Domain;
using ApiGraphQL.Domain.Abstractions;

namespace ApiGraphQL.Application.ListProjects;

public class ListProjectsUseCase
{
    private readonly IProjectDirectoryQuery _query;

    public ListProjectsUseCase(IProjectDirectoryQuery query)
    {
        _query = query;
    }

    public Task<IReadOnlyList<ProjectSummary>> ExecutarAsync(CancellationToken cancellationToken)
    {
        return _query.GetAllAsync(cancellationToken);
    }
}

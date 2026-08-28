using Shared.Entities;

namespace ApiGraphQL.Domain.Abstractions;

public interface IProjectRepository
{
    Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken);

    void Add(Project project);

    Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}

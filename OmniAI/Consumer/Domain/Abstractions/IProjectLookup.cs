namespace Consumer.Domain.Abstractions;

public interface IProjectLookup
{
    Task<Guid?> FindProjectIdByNameAsync(string projectName, CancellationToken cancellationToken);
}

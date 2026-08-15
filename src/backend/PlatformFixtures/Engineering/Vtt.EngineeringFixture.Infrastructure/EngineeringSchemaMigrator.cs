using JasperFx;
using Marten;

namespace Vtt.EngineeringFixture.Infrastructure;

public sealed class EngineeringSchemaMigrator(IDocumentStore documentStore)
{
    public Task ApplyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return documentStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync(
            AutoCreate.CreateOrUpdate);
    }
}

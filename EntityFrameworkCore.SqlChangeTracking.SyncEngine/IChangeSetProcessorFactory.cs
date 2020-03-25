using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public interface IChangeSetProcessorFactory<TDbContext> where TDbContext : DbContext
    {
        object GetChangeSetProcessorForEntity(IEntityType entityType);
    }
}
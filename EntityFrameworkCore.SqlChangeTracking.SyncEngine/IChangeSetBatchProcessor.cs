using System.Collections.Generic;
using System.Threading.Tasks;
using EntityFrameworkCore.SqlChangeTracking.Models;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public interface IChangeSetBatchProcessor<TEntity, TContext> where TContext : DbContext 
    {
        Task ProcessBatch(IEnumerable<ChangeTrackingEntry<TEntity>> changes, ChangeSetProcessorContext<TContext> context);
    }
}
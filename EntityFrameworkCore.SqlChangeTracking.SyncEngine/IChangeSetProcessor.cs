using System.Collections.Generic;
using System.Threading.Tasks;
using EntityFrameworkCore.SqlChangeTracking.Models;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public interface IChangeSetProcessor<TEntity, TContext> where TContext : DbContext 
    {
        Task ProcessChanges(IEnumerable<ChangeTrackingEntry<TEntity>> changes, ChangeSetProcessorContext<TContext> context);
    }
}
using System.Collections.Generic;
using System.Threading.Tasks;
using EntityFrameworkCore.SqlChangeTracking.Models;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public interface IChangeSetProcessor<TEntity, TDbContext> where TDbContext : DbContext 
    {
        string SyncContext { get; }
        Task ProcessChanges(IEnumerable<ChangeTrackingEntry<TEntity>> changes, ChangeSetProcessorContext<TDbContext> context);
    }

    public abstract class DefaultChangeSetProcessor<TEntity, TDbContext> : IChangeSetProcessor<TEntity, TDbContext> where TDbContext : DbContext
    {
        public virtual string SyncContext => "Default";

        public abstract Task ProcessChanges(IEnumerable<ChangeTrackingEntry<TEntity>> changes, ChangeSetProcessorContext<TDbContext> context);
    }
}
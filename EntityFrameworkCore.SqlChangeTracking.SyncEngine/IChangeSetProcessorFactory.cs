using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using EntityFrameworkCore.SqlChangeTracking.Extensions;
using EntityFrameworkCore.SqlChangeTracking.Models;
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public interface IChangeSetBatchProcessorFactory<TContext> where TContext : DbContext
    {
        IEnumerable<IChangeSetBatchProcessor<TEntity, TContext>> GetBatchProcessors<TEntity>();
    }

    public interface IChangeSetProcessor<TContext> where TContext : DbContext
    {
        Task ProcessChangeSet(IEntityType entityType, string syncContext);
    }

    public class ChangeSetProcessor<TContext> : IChangeSetProcessor<TContext> where TContext : DbContext
    {
        IBatchProcessorManagerFactory<TContext> _changeProcessorFactory;
        TContext _dbContext;

        public ChangeSetProcessor(IBatchProcessorManagerFactory<TContext> changeProcessorFactory, TContext dbContext)
        {
            _changeProcessorFactory = changeProcessorFactory;
            _dbContext = dbContext;
        }

        public async Task ProcessChangeSet(IEntityType entityType, string syncContext)
        {
            var processor = _changeProcessorFactory.GetBatchProcessorManager(syncContext);

            Func<ChangeSetProcessorContext<TContext>, ChangeTrackingEntry[], Task> batchCompleteFunc = (context, changeSet) =>
            {
                if (context.RecordCurrentVersion)
                    return context.DbContext.SetLastChangedVersionFor(entityType, changeSet.Max(e => e.ChangeVersion ?? 0), syncContext);

                return Task.CompletedTask;
            };

            var method = processor.GetType().GetMethod(nameof(IBatchProcessorManager<TContext>.ProcessChangesFor)).MakeGenericMethod(entityType.ClrType);

            while (true)
            {
                //IDbContextTransaction transaction;

                //if (_dbContext.Model.IsSnapshotIsolationEnabled())
                //    transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Snapshot);
                //else
                //    transaction = await _dbContext.Database.BeginTransactionAsync();

                //await using var t = transaction;

                var changesFunc = getNextChangeSetFunc(entityType, syncContext);

                var result = await (Task<bool>) method.Invoke(processor, new[] {changesFunc, batchCompleteFunc});

                if (!result)
                    break;

                //await t.CommitAsync();
            }
        }

        Delegate getNextChangeSetFunc(IEntityType entityType, string syncContext)
        {
            var getChangesMethodInfo = typeof(Extensions.DbContextExtensions).GetMethod(nameof(Extensions.DbContextExtensions
                    .GetNextChangeSetAsync), BindingFlags.Public | BindingFlags.Static).MakeGenericMethod(entityType.ClrType);
            
            var dbContextParameterExpression = Expression.Parameter(typeof(TContext), "dbContext");

            //var entityTypeParameter = Expression.Constant(entityType);
            var syncContextParameter = Expression.Constant(syncContext);

            var getChangesCallExpression = Expression.Call(getChangesMethodInfo, dbContextParameterExpression, syncContextParameter);

            var lambda = Expression.Lambda(getChangesCallExpression, dbContextParameterExpression);

            return lambda.Compile();
        }

        //Delegate getBatchCompleteFunc(IEntityType entityType, string syncContext)
        //{
        //    //if (processorContext.RecordCurrentVersion)
        //        //                await dbContext.SetLastChangedVersionFor(entityType, changeSet.Max(r => r.ChangeVersion ?? 0), syncContext);

        //        var processorContextParameter = Expression.Parameter(typeof(ChangeSetProcessorContext<TContext>), "processorContext");

        //    var entityTypeParameter = Expression.Constant(entityType);
        //    var syncContextParameter = Expression.Constant(syncContext);

        //    var getChangesCallExpression = Expression.Call(getChangesMethodInfo, dbContextParameterExpression, entityTypeParameter, syncContextParameter);

        //    var lambda = Expression.Lambda(getChangesCallExpression, processorContextParameter);

        //    return lambda.Compile();
        //}
    }
}
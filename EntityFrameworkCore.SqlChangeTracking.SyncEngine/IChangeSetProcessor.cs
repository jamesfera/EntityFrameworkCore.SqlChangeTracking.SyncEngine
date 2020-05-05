using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using EntityFrameworkCore.SqlChangeTracking.Logging;
using EntityFrameworkCore.SqlChangeTracking.Models;
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public interface IChangeSetProcessor<TContext> where TContext : DbContext
    {
        Task ProcessChanges(IEntityType entityType, string syncContext);
    }

    public class ChangeSetProcessor<TContext> : IChangeSetProcessor<TContext> where TContext : DbContext
    {
        IServiceScopeFactory _serviceScopeFactory;
        ILogger<ChangeSetProcessor<TContext>> _logger;

        public ChangeSetProcessor(IServiceScopeFactory serviceScopeFactory, ILogger<ChangeSetProcessor<TContext>> logger = null)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger ?? NullLogger<ChangeSetProcessor<TContext>>.Instance;
        }

        public async Task ProcessChanges(IEntityType entityType, string syncContext)
        {
            ValueTask BatchCompleteFunc(IChangeSetProcessorContext<TContext> context, IChangeTrackingEntry[] changeSet)
            {
                if (context.RecordCurrentVersion) return context.DbContext.SetLastChangedVersionAsync(entityType, syncContext, changeSet.Max(e => e.ChangeVersion ?? 0));

                return new ValueTask();
            }

            var method = GetType().GetMethod(nameof(processBatch), BindingFlags.NonPublic | BindingFlags.Instance).MakeGenericMethod(entityType.ClrType);

            IChangeTrackingEntry[] currentBatch;

            do
            {
                using var scope = _serviceScopeFactory.CreateScope();

                var dbContext = scope.ServiceProvider.GetService<TContext>();
                var changeSetBatchProcessorFactory = scope.ServiceProvider.GetRequiredService<IChangeSetBatchProcessorFactory<TContext>>();

                var logContext = dbContext.GetLogContext();

                logContext.Add(new KeyValuePair<string, object>("SyncContext", syncContext));

                using var logScope = _logger.BeginScope(logContext);

                using var processorContext = new ChangeSetProcessorContext<TContext>(dbContext, syncContext);

                //IDbContextTransaction transaction;

                //if (dbContext.Model.IsSnapshotIsolationEnabled())
                //    transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Snapshot);
                //else
                //    transaction = await dbContext.Database.BeginTransactionAsync();

                //await using var t = transaction;

                var changesFunc = getNextChangeSetFunc(entityType);

                currentBatch = await (ValueTask<IChangeTrackingEntry[]>) method.Invoke(this, new[] {syncContext as object, changesFunc, dbContext, changeSetBatchProcessorFactory, processorContext});

                if (processorContext.RecordCurrentVersion && currentBatch.Any())
                    await dbContext.SetLastChangedVersionAsync(entityType, syncContext, currentBatch.Max(e => e.ChangeVersion ?? 0));

                //await t?.CommitAsync();

            } while (currentBatch?.Any() ?? false);
        }

        async ValueTask<IChangeTrackingEntry[]> processBatch<TEntity>(string syncContext, Func<TContext, string, ValueTask<IChangeTrackingEntry<TEntity>[]>> getBatchFunc, TContext dbContext, IChangeSetBatchProcessorFactory<TContext> changeSetBatchProcessorFactory, ChangeSetProcessorContext<TContext> processorContext)
        {
            var processors = changeSetBatchProcessorFactory.GetBatchProcessors<TEntity>(syncContext).ToArray();

            if (!processors.Any())
            {
                _logger.LogWarning("No batch processors found for Entity: {EntityType} for SyncContext: {SyncContext}", typeof(TEntity), syncContext);
                return new IChangeTrackingEntry[0];
            }

            var batch = await getBatchFunc(dbContext, syncContext);

            if (!batch.Any())
            {
                _logger.LogDebug("No items found in batch.");
                return new IChangeTrackingEntry[0];
            }

            var entityType = dbContext.Model.FindEntityType(typeof(TEntity));

            _logger.LogInformation("Found {ChangeEntryCount} change(s) in current batch for Table: {TableName}", batch.Length, entityType.GetFullTableName());

            foreach (var changeSetProcessor in processors)
            {
                try
                {
                    await changeSetProcessor.ProcessBatch(batch, processorContext);
                }
                catch (Exception ex)
                {
                    throw;
                }
            }

            return batch;
        }

        Delegate getNextChangeSetFunc(IEntityType entityType)
        {
            var getChangesMethodInfo = typeof(InternalDbContextExtensions).GetMethod(nameof(InternalDbContextExtensions
                    .NextHelper), BindingFlags.Public | BindingFlags.Static).MakeGenericMethod(entityType.ClrType);
            
            var dbContextParameter = Expression.Parameter(typeof(TContext), "dbContext");

            var syncContextParameter = Expression.Parameter(typeof(string), "syncContext");// Expression.Constant(syncContext);

            var getChangesCallExpression = Expression.Call(getChangesMethodInfo, dbContextParameter, Expression.Constant(entityType), syncContextParameter);

            var lambda = Expression.Lambda(getChangesCallExpression, dbContextParameter, syncContextParameter);

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
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

            var method = typeof(IBatchProcessorManager<TContext>).GetMethod(nameof(IBatchProcessorManager<TContext>.ProcessBatch)).MakeGenericMethod(entityType.ClrType);

            while (true)
            {
                using var scope = _serviceScopeFactory.CreateScope();

                var dbContext = scope.ServiceProvider.GetService<TContext>();

                var logContext = dbContext.GetLogContext();

                logContext.Add(new KeyValuePair<string, object>("SyncContext", syncContext));

                using var logScope = _logger.BeginScope(logContext);

                var processor = scope.ServiceProvider.GetRequiredService<IBatchProcessorManager<TContext>>() ;

                //IDbContextTransaction transaction;

                //if (dbContext.Model.IsSnapshotIsolationEnabled())
                //    transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Snapshot);
                //else
                //    transaction = await dbContext.Database.BeginTransactionAsync();

                //await using var t = transaction;

                var changesFunc = getNextChangeSetFunc(entityType);

                var result = await (ValueTask<bool>) method.Invoke(processor, new[] { syncContext as object, changesFunc, (Func<IChangeSetProcessorContext<TContext>, IChangeTrackingEntry[], ValueTask>) BatchCompleteFunc});

                if (!result)
                    break;

                //await t?.CommitAsync();
            }
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
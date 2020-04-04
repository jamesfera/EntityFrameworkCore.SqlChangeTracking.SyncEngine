using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using EntityFrameworkCore.SqlChangeTracking.Models;
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public interface IChangeSetProcessorFactory<TContext> where TContext : DbContext
    {
        IEnumerable<IChangeSetProcessor<TEntity, TContext>> GetChangeSetProcessorsFor<TEntity>();
    }

    public interface IChangeStuff<TContext> where TContext : DbContext
    {
        Task GetChangeSetProcessorsForEntity(IEntityType entityType, string syncContext);
    }

    public class ChangeStuff<TContext> : IChangeStuff<TContext> where TContext : DbContext
    {
        IChangeProcessorFactory<TContext> _changeProcessorFactory;

        public ChangeStuff(IChangeProcessorFactory<TContext> changeProcessorFactory)
        {
            _changeProcessorFactory = changeProcessorFactory;
        }

        public async Task GetChangeSetProcessorsForEntity(IEntityType entityType, string syncContext)
        {
            var processor = _changeProcessorFactory.GetChangeProcessor(syncContext);

            var method = processor.GetType().GetMethod(nameof(IChangeProcessor<TContext>.ProcessChangesFor)).MakeGenericMethod(entityType.ClrType);

            var changesFunc = getChangesSinceLastUpdateFunc(entityType, syncContext);

            //var batchCompleteFunc = getBatchCompleteFunc(entityType, syncContext);

            Func<ChangeSetProcessorContext<TContext>, ChangeTrackingEntry[], Task> batchCompleteFunc = (c, b) =>
            {
                if (c.RecordCurrentVersion)
                    return c.DbContext.SetLastChangedVersionFor(entityType, b.Max(e => e.ChangeVersion ?? 0), syncContext);

                return Task.CompletedTask;
            };
            
            await (Task) method.Invoke(processor, new[] { changesFunc, batchCompleteFunc });
        }

        Delegate getChangesSinceLastUpdateFunc(IEntityType entityType, string syncContext)
        {
            var getChangesMethodInfo = typeof(Extensions.DbContextExtensions).GetMethod(nameof(Extensions.DbContextExtensions
                    .GetChangesSinceLastVersion), BindingFlags.Public | BindingFlags.Static).MakeGenericMethod(entityType.ClrType);
            
            var dbContextParameterExpression = Expression.Parameter(typeof(TContext), "dbContext");

            var entityTypeParameter = Expression.Constant(entityType);
            var syncContextParameter = Expression.Constant(syncContext);

            var getChangesCallExpression = Expression.Call(getChangesMethodInfo, dbContextParameterExpression, entityTypeParameter, syncContextParameter);

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
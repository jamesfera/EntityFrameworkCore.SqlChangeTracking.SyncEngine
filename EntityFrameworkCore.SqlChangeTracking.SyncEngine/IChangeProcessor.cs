using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using EntityFrameworkCore.SqlChangeTracking.Models;
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public interface IChangeProcessor<TContext> where TContext : DbContext
    {
        Task ProcessChangesForTable<T>();

        Task ProcessChangesForTable(IEntityType entityType);

    }

    public class ChangeProcessor<TContext> : IChangeProcessor<TContext> where TContext : DbContext
    {
        private IServiceScopeFactory _serviceScopeFactory;
        private ILogger<ChangeProcessor<TContext>> _logger;
        private IChangeSetProcessorFactory<TContext> _changeSetProcessorFactory;

        public ChangeProcessor(IServiceScopeFactory serviceScopeFactory, ILogger<ChangeProcessor<TContext>> logger, IChangeSetProcessorFactory<TContext> changeSetProcessorFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
            _changeSetProcessorFactory = changeSetProcessorFactory;
        }

        public async Task ProcessChangesForTable<T>()
        {
            using var scope = _serviceScopeFactory.CreateScope();

            var context = scope.ServiceProvider.GetService<DbContext>();
        }

        public async Task ProcessChangesForTable(IEntityType entityType)
        {
            try
            {
                using var serviceScope = _serviceScopeFactory.CreateScope();

                var dbContext = serviceScope.ServiceProvider.GetRequiredService<TContext>();

                var lastChangedVersion = await dbContext.GetLastChangedVersionFor(entityType) ?? 0;

                var changeSet = getChangesFunc(entityType)(dbContext, lastChangedVersion);

                if (!changeSet.Any())
                    return;

                using var processorContext = new ChangeSetProcessorContext<TContext>(dbContext);

                var changeSetProcessor = _changeSetProcessorFactory.GetChangeSetProcessorForEntity(entityType);

                var processorFunc = generateProcessorFunc(entityType, _changeSetProcessorFactory);

                await processorFunc(changeSetProcessor, changeSet, processorContext);

                if (processorContext.RecordCurrentVersion)
                    await dbContext.SetLastChangedVersionFor(entityType, changeSet.Max(r => r.ChangeVersion ?? 0));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ERROR");
            }
        }

        Func<TContext, long, IEnumerable<ChangeTrackingEntry>> getChangesFunc(IEntityType entityType)
        {
            var getChangesMethodInfo = typeof(SqlChangeTracking.DbSetExtensions).GetMethod(nameof(SqlChangeTracking.DbSetExtensions
                .GetChangesSinceVersion)).MakeGenericMethod(entityType.ClrType);

            var dbSetType = typeof(DbSet<>).MakeGenericType(entityType.ClrType);

            var dbSetPropertyInfo = typeof(TContext).GetProperties().FirstOrDefault(p => p.PropertyType == dbSetType);

            if (dbSetPropertyInfo == null)
                throw new Exception($"No Property of type {dbSetType.PrettyName()} found on {typeof(TContext).Name}");

            var dbContextParameterExpression = Expression.Parameter(typeof(TContext), "dbContext");
            var dbSetPropertyExpression = Expression.Property(dbContextParameterExpression, dbSetPropertyInfo);

            var versionParameter = Expression.Parameter(typeof(long), "version");

            var getChangesCallExpression = Expression.Call(getChangesMethodInfo, dbSetPropertyExpression, versionParameter);

            var lambda = Expression.Lambda<Func<TContext, long, IEnumerable<ChangeTrackingEntry>>>(getChangesCallExpression, dbContextParameterExpression, versionParameter);

            return lambda.Compile();
        }
        Func<object, IEnumerable<ChangeTrackingEntry>, ChangeSetProcessorContext<TContext>, Task> generateProcessorFunc(IEntityType entityType, IChangeSetProcessorFactory<TContext> processorFactory)
        {
            var processor = processorFactory.GetChangeSetProcessorForEntity(entityType);
            Type processorType = processor.GetType();

            var processorParameter = Expression.Parameter(typeof(object), "processor");
            var changeSetParameter = Expression.Parameter(typeof(IEnumerable<ChangeTrackingEntry>), "changeSet");
            var handlerContextParameter = Expression.Parameter(typeof(ChangeSetProcessorContext<TContext>), "handlerContext");

            var changeSetType = typeof(IEnumerable<>).MakeGenericType(typeof(ChangeTrackingEntry<>).MakeGenericType(entityType.ClrType));

            var processorCall = Expression.Call(Expression.Convert(processorParameter, processorType), processorType.GetMethod(nameof(IChangeSetProcessor<object, TContext>.ProcessChanges)), Expression.Convert(changeSetParameter, changeSetType), handlerContextParameter);

            return Expression.Lambda<Func<object, IEnumerable<ChangeTrackingEntry>, ChangeSetProcessorContext<TContext>, Task>>(processorCall, processorParameter, changeSetParameter, handlerContextParameter).Compile();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading;
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
        Task ProcessChangesForTable(IEntityType entityType);
    }

    public class ChangeProcessor<TContext> : IChangeProcessor<TContext> where TContext : DbContext
    {
        readonly IServiceScopeFactory _serviceScopeFactory;
        readonly ILogger<ChangeProcessor<TContext>> _logger;

        public ChangeProcessor(IServiceScopeFactory serviceScopeFactory, ILogger<ChangeProcessor<TContext>> logger)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
        }

        SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public async Task ProcessChangesForTable(IEntityType entityType)
        {
            try
            {
                await  _semaphore.WaitAsync();

                _logger.LogInformation("Processing changes for Table: {tableName}", entityType.GetTableName());

                using var serviceScope = _serviceScopeFactory.CreateScope();

                var dbContext = serviceScope.ServiceProvider.GetRequiredService<TContext>();

                var changeSetProcessorFactory = serviceScope.ServiceProvider.GetService<IChangeSetProcessorFactory<TContext>>();

                var syncContexts = entityType.GetSyncContexts();

                foreach (var syncContext in syncContexts)
                {
                    var lastChangedVersion = await dbContext.GetLastChangedVersionFor(entityType, syncContext) ?? 0;

                    var changeSet = getChangesFunc(entityType)(dbContext, lastChangedVersion, 3);
                    
                    _logger.LogInformation("Found {changeSetCount} change(s) for Table: {tableName} for SyncContext: {syncContext} since version: {version}", changeSet.Count(), entityType.GetTableName(), syncContext, lastChangedVersion);

                    if (!changeSet.Any())
                        continue;
                    
                    var changeSetProcessors = changeSetProcessorFactory.GetChangeSetProcessorsForEntity(entityType, syncContext).ToArray();

                    if(!changeSetProcessors.Any())
                        return;

                    using var processorContext = new ChangeSetProcessorContext<TContext>(dbContext);

                    foreach (var changeSetProcessor in changeSetProcessors)
                    {
                        var processorFunc = generateProcessorFunc(entityType, changeSetProcessor);

                        try
                        {
                            await processorFunc(changeSetProcessor, changeSet, processorContext);
                        }
                        catch (Exception ex)
                        {
                            throw;
                        }
                    }

                    if (processorContext.RecordCurrentVersion)
                        await dbContext.SetLastChangedVersionFor(entityType, changeSet.Max(r => r.ChangeVersion ?? 0), syncContext);
                }
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ERROR");
            }
            finally
            {
                _semaphore.Release();
            }
        }

        Func<TContext, long, int, IEnumerable<ChangeTrackingEntry>> getChangesFunc(IEntityType entityType)
        {
            var getChangesMethodInfo = typeof(SyncEngine.Extensions.DbSetExtensions).GetMethod(nameof(SyncEngine.Extensions.DbSetExtensions
                .GetChangesSinceVersion), BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(entityType.ClrType);

            var dbSetType = typeof(DbSet<>).MakeGenericType(entityType.ClrType);

            var dbSetPropertyInfo = typeof(TContext).GetProperties().FirstOrDefault(p => p.PropertyType == dbSetType);

            if (dbSetPropertyInfo == null)
                throw new Exception($"No Property of type {dbSetType.PrettyName()} found on {typeof(TContext).Name}");

            var dbContextParameterExpression = Expression.Parameter(typeof(TContext), "dbContext");
            var dbSetPropertyExpression = Expression.Property(dbContextParameterExpression, dbSetPropertyInfo);

            var versionParameter = Expression.Parameter(typeof(long), "version");
            var maxResultsParameter = Expression.Parameter(typeof(int), "maxResults");

            var getChangesCallExpression = Expression.Call(getChangesMethodInfo, dbSetPropertyExpression, versionParameter, maxResultsParameter);

            var lambda = Expression.Lambda<Func<TContext, long, int, IEnumerable<ChangeTrackingEntry>>>(getChangesCallExpression, dbContextParameterExpression, versionParameter, maxResultsParameter);

            return lambda.Compile();
        }
        Func<object, IEnumerable<ChangeTrackingEntry>, ChangeSetProcessorContext<TContext>, Task> generateProcessorFunc(IEntityType entityType, object processor)
        {
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

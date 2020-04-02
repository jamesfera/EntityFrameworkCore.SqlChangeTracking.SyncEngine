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

                _logger.LogInformation("Processing changes for Table: {TableName}", entityType.GetFullTableName());

                using var serviceScope = _serviceScopeFactory.CreateScope();

                var dbContext = serviceScope.ServiceProvider.GetRequiredService<TContext>();

                var logContext = dbContext.GetLogContext();

                using var logScope = _logger.BeginScope(logContext);

                var changeSetProcessorFactory = serviceScope.ServiceProvider.GetService<IChangeSetProcessorFactory<TContext>>();

                var syncContexts = entityType.GetSyncContexts();

                var clrEntityType = entityType.ClrType;

                foreach (var syncContext in syncContexts)
                {
                    var lastChangedVersion = await dbContext.GetLastChangedVersionFor(entityType, syncContext) ?? 0;

                    var changeSet = getChangesFunc(entityType)(dbContext, lastChangedVersion, 3);
                    
                    _logger.LogInformation("Found {ChangeSetCount} change(s) for Table: {TableName} for SyncContext: {SyncContext} since version: {ChangeVersion}", changeSet.Count(), entityType.GetFullTableName(), syncContext, lastChangedVersion);

                    if (!changeSet.Any())
                        continue;

                    //var it = clrEntityType.GetInterfaces().First();

                    var changeSetProcessors = changeSetProcessorFactory.GetChangeSetProcessorsForEntity(clrEntityType, syncContext).ToArray();

                    if(!changeSetProcessors.Any())
                        return;

                    using var processorContext = new ChangeSetProcessorContext<TContext>(dbContext);

                    var newSet = changeSet.Select(c => convert(clrEntityType, clrEntityType, c));

                    var ofTypeMethod = typeof(Enumerable).GetMethod(nameof(Enumerable.OfType)).MakeGenericMethod(typeof(ChangeTrackingEntry<>).MakeGenericType(clrEntityType));

                    var res = ofTypeMethod.Invoke(null, new []{ newSet });

                    foreach (var changeSetProcessor in changeSetProcessors)
                    {
                        var processorFunc = generateProcessorFunc(clrEntityType, changeSetProcessor);

                        try
                        {
                            await processorFunc(changeSetProcessor, res, processorContext);
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
                _logger.LogError(ex, "Error Processing Changes for Table: {TableName}", entityType.GetFullTableName());
            }
            finally
            {
                _semaphore.Release();
            }
        }

        ChangeTrackingEntry convert(Type interfaceType, Type concreteType, object entry)
        {
            var withTypeMethod = typeof(ChangeTrackingEntry<>).MakeGenericType(concreteType).GetMethod(nameof(ChangeTrackingEntry<object>.WithType));
            var method = withTypeMethod.MakeGenericMethod(interfaceType);

            var entityParam = Expression.Parameter(entry.GetType(), "entity");
            var withTypeCall = Expression.Call(entityParam, method);

            var lambda = Expression.Lambda(withTypeCall, entityParam).Compile();

            return lambda.DynamicInvoke(entry) as ChangeTrackingEntry;
        }

        Func<TContext, long, int, IEnumerable<ChangeTrackingEntry>> getChangesFunc(IEntityType entityType)
        {
            var getChangesMethodInfo = typeof(Extensions.DbSetExtensions).GetMethod(nameof(Extensions.DbSetExtensions
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
        Func<object, object, ChangeSetProcessorContext<TContext>, Task> generateProcessorFunc(Type entityType, object processor)
        {
            Type processorType = processor.GetType();

            var processorParameter = Expression.Parameter(typeof(object), "processor");
            var changeSetParameter = Expression.Parameter(typeof(object), "changeSet");
            var handlerContextParameter = Expression.Parameter(typeof(ChangeSetProcessorContext<TContext>), "handlerContext");

            var changeSetType = typeof(IEnumerable<>).MakeGenericType(typeof(ChangeTrackingEntry<>).MakeGenericType(entityType)); //(typeof(ChangeTrackingEntry<>).MakeGenericType(entityType)).MakeArrayType();

            var processorCall = Expression.Call(Expression.Convert(processorParameter, processorType), processorType.GetMethod(nameof(IChangeSetProcessor<object, TContext>.ProcessChanges)), Expression.Convert(changeSetParameter, changeSetType), handlerContextParameter);

            return Expression.Lambda<Func<object, object, ChangeSetProcessorContext<TContext>, Task>>(processorCall, processorParameter, changeSetParameter, handlerContextParameter).Compile();
        }
    }
}

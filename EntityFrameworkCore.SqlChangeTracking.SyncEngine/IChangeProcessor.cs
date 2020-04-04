using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.SqlChangeTracking.AsyncLinqExtensions;
using EntityFrameworkCore.SqlChangeTracking.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public interface IChangeSetProcessorRegistration
    {
        KeyValuePair<string, Type> Registration { get; }
    }

    public class ChangeSetProcessorRegistration : IChangeSetProcessorRegistration
    {
        public ChangeSetProcessorRegistration(KeyValuePair<string, Type> registration)
        {
            Registration = registration;
        }

        public KeyValuePair<string, Type> Registration { get; }
    }

    public interface IChangeProcessorFactory<TContext> where TContext : DbContext
    {
        IChangeProcessor<TContext> GetChangeProcessor(string syncContext = "Default");
    }

    internal class ChangeProcessorFactory<TContext> : IChangeProcessorFactory<TContext> where TContext : DbContext
    {
        IServiceProvider _serviceProvider;
        Dictionary<string, Type[]> _registrations;

        public ChangeProcessorFactory(IServiceProvider serviceProvider, IEnumerable<IChangeSetProcessorRegistration> registrations)
        {
            _serviceProvider = serviceProvider;
            _registrations = registrations.Select(r => r.Registration).GroupBy(r => r.Key, r => r.Value).ToDictionary(g=>g.Key, g => g.ToArray());
        }

        public IChangeProcessor<TContext> GetChangeProcessor(string syncContext)
        {
            if (!_registrations.TryGetValue(syncContext, out Type[] serviceTypes))
                ;

            var changeSetProcessorFactory = new InternalChangeSetProcessorFactory(_serviceProvider, serviceTypes);

            return new ChangeProcessor<TContext>(_serviceProvider.GetService<ILogger<ChangeProcessor<TContext>>>(), _serviceProvider.GetService<TContext>(), changeSetProcessorFactory);
        }

        class InternalChangeSetProcessorFactory : IChangeSetProcessorFactory<TContext>
        {
            IServiceProvider _serviceProvider;
            Type[] _processorServiceTypes;

            public InternalChangeSetProcessorFactory(IServiceProvider serviceProvider, Type[] processorServiceTypes)
            {
                _serviceProvider = serviceProvider;
                _processorServiceTypes = processorServiceTypes;
            }

            public IEnumerable<IChangeSetProcessor<TEntity, TContext>> GetChangeSetProcessorsFor<TEntity>()
            {
                var entityProcessorTypes = _processorServiceTypes.Where(p => p.GenericTypeArguments[0] == typeof(TEntity) && p.GenericTypeArguments[1] == typeof(TContext));

                var services = entityProcessorTypes.Select(t => (IChangeSetProcessor<TEntity, TContext>)_serviceProvider.GetService(t)).ToArray();

                return services;
            }
        }
    }

    //public interface IChangeSetProcessorFactoryRegistry<TContext> where TContext : DbContext
    //{
    //    IChangeSetProcessorFactory<TContext> GetChangeSetProcessorFactory(string syncContext);
    //    void SetChangeSetProcessorFactory(string syncContext, Type serviceType);
    //}

    //internal class ChangeSetProcessorFactoryRegistry<TContext> : IChangeSetProcessorFactoryRegistry<TContext> where TContext : DbContext
    //{
    //    IServiceProvider _serviceProvider;
    //    Dictionary<string, Type> _registry = new Dictionary<string, Type>();

    //    public ChangeSetProcessorFactoryRegistry(IServiceProvider serviceProvider)
    //    {
    //        _serviceProvider = serviceProvider;
    //    }

    //    public IChangeSetProcessorFactory<TContext> GetChangeSetProcessorFactory(string syncContext)
    //    {
    //        if (!_registry.TryGetValue(syncContext, out Type serviceType))
    //            throw new Exception($"No Change Set Processor registered for DbContext: ${typeof(TContext).PrettyName()} and SyncContext: {syncContext}");



    //        return null;
    //    }

    //    public void SetChangeSetProcessorFactory(string syncContext, Type serviceType)
    //    {
            
    //    }

    //    //class InternalChangeSetProcessorFactory : IChangeSetProcessorFactory<TContext>
    //    //{
    //    //    public IEnumerable<IChangeSetProcessor<TEntity, TContext>> GetChangeSetProcessorsFor<TEntity>()
    //    //    {
    //    //        return _serviceProvider.GetServices<IChangeSetProcessor<TEntity, TContext>>();
    //    //    }
    //    //}
    //}

    public interface IChangeProcessor<TContext> where TContext : DbContext
    {
        Task ProcessChangesFor<TEntity>(Func<TContext, IAsyncEnumerable<ChangeTrackingEntry<TEntity>>> getChangesFunc, Func<ChangeSetProcessorContext<TContext>, ChangeTrackingEntry<TEntity>[], Task>? batchCompleteFunc = null);
    }

    public class ChangeProcessor<TContext> : IChangeProcessor<TContext> where TContext : DbContext
    {
        readonly TContext _dbContext;
        readonly IChangeSetProcessorFactory<TContext> _changeSetProcessorFactory;
        readonly ILogger<ChangeProcessor<TContext>> _logger;

        public ChangeProcessor(ILogger<ChangeProcessor<TContext>> logger, TContext dbContext, IChangeSetProcessorFactory<TContext> changeSetProcessorFactory)
        {
            _logger = logger;
            _dbContext = dbContext;
            _changeSetProcessorFactory = changeSetProcessorFactory;
        }

        SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public async Task ProcessChangesFor<TEntity>(Func<TContext, IAsyncEnumerable<ChangeTrackingEntry<TEntity>>> getChangesFunc, Func<ChangeSetProcessorContext<TContext>, ChangeTrackingEntry<TEntity>[], Task>? batchCompleteFunc = null)
        {
            var dbContext = _dbContext;

            var entityType = dbContext.Model.FindEntityType(typeof(TEntity));

            _logger.LogInformation("Processing changes for Table: {TableName}", entityType.GetFullTableName());

            var changeSet = getChangesFunc(dbContext);

            var batchPage = 0;

            var batchSize = 10;

            var changeSetBatch = await changeSet.Skip(batchSize * batchPage).Take(batchSize).ToArrayAsync();

            var processors = _changeSetProcessorFactory.GetChangeSetProcessorsFor<TEntity>();

            if (!processors.Any())
            {
                return;
            }

            while (changeSetBatch.Any())
            {
                using var processorContext = new ChangeSetProcessorContext<TContext>(dbContext);

                foreach (var changeSetProcessor in processors)
                {
                    try
                    {
                        await changeSetProcessor.ProcessChanges(changeSetBatch, processorContext);
                    }
                    catch (Exception ex)
                    {
                        throw;
                    }
                }

                if (batchCompleteFunc != null)
                    await batchCompleteFunc.Invoke(processorContext, changeSetBatch);
                
                batchPage++;

                changeSetBatch = await changeSet.Skip(batchSize * batchPage).Take(batchSize).ToArrayAsync();
            }
        }

        //public async Task ProcessChangesForTable(IEntityType entityType)
        //{
        //    try
        //    {
        //        await  _semaphore.WaitAsync();

        //        using var serviceScope = _serviceScopeFactory.CreateScope();

        //        var dbContext = serviceScope.ServiceProvider.GetRequiredService<TContext>();

        //        var logContext = dbContext.GetLogContext();

        //        using var logScope = _logger.BeginScope(logContext);

        //        _logger.LogInformation("Processing changes for Table: {TableName}", entityType.GetFullTableName());

        //        var changeSetProcessorFactory = serviceScope.ServiceProvider.GetService<IChangeSetProcessorFactory<TContext>>();

        //        var syncContexts = entityType.GetSyncContexts();

        //        var clrEntityType = entityType.ClrType;

        //        foreach (var syncContext in syncContexts)
        //        {
        //            var lastChangedVersion = await dbContext.GetLastChangedVersionFor(entityType, syncContext) ?? 0;

        //            var changeSet = getChangesFunc(entityType)(dbContext, lastChangedVersion, 3);
                    
        //            _logger.LogInformation("Found {ChangeSetCount} change(s) for Table: {TableName} for SyncContext: {SyncContext} since version: {ChangeVersion}", changeSet.Count(), entityType.GetFullTableName(), syncContext, lastChangedVersion);

        //            if (!changeSet.Any())
        //                continue;

        //            //var it = clrEntityType.GetInterfaces().First();

        //            var changeSetProcessors = changeSetProcessorFactory.GetChangeSetProcessorsForEntity(clrEntityType, syncContext).ToArray();

        //            if(!changeSetProcessors.Any())
        //                return;

        //            using var processorContext = new ChangeSetProcessorContext<TContext>(dbContext);

        //            var newSet = changeSet.Select(c => convert(clrEntityType, clrEntityType, c));

        //            var ofTypeMethod = typeof(Enumerable).GetMethod(nameof(Enumerable.OfType)).MakeGenericMethod(typeof(ChangeTrackingEntry<>).MakeGenericType(clrEntityType));

        //            var res = ofTypeMethod.Invoke(null, new []{ newSet });

        //            foreach (var changeSetProcessor in changeSetProcessors)
        //            {
        //                var processorFunc = generateProcessorFunc(clrEntityType, changeSetProcessor);

        //                try
        //                {
        //                    await processorFunc(changeSetProcessor, res, processorContext);
        //                }
        //                catch (Exception ex)
        //                {
        //                    throw;
        //                }
        //            }

        //            if (processorContext.RecordCurrentVersion)
        //                await dbContext.SetLastChangedVersionFor(entityType, changeSet.Max(r => r.ChangeVersion ?? 0), syncContext);
        //        }
                
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error Processing Changes for Table: {TableName}", entityType.GetFullTableName());
        //    }
        //    finally
        //    {
        //        _semaphore.Release();
        //    }
        //}

        ChangeTrackingEntry convert(Type interfaceType, Type concreteType, object entry)
        {
            var withTypeMethod = typeof(ChangeTrackingEntry<>).MakeGenericType(concreteType).GetMethod(nameof(ChangeTrackingEntry<object>.WithType));
            var method = withTypeMethod.MakeGenericMethod(interfaceType);

            var entityParam = Expression.Parameter(entry.GetType(), "entity");
            var withTypeCall = Expression.Call(entityParam, method);

            var lambda = Expression.Lambda(withTypeCall, entityParam).Compile();

            return lambda.DynamicInvoke(entry) as ChangeTrackingEntry;
        }

        //Func<TContext, long, int, IEnumerable<ChangeTrackingEntry>> getChangesFunc(IEntityType entityType)
        //{
        //    var getChangesMethodInfo = typeof(Extensions.DbSetExtensions).GetMethod(nameof(Extensions.DbSetExtensions
        //        .GetChangesSinceVersion), BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(entityType.ClrType);

        //    var dbSetType = typeof(DbSet<>).MakeGenericType(entityType.ClrType);

        //    var dbSetPropertyInfo = typeof(TContext).GetProperties().FirstOrDefault(p => p.PropertyType == dbSetType);

        //    if (dbSetPropertyInfo == null)
        //        throw new Exception($"No Property of type {dbSetType.PrettyName()} found on {typeof(TContext).Name}");

        //    var dbContextParameterExpression = Expression.Parameter(typeof(TContext), "dbContext");
        //    var dbSetPropertyExpression = Expression.Property(dbContextParameterExpression, dbSetPropertyInfo);

        //    var versionParameter = Expression.Parameter(typeof(long), "version");
        //    var maxResultsParameter = Expression.Parameter(typeof(int), "maxResults");
            
        //    var getChangesCallExpression = Expression.Call(getChangesMethodInfo, dbSetPropertyExpression, versionParameter, maxResultsParameter);

        //    var lambda = Expression.Lambda<Func<TContext, long, int, IEnumerable<ChangeTrackingEntry>>>(getChangesCallExpression, dbContextParameterExpression, versionParameter, maxResultsParameter);

        //    return lambda.Compile();
        //}
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

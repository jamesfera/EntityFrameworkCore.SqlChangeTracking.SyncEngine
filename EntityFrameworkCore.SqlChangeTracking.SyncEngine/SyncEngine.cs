using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.SqlChangeTracking.Models;
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Extensions;
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public class SyncEngine<TDbContext> : ISyncEngine<TDbContext> where TDbContext : DbContext
    {
        private IConfiguration _configuration;

        private ILogger<SyncEngine<TDbContext>> _logger;
        private IChangeSetProcessorFactory<TDbContext> _changeSetProcessorFactory;
        private IServiceScopeFactory _serviceScopeFactory;

        private List<SqlDependencyEx> _sqlTableDependencies = new List<SqlDependencyEx>();

        static int _SqlDependencyIdentity = 0;

        public SyncEngine(IConfiguration configuration, ILogger<SyncEngine<TDbContext>> logger, IChangeSetProcessorFactory<TDbContext> changeSetProcessorFactory, IServiceScopeFactory serviceScopeFactory)
        {
            _configuration = configuration;
            _logger = logger;
            _changeSetProcessorFactory = changeSetProcessorFactory;
            _serviceScopeFactory = serviceScopeFactory;
        }
        
        public Task Start()
        {
            IEntityType[] syncEngineEntityTypes;
            string databaseName;

            using (var serviceScope = _serviceScopeFactory.CreateScope())
            {
                var dbContext = serviceScope.ServiceProvider.GetRequiredService<TDbContext>();

                syncEngineEntityTypes = dbContext.Model.GetEntityTypes().Where(EntityTypeExtensions.IsSyncEngineEnabled).ToArray();

                databaseName = dbContext.Database.GetDbConnection().Database;
            }

            SqlDependencyEx.CleanDatabase(_configuration.GetConnectionString("LegacyConnection"), databaseName);

            _logger.LogInformation("Found {entityTrackingCount} Entities with Sync Engine enabled", syncEngineEntityTypes.Length);

            foreach (var entityType in syncEngineEntityTypes)
            {
                var tableName = entityType.GetTableName();

                Interlocked.Increment(ref _SqlDependencyIdentity);

                var sqlTableDependency = new SqlDependencyEx(_configuration.GetConnectionString("LegacyConnection"), databaseName, tableName, identity: _SqlDependencyIdentity);

                _logger.LogDebug("Create SqlDependency on table {tableName} with identity: {sqlDependencyId}", tableName, _SqlDependencyIdentity);

                sqlTableDependency.TableChanged += async (object sender, SqlDependencyEx.TableChangedEventArgs e) => await ProcessChangeEvent(sender);

                _sqlTableDependencies.Add(sqlTableDependency);

                _logger.LogInformation("Sync Engine configured for Entity: {entityTypeName}", entityType.Name);
            }

            _sqlTableDependencies.ForEach(s => s.Start());

            return Task.CompletedTask;
        }

        async Task ProcessChangeEvent(object sender)
        {
            ChangeSetProcessorContext<TDbContext> handlerContext = null;
            
            try
            {
                var tableName = ((SqlDependencyEx)sender).TableName;

                _logger.LogInformation("Change detected in table: {tableName}", tableName);
                
                using var serviceScope = _serviceScopeFactory.CreateScope();

                var dbContext = serviceScope.ServiceProvider.GetRequiredService<TDbContext>();

                IEntityType entityType = dbContext.Model.GetEntityTypes().FirstOrDefault(e => e.GetTableName() == tableName);

                var lastChangedVersion = await dbContext.GetLastChangedVersionFor(entityType);

                var getChangesMethod = typeof(SqlChangeTracking.DbSetExtensions).GetMethod(nameof(SqlChangeTracking.DbSetExtensions
                    .GetChangesSinceVersion));

                var dbSetType = typeof(DbSet<>).MakeGenericType(entityType.ClrType);

                var dbSet = dbContext.GetType().GetProperties().FirstOrDefault(p => p.PropertyType == dbSetType)?.GetValue(dbContext);
                
                var getChangesForEntity = getChangesMethod.MakeGenericMethod(entityType.ClrType);

                var results = getChangesForEntity.Invoke(null, new[] { dbSet, lastChangedVersion });

                var resultsList = results as IEnumerable<ChangeTrackingEntry>;

                if (!resultsList.Any())
                    return;

                handlerContext = new ChangeSetProcessorContext<TDbContext>(dbContext);

                var changeHandler = _changeSetProcessorFactory.GetChangeSetProcessorForEntity(entityType);

                Type handlerType = changeHandler.GetType();

                await (Task)handlerType.GetMethod(nameof(IChangeSetProcessor<object, TDbContext>.ProcessChanges))
                    .Invoke(changeHandler, new[] { results, handlerContext });

                if (handlerContext.RecordCurrentVersion)
                    await dbContext.SetLastChangedVersionFor(entityType, resultsList.Max(r => r.ChangeVersion));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ERROR");
            }
            finally
            {
                handlerContext?.Dispose();
            }
        }

        public void Stop()
        {
            _sqlTableDependencies.ForEach(s => s.Dispose());
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
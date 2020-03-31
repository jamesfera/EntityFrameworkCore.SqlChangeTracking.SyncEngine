using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.SqlChangeTracking.Models;
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Extensions;
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public class SyncEngine<TContext> : ISyncEngine<TContext> where TContext : DbContext
    {
        readonly IConfiguration _configuration;
        readonly ILogger<SyncEngine<TContext>> _logger;
        readonly IServiceScopeFactory _serviceScopeFactory;
        readonly ITableChangedNotificationDispatcher _tableChangedNotificationDispatcher;
        readonly IChangeProcessor<TContext> _changeProcessor;

        readonly List<SqlDependencyEx> _sqlTableDependencies = new List<SqlDependencyEx>();
        Dictionary<string, IEntityType> _tableNameToEntityTypeMappings;

        static int _SqlDependencyIdentity = 0;

        public SyncEngine(IConfiguration configuration, ILogger<SyncEngine<TContext>> logger, IServiceScopeFactory serviceScopeFactory,
            ITableChangedNotificationDispatcher tableChangedNotificationDispatcher, IChangeProcessor<TContext> changeProcessor)
        {
            _configuration = configuration;
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _tableChangedNotificationDispatcher = tableChangedNotificationDispatcher;
            _changeProcessor = changeProcessor;
        }

        public async Task Start(bool syncOnStartup, CancellationToken cancellationToken)
        {
            List<IEntityType> syncEngineEntityTypes;
            string databaseName;

            using (var serviceScope = _serviceScopeFactory.CreateScope())
            {
                var dbContext = serviceScope.ServiceProvider.GetRequiredService<TContext>();

                if (!dbContext.Database.IsSqlServer())
                    throw new InvalidOperationException("Sync Engine is only compatible with Sql Server.  Configure the DbContext with .UseSqlServer().");

                _tableNameToEntityTypeMappings = dbContext.Model.GetEntityTypes().ToDictionary(e => e.GetTableName(), e => e);

                syncEngineEntityTypes = dbContext.Model.GetEntityTypes().Where(EntityTypeExtensions.IsSyncEngineEnabled).ToList();
                
                databaseName = dbContext.Database.GetDbConnection().Database;

                if (syncOnStartup)
                {
                    _logger.LogInformation("Synchronizing changes since last run...");
                    
                    var processChangesTasks = syncEngineEntityTypes.Select(e => _changeProcessor.ProcessChangesForTable(e)).ToArray();

                    await Task.WhenAll(processChangesTasks);
                }
            }

            SqlDependencyEx.CleanDatabase(_configuration.GetConnectionString("LegacyConnection"), databaseName);

            _logger.LogInformation("Found {entityTrackingCount} Entities with Sync Engine enabled", syncEngineEntityTypes.Count);

            foreach (var entityType in syncEngineEntityTypes)
            {
                var tableName = entityType.GetTableName();

                Interlocked.Increment(ref _SqlDependencyIdentity);

                var sqlTableDependency = new SqlDependencyEx(_configuration.GetConnectionString("LegacyConnection"), databaseName, tableName, identity: _SqlDependencyIdentity);

                _logger.LogDebug("Created SqlDependency on table {tableName} with identity: {sqlDependencyId}", tableName, _SqlDependencyIdentity);

                sqlTableDependency.TableChanged += async (object sender, SqlDependencyEx.TableChangedEventArgs e) => await ProcessChangeEvent(sender, e);

                _sqlTableDependencies.Add(sqlTableDependency);

                _logger.LogInformation("Sync Engine configured for Entity: {entityTypeName}", entityType.Name);
            }

            _sqlTableDependencies.ForEach(s => s.Start());
        }

        async Task ProcessChangeEvent(object sender, SqlDependencyEx.TableChangedEventArgs e)
        {
            string? tableName = null;

            try
            {
                tableName = ((SqlDependencyEx)sender).TableName;

                using (_logger.BeginScope("DbContext: {dbContext}", typeof(TContext)))
                    _logger.LogInformation("Change detected in table: {tableName} ", tableName);

                await _tableChangedNotificationDispatcher.Dispatch(new TableChangedNotification(typeof(TContext), _tableNameToEntityTypeMappings[tableName], e.NotificationType.ToChangeOperation()), new CancellationToken());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Change Event for Table: {tableName}", tableName);
            }
        }

        public async Task Stop(CancellationToken cancellationToken)
        {
            _sqlTableDependencies.ForEach(s => s.Dispose());
        }

        public void Dispose()
        {
            //Stop().Wait();
        }
    }
}
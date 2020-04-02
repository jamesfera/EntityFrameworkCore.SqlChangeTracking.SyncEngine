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
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Options;
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
        readonly ILogger<SyncEngine<TContext>> _logger;
        readonly IServiceScopeFactory _serviceScopeFactory;
        readonly ITableChangedNotificationDispatcher _tableChangedNotificationDispatcher;
        readonly IChangeProcessor<TContext> _changeProcessor;

        readonly List<SqlDependencyEx> _sqlTableDependencies = new List<SqlDependencyEx>();
        Dictionary<string, IEntityType> _tableNameToEntityTypeMappings;

        static int _SqlDependencyIdentity = 0;

        public SyncEngine(ILogger<SyncEngine<TContext>> logger, IServiceScopeFactory serviceScopeFactory,
            ITableChangedNotificationDispatcher tableChangedNotificationDispatcher, IChangeProcessor<TContext> changeProcessor)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _tableChangedNotificationDispatcher = tableChangedNotificationDispatcher;
            _changeProcessor = changeProcessor;
        }

        public async Task Start(SyncEngineOptions options, CancellationToken cancellationToken)
        {
            try
            {
                using var serviceScope = _serviceScopeFactory.CreateScope();

                var dbContext = serviceScope.ServiceProvider.GetRequiredService<TContext>();

                if (!dbContext.Database.IsSqlServer())
                {
                    var ex = new InvalidOperationException("Sync Engine is only compatible with Sql Server.  Configure the DbContext with .UseSqlServer().");

                    _logger.LogCritical(ex, "Error during Sync Engine Initialization");

                    if (options.ThrowOnStartupException)
                        throw ex;

                    return;
                }

                _tableNameToEntityTypeMappings = dbContext.Model.GetEntityTypes().ToDictionary(e => e.GetFullTableName(), e => e);

                var syncEngineEntityTypes = dbContext.Model.GetEntityTypes().Where(EntityTypeExtensions.IsSyncEngineEnabled).ToList();

                var databaseName = dbContext.Database.GetDbConnection().Database;

                var connectionString = dbContext.Database.GetDbConnection().ConnectionString;

                serviceScope.Dispose();

                if (options.SynchronizeChangesOnStartup)
                {
                    _logger.LogInformation("Synchronizing changes since last run...");

                    var processChangesTasks = syncEngineEntityTypes.Select(e => _changeProcessor.ProcessChangesForTable(e)).ToArray();

                    await Task.WhenAll(processChangesTasks);
                }

                if (options.CleanDatabaseOnStartup)
                    SqlDependencyEx.CleanDatabase(connectionString, databaseName);

                _logger.LogInformation("Found {EntityTrackingCount} Entities with Sync Engine enabled", syncEngineEntityTypes.Count);

                foreach (var entityType in syncEngineEntityTypes)
                {
                    Interlocked.Increment(ref _SqlDependencyIdentity);

                    var sqlTableDependency = new SqlDependencyEx(connectionString, databaseName, entityType.GetTableName(), entityType.GetActualSchema(), identity: _SqlDependencyIdentity, receiveDetails: false);

                    _logger.LogDebug("Created SqlDependency on table {TableName} with identity: {SqlDependencyId}", entityType.GetFullTableName(), _SqlDependencyIdentity);

                    sqlTableDependency.TableChanged += async (object sender, SqlDependencyEx.TableChangedEventArgs e) => await ProcessChangeEvent(sender, e);

                    _sqlTableDependencies.Add(sqlTableDependency);

                    _logger.LogInformation("Sync Engine configured for Entity: {EntityTypeName} on Table: {TableName}", entityType.Name, entityType.GetFullTableName());
                }

                _sqlTableDependencies.ForEach(s => s.Start());
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Error attempting to start Sync Engine for DbContext: {DbContext}", typeof(TContext));

                if (options.ThrowOnStartupException)
                    throw;
            }
        }

        async Task ProcessChangeEvent(object sender, SqlDependencyEx.TableChangedEventArgs e)
        {
            string? tableName = null;

            try
            {
                tableName = $"{((SqlDependencyEx)sender).SchemaName}.{((SqlDependencyEx)sender).TableName}" ;
                
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
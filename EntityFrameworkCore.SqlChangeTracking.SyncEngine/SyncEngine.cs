using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Extensions;
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Monitoring;
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public class SyncEngine<TContext> : ISyncEngine<TContext> where TContext : DbContext
    {
        ILogger<SyncEngine<TContext>> _logger;
        IServiceScopeFactory _serviceScopeFactory;
        IChangeSetProcessor<TContext> _changeSetProcessor;
        IDatabaseChangeMonitor _databaseChangeMonitor;

        List<IDisposable> _changeRegistrations = new List<IDisposable>();

        public SyncEngine(IServiceScopeFactory serviceScopeFactory,
            IDatabaseChangeMonitor databaseChangeMonitor, IChangeSetProcessor<TContext> changeSetProcessor, ILogger<SyncEngine<TContext>> logger = null)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _databaseChangeMonitor = databaseChangeMonitor;
            _changeSetProcessor = changeSetProcessor;
            _logger = logger ?? NullLogger<SyncEngine<TContext>>.Instance;
        }

        public async Task Start(SyncEngineOptions options, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(options.SyncContext))
                throw new Exception("");

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

                _logger.LogInformation("Initializing Sync Engine with SyncContext: {SyncContext}", options.SyncContext);

                var syncEngineEntityTypes = dbContext.Model.GetEntityTypes().Where(e => e.IsSyncEngineEnabled()).ToList();

                var databaseName = dbContext.Database.GetDbConnection().Database;

                var connectionString = dbContext.Database.GetDbConnection().ConnectionString;

                foreach (var syncEngineEntityType in syncEngineEntityTypes)
                {
                    await dbContext.InitializeSyncEngine(syncEngineEntityType, options.SyncContext).ConfigureAwait(false);
                }

                //syncEngineEntityTypes.ForEach(async e => await dbContext.InitializeSyncEngine(e, options.SyncContext));

                serviceScope.Dispose();

                if (options.SynchronizeChangesOnStartup)
                {
                    _logger.LogInformation("Synchronizing changes since last run...");

                    var processChangesTasks = syncEngineEntityTypes.Select(e => processChanges(e, options.SyncContext)).ToArray();

                    await Task.WhenAll(processChangesTasks).ConfigureAwait(false);
                }

                _logger.LogInformation("Found {EntityTrackingCount} Entities with Sync Engine enabled for SyncContext: {SyncContext}", syncEngineEntityTypes.Count, options.SyncContext);

                foreach (var entityType in syncEngineEntityTypes)
                {
                    var changeRegistration = _databaseChangeMonitor.RegisterForChanges(o =>
                        {
                            o.TableName = entityType.GetTableName();
                            o.SchemaName = entityType.GetActualSchema();
                            o.DatabaseName = databaseName;
                            o.ConnectionString = connectionString;
                        },
                        t =>
                        {
                            _logger.LogInformation("Received Change notification for Table: {TableName}", entityType.GetFullTableName());

                            return processChanges(entityType, options.SyncContext);
                        });

                    _changeRegistrations.Add(changeRegistration);

                    _logger.LogInformation("Sync Engine configured for Entity: {EntityTypeName} on Table: {TableName}", entityType.Name, entityType.GetFullTableName());
                }
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Error attempting to start Sync Engine for DbContext: {DbContext}", typeof(TContext));

                if (options.ThrowOnStartupException)
                    throw;
            }
        }

        async Task processChanges(IEntityType entityType, string syncContext)
        {
            try
            {
                await _changeSetProcessor.ProcessChanges(entityType, syncContext).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Changes for Table: {TableName} for SyncContext: {SyncContext}", entityType.GetFullTableName(), syncContext);
            }
        }

        public Task Stop(CancellationToken cancellationToken)
        {
            _changeRegistrations.ForEach(r => r.Dispose());

            _logger.LogInformation("Shutting down Sync Engine.");

            return Task.CompletedTask;
        }
    }
}
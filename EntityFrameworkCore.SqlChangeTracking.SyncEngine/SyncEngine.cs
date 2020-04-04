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
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Monitoring;
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

        readonly IDatabaseChangeMonitor _databaseChangeMonitor;

        readonly List<IDisposable> _changeRegistrations = new List<IDisposable>();

        public SyncEngine(ILogger<SyncEngine<TContext>> logger, IServiceScopeFactory serviceScopeFactory,
            IDatabaseChangeMonitor databaseChangeMonitor)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _databaseChangeMonitor = databaseChangeMonitor;
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

                var syncEngineEntityTypes = dbContext.Model.GetEntityTypes().Where(e => e.IsSyncEngineEnabled()).ToList();

                var databaseName = dbContext.Database.GetDbConnection().Database;

                var connectionString = dbContext.Database.GetDbConnection().ConnectionString;

                serviceScope.Dispose();

                //if (options.SynchronizeChangesOnStartup)
                //{
                //    _logger.LogInformation("Synchronizing changes since last run...");

                //    var processChangesTasks = syncEngineEntityTypes.Select(e => _changeProcessor.ProcessChangesForTable(e)).ToArray();

                //    await Task.WhenAll(processChangesTasks);
                //}


                _logger.LogInformation("Found {EntityTrackingCount} Entities with Sync Engine enabled", syncEngineEntityTypes.Count);

                foreach (var entityType in syncEngineEntityTypes)
                {
                    var changeRegistration = _databaseChangeMonitor.RegisterForChanges(o =>
                    {
                        o.TableName = entityType.GetTableName();
                        o.SchemaName = entityType.GetActualSchema();
                        o.DatabaseName = databaseName;
                        o.ConnectionString = connectionString;
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

        public async Task Stop(CancellationToken cancellationToken)
        {
            _changeRegistrations.ForEach(r => r.Dispose());
        }
    }
}
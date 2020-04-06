using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Extensions;
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Utils;
using Microsoft.Extensions.Logging;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine.Monitoring
{
    public interface IDatabaseChangeMonitor
    {
        IDisposable RegisterForChanges(Action<DatabaseChangeMonitorRegistrationOptions> optionsBuilder, Func<ITableChangedNotification, Task> changeEventHandler);
    }

    public class DatabaseChangeMonitor : IDatabaseChangeMonitor, IDisposable
    {
        ILogger<DatabaseChangeMonitor> _logger;

        static int SqlDependencyIdentity = 0;

        ConcurrentDictionary<string, SqlDependencyEx> _sqlDependencies = new ConcurrentDictionary<string, SqlDependencyEx>();
        ConcurrentDictionary<string, ImmutableList<ChangeRegistration>> _registeredChangeActions = new ConcurrentDictionary<string, ImmutableList<ChangeRegistration>>();

        SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public DatabaseChangeMonitor(ILogger<DatabaseChangeMonitor> logger)
        {
            _logger = logger;


            //if (options.CleanDatabaseOnStartup)
            //SqlDependencyEx.CleanDatabase(connectionString, databaseName);
        }

        public IDisposable RegisterForChanges(Action<DatabaseChangeMonitorRegistrationOptions> optionsBuilder, Func<ITableChangedNotification, Task> changeEventHandler)
        {
            var options = new DatabaseChangeMonitorRegistrationOptions();

            optionsBuilder.Invoke(options);

            var registrationKey = $"{options.DatabaseName}.{options.SchemaName}.{options.TableName}";

            var registration = new ChangeRegistration(registrationKey, _registeredChangeActions, changeEventHandler);

            _registeredChangeActions.AddOrUpdate(registrationKey, new List<ChangeRegistration>(new[] { registration }).ToImmutableList(), (k, r) => r.Add(registration));

            _sqlDependencies.GetOrAdd(registrationKey, k =>
            {
                var id = Interlocked.Increment(ref SqlDependencyIdentity);

                var fullTableName = $"{options.SchemaName}.{options.TableName}";

                var sqlTableDependency = new SqlDependencyEx(options.ConnectionString, options.DatabaseName, options.TableName, options.SchemaName, identity: SqlDependencyIdentity, receiveDetails: false);

                _logger.LogInformation("Created Change Event Listener on table {TableName} with identity: {SqlDependencyId}", fullTableName, id);

                sqlTableDependency.TableChanged += async (sender, e) => await TableChangedEventHandler(sender, e);

                sqlTableDependency.NotificationProcessStopped += (sender, args) => _logger.LogInformation("Terminated: Change Event Listener on table {TableName} with identity: {SqlDependencyId}", fullTableName, id);

                sqlTableDependency.Start();

                return sqlTableDependency;
            });

            return registration;
        }

        async Task TableChangedEventHandler(object sender, SqlDependencyEx.TableChangedEventArgs e)
        {
            string? tableName = null;

            //BlockingCollection<ITableChangedNotification> notificationQueue = new BlockingCollection<ITableChangedNotification>();

            //notificationQueue.

            try
            {
                var sqlEx = (SqlDependencyEx) sender;

                tableName = $"{sqlEx.SchemaName}.{sqlEx.TableName}";

                _logger.LogInformation("Change detected in table: {TableName} ", tableName);

                var registrationKey = $"{sqlEx.DatabaseName}.{sqlEx.SchemaName}.{sqlEx.TableName}";

                if (_registeredChangeActions.TryGetValue(registrationKey, out ImmutableList<ChangeRegistration> actions))
                {
                    //await _semaphore.WaitAsync();
                    
                    var tasks = actions.Select(async a =>
                    {
                        var notification = new TableChangedNotification(sqlEx.DatabaseName, sqlEx.TableName, sqlEx.SchemaName, e.NotificationType.ToChangeOperation());

                        try
                        {
                            await a.ChangeFunc(notification);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error handling notification: {TableChangedNotification} Handler: {NotificationHandler}", notification, a.GetType().PrettyName());
                        }
                    });

                    try
                    {
                        await Task.WhenAll(tasks);
                    }
                    finally
                    {
                        //_semaphore.Release();
                    }
                }
                else //this should never happen
                {
                    _logger.LogWarning("Log a warning here");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Change Event for Table: {TableName}", tableName);
            }
        }

        public void Dispose()
        {
            _sqlDependencies?.Values.ToList().ForEach(d => d.Dispose());
            _sqlDependencies?.Clear();
        }

        class ChangeRegistration : IDisposable
        {
            ConcurrentDictionary<string, ImmutableList<ChangeRegistration>> _registeredChangeActions;
            string _registrationKey;

            public Func<ITableChangedNotification, Task> ChangeFunc { get; }

            public ChangeRegistration(string registrationKey, ConcurrentDictionary<string, ImmutableList<ChangeRegistration>> registeredChangeActions, Func<ITableChangedNotification, Task> changeFunc)
            {
                _registrationKey = registrationKey;
                _registeredChangeActions = registeredChangeActions;
                ChangeFunc = changeFunc;
            }

            public void Dispose()
            {
                _registeredChangeActions.AddOrUpdate(_registrationKey, new List<ChangeRegistration>().ToImmutableList(), (k, l) => l.Remove(this));
            }
        }
    }

    public class DatabaseChangeMonitorRegistrationOptions
    {
        public string TableName { get; set; }
        public string SchemaName { get; set; }
        public string DatabaseName { get; set; }
        public string ConnectionString { get; set; }
    }

    
}

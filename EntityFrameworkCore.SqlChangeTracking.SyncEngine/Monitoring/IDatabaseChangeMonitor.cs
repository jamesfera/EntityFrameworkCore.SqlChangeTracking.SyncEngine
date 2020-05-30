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
        void Enable();
        void Disable();
    }

    public class DatabaseChangeMonitor : IDatabaseChangeMonitor, IAsyncDisposable
    {
        ILogger<DatabaseChangeMonitor> _logger;
        ILoggerFactory _loggerFactory;

        static int SqlDependencyIdentity = 0;

        ConcurrentDictionary<string, SqlDependencyEx> _sqlDependencies = new ConcurrentDictionary<string, SqlDependencyEx>();
        ConcurrentDictionary<string, ImmutableList<ChangeRegistration>> _registeredChangeActions = new ConcurrentDictionary<string, ImmutableList<ChangeRegistration>>();

        CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        ImmutableList<Task> _notificationTasks = ImmutableList<Task>.Empty;

        bool _enabled = true;

        public DatabaseChangeMonitor(ILogger<DatabaseChangeMonitor> logger, ILoggerFactory loggerFactory)
        {
            _logger = logger;
            _loggerFactory = loggerFactory;
        }

        static SemaphoreSlim _registrationSemaphore = new SemaphoreSlim(1,1);

        public IDisposable RegisterForChanges(Action<DatabaseChangeMonitorRegistrationOptions> optionsBuilder, Func<ITableChangedNotification, Task> changeEventHandler)
        {
            try
            {
                _registrationSemaphore.Wait();

                var options = new DatabaseChangeMonitorRegistrationOptions();

                optionsBuilder.Invoke(options);

                var registrationKey = $"{options.DatabaseName}.{options.SchemaName}.{options.TableName}";

                var registration = new ChangeRegistration(registrationKey, _registeredChangeActions, changeEventHandler);

                _registeredChangeActions.AddOrUpdate(registrationKey, new List<ChangeRegistration>(new[] {registration}).ToImmutableList(), (k, r) => r.Add(registration));

                _sqlDependencies.GetOrAdd(registrationKey, k =>
                {
                    var id = Interlocked.Increment(ref SqlDependencyIdentity);

                    var fullTableName = $"{options.SchemaName}.{options.TableName}";

                    var sqlTableDependency = new SqlDependencyEx(_loggerFactory.CreateLogger<SqlDependencyEx>(), options.ConnectionString, options.DatabaseName, options.TableName, options.SchemaName, identity: SqlDependencyIdentity, receiveDetails: true);

                    var notificationTask = sqlTableDependency.Start(TableChangedEventHandler, (sqlEx, ex) =>
                    {
                        if (ex == null)
                            _logger.LogInformation("Table change listener terminated for table: {TableName} database: {DatabaseName}", fullTableName, options.DatabaseName);
                        else
                            _logger.LogError(ex, "Table change listener terminated for table: {TableName} database: {DatabaseName}", fullTableName, options.DatabaseName);
                    }, _cancellationTokenSource.Token).Result;

                    _logger.LogInformation("Created Change Event Listener on table {TableName} with identity: {SqlDependencyId}", fullTableName, id);

                    _notificationTasks = _notificationTasks.Add(notificationTask);

                    return sqlTableDependency;
                });

                return registration;
            }
            finally
            {
                _registrationSemaphore.Release();
            }
        }

        public void Enable()
        {
            _enabled = true;
        }

        public void Disable()
        {
            _enabled = false;
        }

        async Task TableChangedEventHandler(SqlDependencyEx sqlEx, SqlDependencyEx.TableChangedEventArgs e)
        {
            string? tableName = null;

            try
            {
                tableName = $"{sqlEx.SchemaName}.{sqlEx.TableName}";

                if (!_enabled)
                {
                    _logger.LogDebug("Database Change Monitor disabled.  Skipping change notification for table: {TableName}", tableName);
                    return;
                }

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

        bool _disposed = false;
        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;

                _cancellationTokenSource.Cancel();

                foreach (var sqlDependencyEx in _sqlDependencies.Values)
                {
                    await sqlDependencyEx.DisposeAsync().ConfigureAwait(false);
                }

                //_sqlDependencies?.Clear();

                _cancellationTokenSource.Dispose();
            }
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

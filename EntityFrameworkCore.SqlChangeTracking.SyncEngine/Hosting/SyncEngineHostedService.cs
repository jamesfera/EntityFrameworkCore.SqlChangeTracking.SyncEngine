using System;
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public class SyncEngineHostedService<TContext> : IHostedService where TContext : DbContext
    {
        readonly ISyncEngine<TContext> _syncEngine;
        SyncEngineOptions _syncEngineOptions;

        public SyncEngineHostedService(ISyncEngine<TContext> syncEngine, SyncEngineOptions syncEngineOptions)
        {
            _syncEngine = syncEngine;
            _syncEngineOptions = syncEngineOptions;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _syncEngine.Start(_syncEngineOptions, cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return _syncEngine.Stop(cancellationToken);
        }
    }
}

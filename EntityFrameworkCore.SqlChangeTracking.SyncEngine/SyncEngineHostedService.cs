using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public class SyncEngineHostedService<TContext> : IHostedService where TContext : DbContext
    {
        ISyncEngine<TContext> _syncEngine;

        public SyncEngineHostedService(ISyncEngine<TContext> syncEngine)
        {
            _syncEngine = syncEngine;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _syncEngine.Start(true, cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return _syncEngine.Stop(cancellationToken);
        }
    }
}

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public interface ISyncEngine<TDbContext> : IDisposable where TDbContext : DbContext
    {
        Task Start(bool syncOnStartup, CancellationToken cancellationToken);
        Task Stop(CancellationToken cancellationToken);
    }
}

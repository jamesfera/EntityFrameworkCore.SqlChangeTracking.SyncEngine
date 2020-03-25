using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public class ChangeSetProcessorContext<TDbContext> : IDisposable where TDbContext : DbContext
    {
        internal ChangeSetProcessorContext(TDbContext dbContext) => DbContext = dbContext;
        public TDbContext DbContext { get; private set; }

        internal bool RecordCurrentVersion { get; set; } = true;

        public void SkipRecordCurrentVersion()
        {
            RecordCurrentVersion = false;
        }

        public void Dispose()
        {
        }
    }
}
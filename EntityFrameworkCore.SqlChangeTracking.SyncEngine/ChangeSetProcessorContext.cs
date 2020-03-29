using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public class ChangeSetProcessorContext<TContext> : IDisposable where TContext : DbContext
    {
        internal ChangeSetProcessorContext(TContext dbContext) => DbContext = dbContext;
        public TContext DbContext { get; private set; }

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
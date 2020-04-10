using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public interface IChangeSetProcessorContext<TContext> where TContext : DbContext
    {
        TContext DbContext { get; }
        string SyncContext { get; }
        bool RecordCurrentVersion { get; }

        void Dispose();
        void SkipRecordCurrentVersion();
    }

    public class ChangeSetProcessorContext<TContext> : IDisposable, IChangeSetProcessorContext<TContext> where TContext : DbContext
    {
        internal ChangeSetProcessorContext(TContext dbContext, string syncContext)
        {
            DbContext = dbContext;
            SyncContext = syncContext;
        }

        public TContext DbContext { get; }
        public string SyncContext { get; }

        public bool RecordCurrentVersion { get; internal set; } = true;

        public void SkipRecordCurrentVersion()
        {
            RecordCurrentVersion = false;
        }

        public void Dispose()
        {
        }
    }
}
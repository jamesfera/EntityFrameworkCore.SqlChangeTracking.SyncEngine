namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine.Options
{
    public class SyncEngineOptions
    {
        public bool SynchronizeChangesOnStartup { get; set; } = true;
        public bool CleanDatabaseOnStartup { get; set; } = true;
        public bool ThrowOnStartupException { get; set; } = false;
    }
}

using System.ComponentModel.DataAnnotations;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine.Models
{
    public class LastSyncedChangeVersion
    {
        [Key]
        public string TableName { get; private set; }
        public long LastSyncedVersion { get; set; }
    }
}

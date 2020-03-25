using System;
using System.Collections.Generic;
using System.Text;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine.Models
{
    public class LastSyncedChangeVersion
    {
        public string TableName { get; private set; }
        public long LastSyncedVersion { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;


namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    public static class EntityTypeExtensions
    {
        public static bool IsSyncEngineEnabled(this IEntityType entityType)
            => entityType[SyncEngineAnnotationNames.Enabled] as bool? ?? false;

        public static void SetSyncEngineEnabled(this IMutableEntityType entityType, bool enabled)
            => entityType.SetOrRemoveAnnotation(SyncEngineAnnotationNames.Enabled, enabled);
    }
}

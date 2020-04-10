using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine
{
    internal static class TypeExtensionsInternal
    {
        public static string PrettyName(this Type type)
        {
            if (type.IsGenericType)
                return $"{type.FullName.Substring(0, type.FullName.LastIndexOf("`", StringComparison.InvariantCulture))}<{string.Join(", ", type.GetGenericArguments().Select(PrettyName))}>";

            return type.FullName;
        }
    }
}

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine.Extensions
{
    public static class TypeExtensions
    {
        static Type ProcessorInterfaceType = typeof(IChangeSetBatchProcessor<,>);

        public static bool IsChangeProcessorForType<TContext>(this Type type, Type processorType) where TContext : DbContext
        {
            return type.IsChangeProcessor<TContext>() && type.GetTypeForChangeProcessor<TContext>() == processorType;
        }

        public static bool IsChangeProcessor<TContext>(this Type type) where TContext : DbContext
        {
            return type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == ProcessorInterfaceType && i.GenericTypeArguments[1] == typeof(TContext));
        }

        public static Type? GetTypeForChangeProcessor<TContext>(this Type changeProcessorType) where TContext : DbContext
        {
            if (!changeProcessorType.IsChangeProcessor<TContext>())
                return null;

            return changeProcessorType.GetChangeProcessorInterface<TContext>()?.GetGenericArguments()[0];
        }

        public static Type? GetChangeProcessorInterface<TContext>(this Type changeProcessorType) where TContext : DbContext
        {
            return changeProcessorType.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == ProcessorInterfaceType);
        }
    }
}

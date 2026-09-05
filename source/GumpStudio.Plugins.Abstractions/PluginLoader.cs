using System.Reflection;
using System.Runtime.Loader;

namespace GumpStudio.Plugins;

/// <summary>A plugin that was found on disk, whether or not it loaded.</summary>
/// <param name="Info">What the plugin reports about itself.</param>
/// <param name="AssemblyPath">Where it was found.</param>
/// <param name="Instance">The instance, or null when it failed to load.</param>
/// <param name="Error">Why it failed, when it did.</param>
public sealed record DiscoveredPlugin(
    PluginInfo Info,
    string AssemblyPath,
    IGumpStudioPlugin? Instance,
    string? Error)
{
    public bool IsUsable => Instance is not null;
}

/// <summary>
/// Finds and loads plugin assemblies.
/// </summary>
/// <remarks>
/// <para>
/// Each plugin gets its own collectible <see cref="AssemblyLoadContext"/>, so it
/// can actually be unloaded. The original used <c>Assembly.LoadFile</c> into the
/// default context on .NET Framework, which is why its plugin manager told the
/// user to restart the application after any change.
/// </para>
/// <para>
/// A plugin that throws while loading is reported and skipped. The original
/// enumerated types outside its own try block, so one bad assembly aborted
/// discovery for every plugin after it.
/// </para>
/// </remarks>
public sealed class PluginLoader : IDisposable
{
    private readonly List<PluginLoadContext> _contexts = [];

    /// <summary>Scans a directory for plugin assemblies.</summary>
    /// <param name="directory">Folder to scan. A missing folder yields nothing.</param>
    public IReadOnlyList<DiscoveredPlugin> Discover(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        if (!Directory.Exists(directory))
        {
            return [];
        }

        List<DiscoveredPlugin> found = [];

        foreach (string path in Directory.EnumerateFiles(directory, "*.dll"))
        {
            found.AddRange(Load(path));
        }

        return found;
    }

    /// <summary>Loads every plugin in one assembly.</summary>
    public IReadOnlyList<DiscoveredPlugin> Load(string assemblyPath)
    {
        ArgumentNullException.ThrowIfNull(assemblyPath);

        PluginLoadContext context = new(assemblyPath);
        List<DiscoveredPlugin> found = [];

        try
        {
            Assembly assembly = context.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));

            foreach (Type type in GetPluginTypes(assembly))
            {
                found.Add(Instantiate(type, assemblyPath));
            }
        }
        catch (BadImageFormatException)
        {
            // A native DLL sitting next to the plugins is not an error worth
            // reporting; it simply is not a plugin.
            context.Unload();

            return [];
        }
        catch (FileLoadException ex)
        {
            context.Unload();

            return [Failed(assemblyPath, ex.Message)];
        }

        if (found.Count == 0)
        {
            context.Unload();
        }
        else
        {
            _contexts.Add(context);
        }

        return found;
    }

    private static IEnumerable<Type> GetPluginTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes().Where(IsPluginType);
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Some types failed to load; use the ones that did rather than
            // discarding the whole assembly.
            return ex.Types.OfType<Type>().Where(IsPluginType);
        }
    }

    private static bool IsPluginType(Type type) =>
        typeof(IGumpStudioPlugin).IsAssignableFrom(type)
        && type is { IsAbstract: false, IsInterface: false }
        && type.GetConstructor(Type.EmptyTypes) is not null;

    private static DiscoveredPlugin Instantiate(Type type, string assemblyPath)
    {
        try
        {
            IGumpStudioPlugin plugin = (IGumpStudioPlugin)Activator.CreateInstance(type)!;

            return new DiscoveredPlugin(plugin.Info, assemblyPath, plugin, null);
        }
        catch (TargetInvocationException ex)
        {
            return Failed(assemblyPath, ex.InnerException?.Message ?? ex.Message, type);
        }
        catch (MissingMethodException ex)
        {
            return Failed(assemblyPath, ex.Message, type);
        }
    }

    private static DiscoveredPlugin Failed(string assemblyPath, string error, Type? type = null) =>
        new(
            new PluginInfo(
                type?.FullName ?? Path.GetFileNameWithoutExtension(assemblyPath),
                type?.Name ?? Path.GetFileName(assemblyPath)),
            assemblyPath,
            null,
            error);

    /// <summary>Unloads every plugin assembly.</summary>
    public void Dispose()
    {
        foreach (PluginLoadContext context in _contexts)
        {
            context.Unload();
        }

        _contexts.Clear();
    }

    /// <summary>
    /// An isolated, unloadable context for one plugin assembly.
    /// </summary>
    /// <remarks>
    /// Dependencies are resolved next to the plugin, but types the host already
    /// defines — the plugin contracts and the core model — deliberately fall
    /// through to the default context, or the plugin's exporter would not be the
    /// same type the host is looking for.
    /// </remarks>
    private sealed class PluginLoadContext(string assemblyPath)
        : AssemblyLoadContext(Path.GetFileNameWithoutExtension(assemblyPath), isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(assemblyPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Returning null delegates to the default context, which is what
            // keeps shared contract types identical on both sides.
            if (Default.Assemblies.Any(a => a.GetName().Name == assemblyName.Name))
            {
                return null;
            }

            string? path = _resolver.ResolveAssemblyToPath(assemblyName);

            return path is null ? null : LoadFromAssemblyPath(path);
        }
    }
}

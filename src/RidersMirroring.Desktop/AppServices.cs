namespace Riders.Mirroring.Desktop;

/// <summary>
/// Tiny service locator used to expose app-wide singletons
/// (<see cref="Theming.IThemeService"/>, future loggers, etc.) without
/// pulling in a full DI container. Created in <see cref="App.OnStartup"/>
/// and queried by child views / view-models.
/// </summary>
/// <remarks>
/// We keep this in-process on purpose: the dependency graph is small and
/// the tests construct the view-models directly with their dependencies
/// rather than going through this locator.
/// </remarks>
public static class AppServices
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, object> _services = new();

    /// <summary>Register a singleton instance. Overwrites any previous one.</summary>
    public static void Register<T>(T instance)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        _services[typeof(T)] = instance;
    }

    /// <summary>Resolve a previously-registered service, or <c>null</c>.</summary>
    public static T? Resolve<T>()
        where T : class
    {
        return _services.TryGetValue(typeof(T), out var instance) ? instance as T : null;
    }

    /// <summary>Resolve a previously-registered service, throwing if missing.</summary>
    public static T Require<T>()
        where T : class
    {
        return Resolve<T>()
            ?? throw new InvalidOperationException(
                $"Service '{typeof(T).FullName}' was not registered. " +
                "App.OnStartup should set it up.");
    }

    /// <summary>
    /// Drop every registered service. Intended for tests so each test class
    /// starts with a clean slate; the AppServices locator is otherwise
    /// process-wide.
    /// </summary>
    public static void Reset() => _services.Clear();
}
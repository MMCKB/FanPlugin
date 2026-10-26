using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace ClassIsland.FanControl;

/// <summary>
/// 插件依赖解析器。
/// <para>
/// ClassIsland 使用 <see cref="AssemblyDependencyResolver"/> 按 deps.json 中记录的相对路径
/// （例如 <c>runtimes/win-x64/lib/net8.0/LibreHardwareMonitorLib.dll</c>）来加载插件依赖，
/// 而插件包是把所有 DLL 平铺在插件目录里的，因此随包依赖会找不到。
/// </para>
/// <para>
/// 这里在程序集加载时（ModuleInitializer）给当前 AssemblyLoadContext 挂一个 Resolving 回调，
/// 按“插件目录/程序集名.dll”兜底加载。
/// </para>
/// </summary>
internal static class PluginDependencyResolver
{
    private static string? _pluginFolder;

    [ModuleInitializer]
    internal static void Install()
    {
        try
        {
            var assembly = typeof(PluginDependencyResolver).Assembly;
            var context = AssemblyLoadContext.GetLoadContext(assembly);
            if (context == null)
            {
                return;
            }

            _pluginFolder = GetPluginFolder(assembly);
            if (string.IsNullOrEmpty(_pluginFolder))
            {
                return;
            }

            context.Resolving += Resolve;
        }
        catch
        {
            // 安装失败不应影响插件加载，最多是依赖找不到。
        }
    }

    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        if (string.IsNullOrEmpty(name.Name) || string.IsNullOrEmpty(_pluginFolder))
        {
            return null;
        }

        // 注意：这里绝不能调用 Assembly.Load(name) —— 它会再次触发本 ALC 的 Resolving，
        // 造成无限递归（Stack overflow）。插件目录里没有的程序集（ClassIsland.Core、
        // Avalonia、Xaml.Behaviors 等）交给宿主的加载器自行回退解析。
        var path = Path.Combine(_pluginFolder, name.Name + ".dll");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return context.LoadFromAssemblyPath(path);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetPluginFolder(Assembly assembly)
    {
        try
        {
            var location = assembly.Location;
            if (!string.IsNullOrEmpty(location))
            {
                return Path.GetDirectoryName(location);
            }
        }
        catch
        {
            // 动态加载的程序集可能没有 Location。
        }

        return AppContext.BaseDirectory;
    }
}

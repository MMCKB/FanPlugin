using Microsoft.Win32;
using System.Diagnostics;

namespace ClassIsland.FanControl.Services;

/// <summary>PawnIO 的检测来源。</summary>
public enum PawnIoSource
{
    None,
    Registry,
    DriverService,
    CustomPath,
    PathEnvironment,
    DefaultDirectory
}

/// <summary>PawnIO 驱动的检测结果。</summary>
public sealed class PawnIoStatus
{
    public bool IsInstalled { get; init; }

    public PawnIoSource Source { get; init; }

    /// <summary>版本号（来自卸载项），未知时为 null。</summary>
    public string? Version { get; init; }

    /// <summary>检测到的安装目录，未知时为 null。</summary>
    public string? Directory { get; init; }

    public string Summary => Source switch
    {
        PawnIoSource.Registry => $"已安装{(Version is { Length: > 0 } v ? " · v" + v : "")}",
        PawnIoSource.DriverService => "已安装 · 检测到驱动服务",
        PawnIoSource.CustomPath => "已安装 · 自定义路径",
        PawnIoSource.PathEnvironment => "已安装 · 来自 PATH",
        PawnIoSource.DefaultDirectory => "已安装 · 默认目录",
        _ => "未检测到"
    };

    public static PawnIoStatus Missing { get; } = new() { IsInstalled = false, Source = PawnIoSource.None };
}

/// <summary>
/// PawnIO 驱动定位器。
/// PawnIO 是内核驱动（设备 \Device\PawnIO），LibreHardwareMonitor 通过它读写 SuperIO。
/// 这里综合多种信号判断是否安装：LHM 的卸载注册表项、驱动服务、用户自定义路径、PATH、默认目录。
/// </summary>
public static class PawnIoLocator
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";
    private const string ServiceKey = @"SYSTEM\CurrentControlSet\Services\PawnIO";
    private static readonly string[] DriverFileNames = { "PawnIO.sys", "PawnIO.dll", "PawnIOLib.dll" };

    public static PawnIoStatus Detect(string? customPath)
    {
        // 1. LHM 自己的判定（卸载项里的 DisplayVersion），与运行期行为一致。
        try
        {
            if (LibreHardwareMonitor.PawnIo.PawnIo.Version is { } version)
            {
                return new PawnIoStatus
                {
                    IsInstalled = true,
                    Source = PawnIoSource.Registry,
                    Version = version.ToString(),
                    Directory = TryReadInstallLocation()
                };
            }
        }
        catch
        {
            // 忽略，继续用其它信号判断。
        }

        // 2. 驱动服务（手动用 INF/pnputil 安装时没有卸载项，但会有服务）。
        var serviceImage = TryReadServiceImagePath();
        if (!string.IsNullOrEmpty(serviceImage))
        {
            return new PawnIoStatus
            {
                IsInstalled = true,
                Source = PawnIoSource.DriverService,
                Directory = Path.GetDirectoryName(serviceImage)
            };
        }

        // 3. 用户自定义路径（安装到非默认位置、或便携部署的情况）。
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            var dir = ResolveDirectory(customPath);
            if (dir != null)
            {
                return new PawnIoStatus
                {
                    IsInstalled = true,
                    Source = PawnIoSource.CustomPath,
                    Directory = dir
                };
            }
        }

        // 4. 系统 / 用户 PATH 里能找到驱动文件。
        var fromPath = SearchPathEnvironment();
        if (fromPath != null)
        {
            return new PawnIoStatus
            {
                IsInstalled = true,
                Source = PawnIoSource.PathEnvironment,
                Directory = fromPath
            };
        }

        // 5. 常见默认目录。
        foreach (var candidate in DefaultDirectories())
        {
            if (ContainsDriver(candidate))
            {
                return new PawnIoStatus
                {
                    IsInstalled = true,
                    Source = PawnIoSource.DefaultDirectory,
                    Directory = candidate
                };
            }
        }

        return PawnIoStatus.Missing;
    }

    /// <summary>把目录加入当前用户的 PATH（不需要管理员权限）。返回是否成功。</summary>
    public static bool AddToUserPath(string directory)
    {
        try
        {
            var current = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
            var parts = current.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Any(x => string.Equals(x.TrimEnd('\\'), directory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            var updated = string.IsNullOrWhiteSpace(current)
                ? directory
                : current.TrimEnd(';') + ";" + directory;

            Environment.SetEnvironmentVariable("PATH", updated, EnvironmentVariableTarget.User);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>打开 PawnIO 下载页。</summary>
    public static void OpenDownloadPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://pawnio.eu/") { UseShellExecute = true });
        }
        catch
        {
            // 浏览器启动失败时忽略。
        }
    }

    /// <summary>目录（或文件）能否解析成“包含驱动的目录”。</summary>
    public static string? ResolveDirectory(string pathOrFile)
    {
        try
        {
            if (File.Exists(pathOrFile))
            {
                var dirOfFile = Path.GetDirectoryName(pathOrFile);
                return dirOfFile != null && ContainsDriver(dirOfFile) ? dirOfFile : null;
            }

            if (Directory.Exists(pathOrFile))
            {
                var full = Path.GetFullPath(pathOrFile);
                return ContainsDriver(full) ? full : null;
            }
        }
        catch
        {
            // 路径非法。
        }

        return null;
    }

    private static bool ContainsDriver(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return false;
            }

            return DriverFileNames.Any(name =>
                File.Exists(Path.Combine(directory, name)));
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> DefaultDirectories()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        if (!string.IsNullOrEmpty(programFiles))
        {
            yield return Path.Combine(programFiles, "PawnIO");
        }

        if (!string.IsNullOrEmpty(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "PawnIO");
        }

        if (!string.IsNullOrEmpty(programData))
        {
            yield return Path.Combine(programData, "PawnIO");
        }
    }

    private static string? SearchPathEnvironment()
    {
        foreach (var raw in new[]
                 {
                     Environment.GetEnvironmentVariable("PATH"),
                     Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)
                 })
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (ContainsDriver(part))
                {
                    return part;
                }
            }
        }

        return null;
    }

    private static string? TryReadInstallLocation()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(UninstallKey);
                if (key?.GetValue("InstallLocation") is string location && !string.IsNullOrWhiteSpace(location))
                {
                    return location.Trim('"');
                }
            }
            catch
            {
                // 忽略并在下一个视图重试。
            }
        }

        return null;
    }

    private static string? TryReadServiceImagePath()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(ServiceKey);
            if (key?.GetValue("ImagePath") is not string imagePath || string.IsNullOrWhiteSpace(imagePath))
            {
                return null;
            }

            var path = imagePath.Trim('"');

            // 处理 \SystemRoot\System32\drivers\PawnIO.sys 之类的写法。
            if (path.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            {
                var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                path = Path.Combine(systemRoot, path.Substring(@"\SystemRoot\".Length));
            }
            else if (path.StartsWith(@"\??\", StringComparison.Ordinal))
            {
                path = path.Substring(4);
            }

            return path;
        }
        catch
        {
            return null;
        }
    }
}

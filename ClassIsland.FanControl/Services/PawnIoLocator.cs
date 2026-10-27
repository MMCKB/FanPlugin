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

    /// <summary>检测到的驱动所在目录，未知时为 null。</summary>
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
///
/// 判定原则：<b>只有真的存在驱动文件（PawnIO.sys）才算已安装</b>。
/// 卸载后 C:\Program Files\PawnIO 里常残留 PawnIOLib.dll / uninstall.exe，
/// 注册表也可能残留卸载项或服务键，这些都不能作为“已安装”的依据。
/// </summary>
public static class PawnIoLocator
{
    private const string ServiceKey = @"SYSTEM\CurrentControlSet\Services\PawnIO";

    /// <summary>驱动文件名。只有它才是“已安装”的证据。</summary>
    private const string DriverFileName = "PawnIO.sys";

    public static PawnIoStatus Detect(string? customPath)
    {
        // 1. PnP 设备节点（最权威）：驱动装好后系统会创建 ROOT\PAWNIO\0000。
        //    PawnIO 2.x 的设备节点没有 ImagePath 服务，只能靠它判断。
        if (TryGetDeviceNode() is { } device)
        {
            var deviceVersion = TryGetRegistryVersion() ?? device.Version;
            return new PawnIoStatus
            {
                IsInstalled = true,
                Source = PawnIoSource.DriverService,
                Version = deviceVersion?.ToString(),
                Directory = device.Directory
            };
        }

        // 2. 退回到文件层面的判断（便携部署、或注册表读不到时）。
        var (directory, source) = FindDriver(customPath);
        if (directory == null)
        {
            // 找不到驱动文件：即使注册表/目录有残留，也按未安装处理。
            return PawnIoStatus.Missing;
        }

        var version = TryGetRegistryVersion();
        return new PawnIoStatus
        {
            IsInstalled = true,
            Source = version != null ? PawnIoSource.Registry : source,
            Version = version?.ToString(),
            Directory = directory
        };
    }

    /// <summary>
    /// 读取 PnP 设备节点 ROOT\PAWNIO\*，返回驱动 INF 所在目录与驱动版本。
    /// 没有设备节点则返回 null。
    /// </summary>
    private static (string? Directory, Version? Version)? TryGetDeviceNode()
    {
        const string enumPath = @"SYSTEM\CurrentControlSet\Enum\ROOT\PAWNIO";

        try
        {
            using var enumKey = Registry.LocalMachine.OpenSubKey(enumPath);
            var instances = enumKey?.GetSubKeyNames();
            if (instances is not { Length: > 0 })
            {
                return null;
            }

            string? infName = null;
            Version? version = null;

            // 形如 "{62f9c741-...}\0012"，指向 Control\Class 下的驱动键。
            using (var instanceKey = enumKey!.OpenSubKey(instances[0]))
            {
                if (instanceKey?.GetValue("Driver") is string driverRef && !string.IsNullOrWhiteSpace(driverRef))
                {
                    using var classKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\" + driverRef);
                    infName = classKey?.GetValue("InfPath") as string;
                    if (Version.TryParse(classKey?.GetValue("DriverVersion") as string, out var v))
                    {
                        version = v;
                    }
                }
            }

            var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var directory = !string.IsNullOrEmpty(infName) && !string.IsNullOrEmpty(systemRoot)
                ? Path.Combine(systemRoot, "INF")
                : null;

            return (directory, version);
        }
        catch
        {
            return null;
        }
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

    /// <summary>在目录里找驱动安装信息文件（优先 PawnIO.inf）。</summary>
    public static string? FindInfFile(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return null;
            }

            var preferred = Path.Combine(folder, "PawnIO.inf");
            if (File.Exists(preferred))
            {
                return preferred;
            }

            return Directory.GetFiles(folder, "*.inf").FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 用 pnputil 安装驱动。需要管理员权限，返回是否成功与实际输出。
    /// </summary>
    public static (bool Ok, string Output) InstallDriver(string infPath)
    {
        try
        {
            var psi = new ProcessStartInfo("pnputil")
            {
                Arguments = $"/add-driver \"{infPath}\" /install",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return (false, "无法启动 pnputil。");
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(30000);

            var output = string.Join(" ", new[] { stdout, stderr }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim()));

            var text = string.IsNullOrEmpty(output) ? "（无输出）" : output;
            var succeeded = process.HasExited &&
                            (process.ExitCode == 0 ||
                             text.Contains("已成功添加驱动程序包", StringComparison.Ordinal) ||
                             text.Contains("Successfully", StringComparison.OrdinalIgnoreCase));

            return (succeeded, text);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
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

    /// <summary>按优先级找出驱动所在目录。</summary>
    private static (string? Directory, PawnIoSource Source) FindDriver(string? customPath)
    {
        // 1. 驱动服务登记的 ImagePath（最权威：驱动真的被系统登记过）。
        var serviceImage = TryReadServiceImagePath();
        if (!string.IsNullOrEmpty(serviceImage) && File.Exists(serviceImage))
        {
            return (Path.GetDirectoryName(serviceImage), PawnIoSource.DriverService);
        }

        // 2. 用户手动指定的目录。
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            var dir = ResolveDirectory(customPath);
            if (dir != null)
            {
                return (dir, PawnIoSource.CustomPath);
            }
        }

        // 3. PATH 中的目录。
        var fromPath = SearchPathEnvironment();
        if (fromPath != null)
        {
            return (fromPath, PawnIoSource.PathEnvironment);
        }

        // 4. 系统驱动目录（含 DriverStore 的 pawnio.inf_*）。
        foreach (var candidate in DriverDirectories())
        {
            if (ContainsDriver(candidate))
            {
                return (candidate, PawnIoSource.DefaultDirectory);
            }
        }

        // 5. 常见安装目录。
        foreach (var candidate in DefaultDirectories())
        {
            if (ContainsDriver(candidate))
            {
                return (candidate, PawnIoSource.DefaultDirectory);
            }
        }

        return (null, PawnIoSource.None);
    }

    private static bool ContainsDriver(string directory)
    {
        try
        {
            return Directory.Exists(directory) && File.Exists(Path.Combine(directory, DriverFileName));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>系统驱动目录：System32\drivers 与 DriverStore\FileRepository\pawnio.inf_*。</summary>
    private static IEnumerable<string> DriverDirectories()
    {
        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrEmpty(systemRoot))
        {
            yield break;
        }

        yield return Path.Combine(systemRoot, "System32", "drivers");

        var repo = Path.Combine(systemRoot, "System32", "DriverStore", "FileRepository");
        string[] dirs;
        try
        {
            dirs = Directory.GetDirectories(repo, "pawnio.inf_*");
        }
        catch
        {
            yield break;
        }

        foreach (var dir in dirs)
        {
            yield return dir;
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

    /// <summary>卸载项里的版本号（LHM 也用它判断是否安装）。</summary>
    private static Version? TryGetRegistryVersion()
    {
        try
        {
            // 直接问 LHM，保证与运行期判定一致。
            return LibreHardwareMonitor.PawnIo.PawnIo.Version;
        }
        catch
        {
            return null;
        }
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

# 第三方组件与许可

本项目自身代码以 [AGPL-3.0](LICENSE) 许可发布。以下第三方组件由本项目引用或随插件包分发，其版权与许可归各自作者所有，本项目不改变其许可。

## 随插件分发的运行时组件

| 组件 | 版本 | 许可 | 项目地址 |
| --- | --- | --- | --- |
| LibreHardwareMonitorLib | 0.9.6 | MPL-2.0 | <https://github.com/LibreHardwareMonitor/LibreHardwareMonitor> |
| HidSharp | 2.1.0 | Apache-2.0 | <https://github.com/ZeroErrors/HidSharp> |
| DiskInfoToolkit | — | MIT | <https://github.com/LibreHardwareMonitor/LibreHardwareMonitor> 的依赖项 |
| BlackSharp.Core | — | MIT | LibreHardwareMonitor 的依赖项 |
| RAMSPDToolkit-NDD | — | MIT | LibreHardwareMonitor 的依赖项 |
| System.CodeDom / System.IO.Ports / System.Management / System.Threading.AccessControl | 8.x | MIT | .NET Runtime 的一部分，<https://github.com/dotnet/runtime> |

> **MPL-2.0 说明**：LibreHardwareMonitorLib 未做任何修改，以原样二进制形式分发。其源代码可从上述地址获取。
> **LGPL / 动态链接说明**：ClassIsland.PluginSdk 以库的形式被引用，未做修改；插件运行时会加载 ClassIsland 自带的副本，用户可自行替换。

## 编译期引用（不随包分发）

| 组件 | 版本 | 许可 | 项目地址 |
| --- | --- | --- | --- |
| ClassIsland.PluginSdk | 2.1.0.1 | LGPL-3.0-only | <https://github.com/ClassIsland/ClassIsland> |
| Avalonia | 11.3.17 | MIT | <https://github.com/AvaloniaUI/Avalonia> |
| FluentAvalonia | 2.x | MIT | <https://github.com/amwx/FluentAvalonia> |
| Xaml.Behaviors | 11.3.9.5 | MIT | <https://github.com/AvaloniaUI/Avalonia.Xaml.Behaviors> |

## 素材

| 素材 | 许可 | 来源 |
| --- | --- | --- |
| 线条风扇图标（`icon-src/fan_blue.svg`） | ISC | [Lucide](https://lucide.dev/) 的 `fan` 图标 |
| ClassIsland 应用图标（`icon-src/AppLogo@768w.png`） | 随 ClassIsland 仓库发布 | <https://github.com/ClassIsland/ClassIsland>（版权归原作者） |

插件图标由 ClassIsland 官方 Logo 与 Lucide 风扇图标合成，仅用于表明本插件运行于 ClassIsland 之上，不代表官方立场。

## 外部依赖（不随包分发）

| 组件 | 说明 |
| --- | --- |
| [PawnIO](https://pawnio.eu/) | 内核驱动，用于底层访问主板芯片。需用户自行安装，本项目不包含其任何文件。 |

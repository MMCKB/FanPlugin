<p align="center">
  <img src="icon.png" width="128" height="128" alt="风扇控制插件图标">
</p>

<h1 align="center">风扇控制</h1>

<p align="center">在 ClassIsland 2 中查看风扇实时转速，并按百分比接管风扇转速。</p>

> [!IMPORTANT]
> **本插件完全由 AI 编写。**
> 代码、界面、构建脚本与本文档均由 AI 生成，未经人工逐行审查，可能存在缺陷或疏漏。
> 其中涉及**硬件调速**，请自行评估风险后再使用；因使用本插件造成的硬件或数据问题，作者不承担责任。

> [!CAUTION]
> **部分大屏的电脑能直接调到 0 转，请谨慎使用。**
> 风扇停转可能导致 CPU / GPU 过热、降频甚至硬件损坏，请勿长时间保持 0 转。
> **出问题本人概不负责。**

## 快速开始

1. 前往 [Releases](https://github.com/MMCKB/FanPlugin/releases) 下载最新的 `ClassIsland.FanControl.cipx`。
2. 打开 ClassIsland，进入 **应用设置 → 插件**，点击安装插件并选择刚下载的 `.cipx` 文件。
3. 安装 **PawnIO** 驱动（<https://pawnio.eu/>）——读取和调节主板 / CPU 风扇需要它。
4. **以管理员身份**重新启动 ClassIsland。
5. 打开 **设置 → 风扇控制**，即可看到风扇列表、调整显示顺序，并把「接管」开关打开来手动调速。

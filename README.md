# Codex Battery

[English](docs/README.en.md) · [日本語](docs/README.ja.md)

在 Mac 菜单栏或 Windows 任务栏右下角，用一枚小电池查看 Codex 还剩多少额度。百分比写在电池里。点开菜单可以查看额度周期和重置时间，也可以让 ChatGPT 里选的 Pet 陪在电池旁边。

| 下载 | 系统 | 启动方式 |
| --- | --- | --- |
| [macOS 通用版](https://github.com/hqbingohhl/codex-battery/releases/latest/download/Codex-Battery-macOS.zip) | macOS 13 及以上，Apple Silicon 或 Intel | 解压，将 **Codex Battery.app** 拖到个人应用程序目录 `~/Applications`，双击打开 |
| [Windows 便携版](https://github.com/hqbingohhl/codex-battery/releases/latest/download/Codex-Battery-Windows.zip) | Windows 10、11，.NET Framework 4.8 或 4.8.1 | 完整解压到新文件夹，保留 Assets 目录，双击 **CodexBattery.exe** |

升级前请从旧版菜单中选择「退出」。如果是从使用早期应用标识的 Mac 版本升级，首次启动时请重新选择需要的显示偏好。macOS 下载包使用临时签名，未经 Apple 公证。Windows EXE 未使用 Authenticode 证书签名。版本说明及 SHA-256 校验文件见 [GitHub Releases](https://github.com/hqbingohhl/codex-battery/releases)。

## 使用

1. 在同一台电脑安装 Codex 并登录希望查看额度的账号。Windows 使用本机 Windows 登录的 Codex。单独登录 WSL 时，两边的登录状态彼此独立。
2. 打开 Codex Battery。它通过本机 Codex 的 `account/rateLimits/read` 接口读取账号实际提供的周期。只有周额度时，就只显示周额度。断线或读数过期时，界面会标出状态。
3. 在「设置 → 语言」里选择跟随系统、中文、日本語或 English。选择会保存在本机。英文界面的路径是 **Settings → Language**，日文是 **設定 → 言語**。

点电池可以打开菜单或进入额度窗口。Windows 菜单会从底部任务栏向上展开；如果图标折叠，可从隐藏区域拖到时钟旁。可选择最关心的周期，更改颜色、尺寸和刷新间隔。关闭额度窗口后，程序继续在菜单栏或托盘运行。

## Pet 和本机数据

两版都读取当前电脑上 ChatGPT / Codex 的 Pet 选择。自定义角色需要先在桌面端加载图像。Windows 的 EXE 图标固定使用蓝色 Codex Pet，运行时角色会跟随当前选择，缺图时也可指定自己的备用图片。分发包不包含作者个人的 Pet、缓存、账号配置或额度读数。

应用通过已经登录的本机 Codex 获取额度，界面偏好保存在本机。此项目是独立开发的桌面工具。Codex、ChatGPT 和内置 Pet 的名称及图像归其各自权利人所有。随包字体和许可见 [字体说明](windows/Assets/Fonts/README.md)。

## 从源码构建

- **macOS**　安装 Xcode Command Line Tools，在 `macos` 目录执行 `zsh build.sh`。产物为 `Codex Battery.app`。
- **Windows**　在 Windows 上从 `windows` 目录运行 `Build.cmd`，再运行 `Test.cmd` 验证程序。构建使用 .NET Framework 的 C# 编译器。

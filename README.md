# 时间戳转换器 · 原生网络图标版

基于 [QLliqin/NetworkIconSpoofController](https://github.com/QLliqin/NetworkIconSpoofController) 修改的 Windows 桌面工具。窗口、程序图标与托盘菜单均为时间戳转换器，并提供真实的 Unix 时间戳双向转换。

[下载 Windows 11 x64 程序](https://github.com/QLliqin/NetworkIconSpoofController/raw/refs/heads/main/dist/TimestampConverter.exe) · [下载完整源码](https://github.com/QLliqin/NetworkIconSpoofController/archive/refs/heads/main.zip) · [自动构建记录](https://github.com/QLliqin/NetworkIconSpoofController/actions/workflows/build.yml)

下载后将程序放在可写文件夹运行。当前版本为 3.0；本机已通过三种原生图标切换、默认恢复、正常退出及异常退出恢复测试。

## 四个选项

| 选项 | 转换时区 | 系统网络图标 |
| --- | --- | --- |
| 美国 | 纽约，自动夏令时 | 断网地球 |
| 中国 | 北京，UTC+08:00 | 网线 |
| 英国 | 伦敦，自动夏令时 | Wi-Fi |
| 默认 | 本机时区 | Windows 当前真实图标 |

美国固定采用美国东部时间。时间戳支持秒、毫秒、负数；日期输入格式为 `yyyy-MM-dd HH:mm:ss` 或 `yyyy-MM-dd HH:mm:ss.fff`。遇到夏令时跳过或重复的当地时间会提示错误。

## 原生图标的实现

3.0 版通过 Windows 的 XAML 诊断接口把内嵌组件加载到 Explorer，找到系统网络图标自己的 `TextBlock`，修改该控件实际呈现的 `Text` 字形。图标继续由 Explorer 原生绘制，并使用系统的字体、颜色、布局、悬停和鼠标交互。

没有额外覆盖窗口，也没有创建替代网络图标的应用托盘图标。应用自己的托盘图标始终是时钟。

使用原生 XAML 离散动画的属性优先级保留底层绑定，停止动画后由 Windows 当前网络状态接管。选择“默认”或从托盘退出会停止修改；控制器异常退出后，组件通过进程状态与心跳检测恢复。系统网络状态、网卡、路由、NCSI、hosts 和防火墙配置不会因新版发生变化，原生提示文字仍反映真实网络连接。

## 使用

1. 运行 `dist/TimestampConverter.exe`，无需安装或管理员权限。
2. 点击地区选项，等待原生控件确认更新；失败时界面会显示具体原因。
3. 输入时间戳或日期，点击对应的“转换”，可复制结果。
4. 选择“默认”恢复系统图标；托盘菜单“退出”结束程序。关闭或最小化窗口会收起到时钟托盘。

运行时原生组件按内容哈希释放到 EXE 同目录的 `TimestampConverter.Native/` 文件夹，程序目录需要可写。为避免 XAML 保留的 COM 指针失效，已加载 DLL 会留在 Explorer 内存中直到 Explorer 退出；程序退出后会恢复图标并停止该会话。缓存文件可在 Explorer 不再使用它们后清理。

## 兼容范围

- 目标为 Windows 11 x64 默认任务栏。Windows 10、ARM64 和第三方任务栏不在此版支持范围内。
- 不同 Windows 构建可能更换 XAML 接口或控件结构；程序必须收到原生控件的实际读回确认才能报告成功。
- XAML 诊断接口可能与其他任务栏美化、调试工具冲突。附加失败时不会回退到覆盖图标。
- 首次应用需要任务栏组件处于可用状态；Explorer 重启后请重新选择地区。

## 构建

需要 Windows、Windows PowerShell 5.1+、Windows 自带的 .NET Framework 4.x 编译器，以及 LLVM-MinGW x64 C++ 工具链。

构建使用 [LLVM-MinGW 20261020 UCRT x86_64](https://github.com/mstorsjo/llvm-mingw/releases/tag/20261020)，将工具链解压到 `obj/toolchain/` 下，或通过 `-ToolchainDirectory` 指定其目录。工具链压缩包 SHA256：

```text
FD5462B7F5D93B2FF981FCD3F9C7E427E47F3203C64EA91C27BCC444EEBD74CD
```

```powershell
.\build.ps1
.\verify.ps1
# 自定义工具链路径：
.\build.ps1 -ToolchainDirectory 'D:\tools\llvm-mingw-20261020-ucrt-x86_64'
```

构建输出为单文件 `dist/TimestampConverter.exe`，内部包含原生 DLL。脚本不自动下载或安装依赖。`verify.ps1` 检查版本、x64 架构、权限清单、内嵌 DLL、时钟图标和 71 项时间转换断言，不加载组件到 Explorer。

桌面原生图标集成测试需主动运行，测试会短暂切换三个原生图标并恢复：

```powershell
.\tools\test-native.ps1
```

集成测试独立读取 Explorer 的 UI Automation 图标值，核对三种模式及默认、退出恢复，并用专用子进程验证异常退出后的恢复。它不会由 CI 自动执行。

## 从原版迁移

原版通过 NCSI 配置影响联网状态。如果曾开启原版功能，先运行保留的 `restore.cmd` 并按提示授权恢复。新版“默认”只撤销新版的原生控件修改，不会撤销原版的注册表、hosts 或防火墙配置。

## 主要文件

| 文件 | 用途 |
| --- | --- |
| `src/Program.cs` | 转换器窗口和托盘菜单 |
| `src/TimestampConverter.cs` | 时间戳与时区转换 |
| `src/NativeNetworkIconController.cs` | 原生组件加载、命令与恢复 |
| `native/NetworkIconNative.cpp` | Explorer 原生网络图标修改 |
| `native/*_abi.h` | Windows 官方接口的必要 ABI 声明 |
| `tests/NativeNetworkIconTests.cs` | 原生控件独立读回验证 |
| `tests/TimestampConverterTests.cs` | 时间转换测试 |
| `tools/build-native.ps1` | 编译原生 DLL |
| `tools/restore.ps1` | 保留的原版 NCSI 恢复工具 |

原项目未指定开源许可证；未经版权所有者明确许可，不自动授予复制、修改或再分发权利。XAML 接口声明依据 [Microsoft Windows SDK 元数据](https://github.com/microsoft/win32metadata)；诊断接口用法参考 [Microsoft 文档](https://learn.microsoft.com/windows/win32/api/xamlom/nf-xamlom-initializexamldiagnosticsex)。

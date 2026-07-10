# Network Icon Spoof Controller

<p align="center">
  <img src="assets/app-icon.png" width="144" alt="Network Icon Spoof Controller icon">
</p>

一个适用于 Windows 10/11 的图形控制器：让任务栏网络状态显示“无 Internet”地球图标，同时保留浏览器和普通 TCP/IP 网络访问。

> [!IMPORTANT]
> 本工具会改变 Windows NCSI 的联网判断。依赖 NCSI 状态的 Microsoft Store 应用、启动器或企业软件可能认为设备离线，即使浏览器仍能访问网络。

## 功能

- 主窗口提供“开启伪装”“关闭伪装”和“刷新状态”按钮。
- 关闭或最小化窗口时隐藏到系统托盘，当前配置不会被自动改变。
- 托盘菜单可开启、关闭、查看状态，或退出并保留当前设置。
- 使用系统级 NCSI 配置，程序退出后状态仍然保留。
- 首次从正常状态开启时备份相关注册表值，关闭时优先恢复原值。
- 带独立恢复脚本，控制器无法启动时也能恢复。
- 自制 SVG/PNG/多分辨率 ICO 图标，统一用于 EXE、窗口和托盘。

## 使用

1. 从 [`dist/NetworkIconSpoofController.exe`](dist/NetworkIconSpoofController.exe) 获取程序。
2. 双击运行，并在 Windows UAC 提示中选择“是”。
3. 点击“开启伪装”或“关闭伪装”。
4. 等待约 5-30 秒，让 Windows 刷新任务栏网络图标。

托盘图标可能位于任务栏右侧的隐藏图标区域。再次运行 EXE 会唤回已经运行的控制器，不会创建第二个实例。

## 构建

环境要求：

- Windows 10/11
- Windows PowerShell 5.1
- .NET Framework 4.x（Windows 自带的 `csc.exe`）

在 PowerShell 中运行：

```powershell
.\build.ps1 -Clean
.\verify.ps1
```

也可以双击 `build.cmd`。输出位于 `dist/NetworkIconSpoofController.exe`。

图标源文件是 `assets/app-icon.svg`。如需重新生成 PNG 和 ICO，请安装 Pillow 后运行：

```powershell
python .\tools\generate_icon.py
```

## 恢复

首选方式是在控制器中点击“关闭伪装”。

如果控制器无法运行，双击 `restore.cmd` 并同意 UAC。它会：

- 恢复此电脑原有的 Microsoft NCSI 探测地址和超时设置；
- 删除本项目创建的 NCSI 策略覆盖；
- 删除带 `WiFiSpoofer_NCSI_Block` 前缀的防火墙规则；
- 删除 hosts 文件中由标记包围的项目区块；
- 刷新 DNS、ARP 和网络状态服务。

## 工作原理

开启时，控制器只针对 Windows 的联网探测：

- 把主动 Web/DNS 探测指向本机并设置为不匹配内容；
- 禁用 NCSI 被动轮询，避免普通网页流量把系统状态改回 Internet；
- 在 hosts 中拦截 Microsoft 连通性检测域名；
- 为这些域名当时解析到的地址添加仅限 TCP 80 的出站阻断规则；
- 刷新 `NlaSvc` 和 `netprofm`。

它不会关闭网卡、修改默认路由或阻断普通 HTTPS 网站。

## 项目结构

```text
.
|-- assets/                         图标源文件和生成产物
|-- dist/                           已构建的 Windows EXE
|-- src/Program.cs                  控制器源码
|-- src/app.manifest                requireAdministrator 清单
|-- tools/generate_icon.py          图标生成器
|-- tools/restore.ps1               独立恢复工具
|-- build.ps1 / build.cmd           本地构建入口
|-- verify.ps1                      只读发布验证
`-- .github/workflows/build.yml     GitHub Actions 构建
```

## 已知限制

- Windows 可能缓存任务栏图标；配置变化后通常需要等待数秒，偶尔需要断开并重连 Wi-Fi。
- 公司或学校设备上的组策略可能覆盖本工具设置。
- 发布 EXE 未使用商业代码签名证书，首次下载运行时 Windows SmartScreen 可能显示来源未知提示。
- 当前恢复默认值针对本项目开发和验证所用的 Windows 10/11 NCSI 配置；首次正常开启时生成的备份具有更高优先级。

本项目未指定开源许可证。未经版权所有者明确许可，不自动授予复制、修改或再分发权利。

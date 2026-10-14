# Deskworlds

[English](README.md) | 中文

[![观看河床演示](docs/images/riverscape.gif)](docs/videos/riverscape.mp4)

Deskworlds 在你的桌面上放一个小小的、活着的 3D 世界，支持 Mac 与 Windows。每个世界里的生物都会回应你的鼠标指针，还可以从菜单栏或托盘投喂它们。一切都在本地以 Three.js 和 WebGL2 运行，没有账号、没有统计上报、不需要联网。

## 世界

| | |
| --- | --- |
| ![河床](docs/images/riverscape-wide.png) **河床** | ![珊瑚礁](docs/images/reefscape-wide.png) **珊瑚礁** |
| ![斗鱼](docs/images/bettascape-wide.png) **斗鱼** | ![等离子球](docs/images/plasmascape-wide.png) **等离子球** |
| ![锦鲤池](docs/images/koiscape-wide.png) **锦鲤池** | ![篝火](docs/images/bonfirescape-wide.png) **篝火** |

## 在 Mac 上安装

需要 macOS 13 或更高版本，以及 Xcode 命令行工具（`xcode-select --install`）。克隆本仓库后，在项目根目录执行：

```sh
sh wallpaper/install.sh
```

脚本会构建应用、安装到 `~/Applications/Deskworlds.app` 并添加登录项。第一帧大约 20 秒后出现。你的桌面图片不会被更改；世界绘制在它上面。

更新时拉取最新源码重新运行安装脚本即可。卸载执行 `sh wallpaper/uninstall.sh`。

## 在 Windows 上安装

### 方式一

 可以直接从网盘下载编译好的安装包

```
我用夸克网盘给你分享了「Deskworlds.zip」，点击链接或复制整段内容，打开「夸克网盘APP」即可获取。
/~b7e93bRkwG~:/
链接：https://pan.quark.cn/s/d1206f842f40
```

也可以关注公众号，最新安装包会在公众号发布。

解压后双击 `Deskworlds.exe` 即可运行，。

<p align="center">
  <img src="docs/images/公众号二维码.jpg" width="240" alt="Deskworlds 公众号二维码"><br>
  <sub>微信扫码关注公众号，获取最新版安装包</sub>
</p>
### 方式二

也可以自行构建，需要安装 .NET 8 SDK，然后执行：

```powershell
powershell -ExecutionPolicy Bypass -File windows/publish.ps1
```

这会产出自包含的 `dist\` 文件夹（约 92 MB）：一个内嵌 .NET 运行时的 `Deskworlds.exe`，加上各个世界。卸载时从托盘退出并删除文件夹即可；设置保存在 `HKCU\Software\Deskworlds`，日志在 `%LOCALAPPDATA%\Deskworlds\log.txt`。想开机自启，就把 `Deskworlds.exe` 的快捷方式放进 `shell:startup`。

## 使用

在 Mac 上，点击菜单栏中的 Deskworlds 图标即可切换世界、投喂生物、拨弄火焰或暂停。在 Windows 上，同样的功能在托盘图标的右键菜单里：「世界」切换世界，「投喂」投喂（篝火为「拨弄火焰」），「暂停」暂停，「被覆盖时动画」让壁纸在被窗口遮挡时保持动画，「退出」退出。壁纸会在每一块连接的显示器上运行；桌面图标和点击一切如常。

在等离子球世界里，双击桌面会有一道闪电劈在你点击的位置——双击本身照常生效，该打开图标就打开图标。

壁纸在电池供电或窗口遮挡大部分桌面时降低帧率；暂停、锁屏或显示器关闭时停止。Windows 上的「被覆盖时动画」（默认开启）让壁纸在被窗口遮挡时保持较低帧率而不是停止——录屏时特别有用。为此应用会读取鼠标位置和窗口尺寸，但不记录按键，也不申请任何特殊权限。

## 在浏览器中运行

所有世界也可以在任何支持 WebGL2 的浏览器中运行。需要 Node.js 20 或更高版本：

```sh
npm start
```

然后打开 http://127.0.0.1:8080。点击场景即可投喂。空格暂停，F 切换全屏，H 隐藏控制栏。

## 开发

场景部分没有任何依赖需要安装。每个世界位于 `scenes/<名称>/`，画廊位于 `ui/`，macOS 应用位于 `wallpaper/`，Windows 宿主位于 `windows/`——一个 C# WinForms 应用，把 WebView2 放到每块显示器的壁纸层，并根据遮挡、电池与会话状态管理帧率。

```sh
npm test
```

`npm test` 在任何 shell 下都可运行；`npm run check` 需要 POSIX shell（Windows 上可用 Git Bash）。Windows 宿主使用 .NET 8 SDK 构建：

```powershell
dotnet build windows/Deskworlds/Deskworlds.csproj -c Release
```

## 许可

[MIT](LICENSE)。Three.js 以其 [MIT 许可](vendor/THREE-LICENSE.txt)捆绑分发。

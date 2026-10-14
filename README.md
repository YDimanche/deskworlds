# Deskworlds

English | [中文](README.zh-CN.md)

[![Watch Riverbed](docs/images/riverscape.gif)](docs/videos/riverscape.mp4)

Deskworlds puts a small living 3D world on your desktop, on both Mac and Windows. The creatures in each world react to your cursor, and you can feed them from the menu bar or tray. Everything runs locally with Three.js and WebGL2, with no account, analytics or internet connection.

## Worlds

| | |
| --- | --- |
| ![Riverbed](docs/images/riverscape-wide.png) **Riverbed** | ![Coral reef](docs/images/reefscape-wide.png) **Coral reef** |
| ![Betta](docs/images/bettascape-wide.png) **Betta** | ![Plasma globe](docs/images/plasmascape-wide.png) **Plasma globe** |
| ![Koi pond](docs/images/koiscape-wide.png) **Koi pond** | ![Bonfire](docs/images/bonfirescape-wide.png) **Bonfire** |

## Install on Mac

You need macOS 13 or newer and the Xcode command line tools (`xcode-select --install`). Clone the repository, then run this from the project folder:

```sh
sh wallpaper/install.sh
```

This builds the app, installs it at `~/Applications/Deskworlds.app`, and adds a login item. The first frame takes about 20 seconds to appear. Your desktop picture is not changed; the world draws on top of it.

To update, pull the latest source and run the installer again. To remove it, run `sh wallpaper/uninstall.sh`.

## Install on Windows

### Option 1

You can download the compiled package directly from the cloud drive:

```
我用夸克网盘给你分享了「Deskworlds.zip」，点击链接或复制整段内容，打开「夸克网盘APP」即可获取。
/~b7e93bRkwG~:/
链接：https://pan.quark.cn/s/d1206f842f40
```

You can also follow the WeChat official account (公众号) — the latest package is published there.

Unzip and double-click `Deskworlds.exe` to run it.

<p align="center">
  <img src="docs/images/公众号二维码.jpg" width="240" alt="Deskworlds 公众号二维码"><br>
  <sub>微信扫码关注公众号，获取最新版安装包</sub>
</p>

### Option 2

Or build it yourself. Install the .NET 8 SDK and run:

```powershell
powershell -ExecutionPolicy Bypass -File windows/publish.ps1
```

This produces a self-contained `dist\` folder (~92 MB): one `Deskworlds.exe` with the .NET runtime embedded, plus the worlds. To remove the app, quit it from the tray and delete the folder; settings live under `HKCU\Software\Deskworlds` and the log at `%LOCALAPPDATA%\Deskworlds\log.txt`. To start with Windows, put a shortcut to `Deskworlds.exe` in `shell:startup`.

## Usage

On the Mac, click the Deskworlds icon in the menu bar to switch worlds, feed the creatures, stir the fire, or pause. On Windows, the same controls live in the tray icon's right-click menu: 世界 switches worlds, 投喂 feeds (拨弄火焰 for the Bonfire), 暂停 pauses, and 被覆盖时动画 keeps the worlds animating behind covering windows. The wallpaper runs on every connected monitor, and desktop icons and clicks work as usual.

In the Plasma globe world, double-clicking the desktop strikes a lightning bolt where you clicked — and the double-click still opens the icon or does whatever it would normally do.

The wallpaper lowers its frame rate on battery or when windows cover most of the desktop, and stops when paused, the screen is locked, or the display is off. On Windows, 被覆盖时动画 (on by default) keeps a reduced frame rate behind covering windows instead of stopping — useful while recording the screen. For this the app reads your cursor position and window sizes, but does not record keystrokes or request any special permissions.

## Run in a browser

All worlds also run in any browser with WebGL2. With Node.js 20 or newer:

```sh
npm start
```

Then open http://127.0.0.1:8080. Click a scene to drop food. Space pauses, F toggles fullscreen, and H hides the controls.

## Development

There are no dependencies to install for the scenes. Each world lives in `scenes/<name>/`, the gallery in `ui/`, the macOS app in `wallpaper/`, and the Windows host in `windows/` — a C# WinForms app that puts a WebView2 on every monitor's wallpaper layer and manages frame rate from occlusion, battery and session state.

```sh
npm test
```

`npm test` runs on any shell; `npm run check` needs a POSIX shell (Git Bash on Windows). The Windows host builds with the .NET 8 SDK:

```powershell
dotnet build windows/Deskworlds/Deskworlds.csproj -c Release
```

## License

[MIT](LICENSE). Three.js is bundled under its [MIT license](vendor/THREE-LICENSE.txt).

# UFO-TW 使用说明 / User Guide

## 中文说明

本版本在 MultiFunPlayer 中加入 UFO-TW 原生 Windows BLE 输出，支持：

- 正版 UFO-TW；
- 使用 UFO-TW 伪装协议的 ESP32 兼容设备；
- 左乳轴 `Lnip` 和右乳轴 `Rnip`；
- 视频暂停时立即将两个轴回正到 50%；
- 不需要额外启动 Intiface，也不需要先在 Windows 设置中配对。

### 使用步骤

1. 编译并运行 `MultiFunPlayer`。
2. 在输出目标区域点击 `+`，添加 `UFO-TW BLE`。
3. 点击刷新，选择 UFO-TW 或对应的 BLE 设备。
4. 根据设备固件选择 `Genuine UFO-TW`：正版设备开启，ESP32 伪装版关闭。
5. 点击连接，并加载对应的 funscript 文件。

### Funscript 文件名

将左右乳脚本放在视频同目录，并使用以下命名：

```text
视频名称.Lnip.funscript
视频名称.Rnip.funscript
```

数值含义与 UFO-TW 约定一致：`50` 为停止/中心，`0` 为反向 100%，`49` 为反向 2%，`51` 为正向 2%，`100` 为正向 100%。

输出目标中的单点滑块用于手动测试；点击 `Reset 50` 可以将左右轴恢复到 50%。视频暂停后，MFP 会立即发送中心值，并在暂停期间阻止旧的旋转指令继续输出；恢复播放后继续发送当前脚本值。

如果设备没有出现在列表中，请确认设备已开机、Windows 蓝牙已开启，并点击刷新。正版设备通常不需要在 Windows 设置中完成配对，直接由 MFP 使用原生 BLE 连接即可。

## English Guide

This version adds native Windows BLE output for UFO-TW to MultiFunPlayer. It supports:

- genuine UFO-TW devices;
- ESP32 devices running UFO-TW-compatible firmware;
- the left nipple axis `Lnip` and right nipple axis `Rnip`;
- immediate centering to 50% when the video is paused;
- direct BLE connection without Intiface and without pairing the device in Windows Settings first.

### Steps

1. Build and run `MultiFunPlayer`.
2. Click `+` in the output area and add `UFO-TW BLE`.
3. Click Refresh and select UFO-TW or the matching BLE device.
4. Set `Genuine UFO-TW` according to the firmware: enable it for the genuine device and disable it for the ESP32 compatibility firmware.
5. Connect and load the corresponding funscript files.

### Funscript file names

Place the left and right scripts next to the video and use these names:

```text
video-name.Lnip.funscript
video-name.Rnip.funscript
```

The values follow the UFO-TW convention: `50` is center/stop, `0` is 100% reverse, `49` is 2% reverse, `51` is 2% forward, and `100` is 100% forward.

The single-point sliders in the output target are for manual testing. Click `Reset 50` to center both axes. When playback is paused, MFP immediately sends the center value and suppresses the previous rotation command; playback resumes with the current script value.

If the device does not appear, make sure it is powered on, Windows Bluetooth is enabled, and press Refresh. Genuine devices normally do not need to be paired in Windows Settings; MFP connects to them directly through native BLE.

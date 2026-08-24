# UFO-TW 使用说明 / User Guide

## 中文说明

本版本为 MultiFunPlayer 增加 `Lnip`（左路）和 `Rnip`（右路），并提供两种直接输出：

- `UFO-TW BLE (StableLink v3)`：支持正版 UFO-TW 和 ESP32 兼容固件；
- `UFO-TW USB Serial`：通过 USB 数据线直接控制更新后的 ESP32-C3 兼容板。

不需要额外运行 Intiface。

### Funscript 文件名与数值

把脚本放在视频旁边并命名为：

```text
视频名称.Lnip.funscript
视频名称.Rnip.funscript
```

数值含义：`50` 停止，`0` 反转 100%，`49` 反转 2%，`51` 正转 2%，`100` 正转 100%。

### 正版 UFO-TW：BLE

1. 在输出区域点 `+`，添加 `UFO-TW BLE (StableLink v3)`。
2. 刷新并选择正版设备。
3. 将 `Genuine protocol` 设为 `True`。
4. 点击连接。

通常不需要先在 Windows 设置中配对。

### ESP32 兼容板：USB（推荐）

1. 先用配套一键刷入器安装 BLE + USB 双通道固件。
2. 用 USB 数据线连接开发板，并关闭 Thonny 和刷入器。
3. 添加 `UFO-TW USB Serial`。
4. 选择带 `ESP32` 标记的串口后连接。

ESP32-C3 打开原生 USB 串口时可能重启一次。MFP 会等待 1 秒再发送心跳；固件连续 500ms 收不到心跳会自动停止双路。

### ESP32 兼容板：BLE

1. 添加 `UFO-TW BLE (StableLink v3)`。
2. 刷新并选择 `UFO-ESP`。
3. 将 `Genuine protocol` 设为 `False`。
4. 点击连接。

USB 心跳有效时优先于 BLE；USB 超时后固件会先停止，再允许 BLE 恢复。建议 MFP 一次只连接一个 UFO 输出。

### 暂停行为

播放器暂停时，MFP 立即向两路发送停止值，并在暂停期间保持停止；恢复播放后继续发送当前脚本值。该行为适用于 MFP 支持的各类播放器，不只 MPV。

## English Guide

This build adds the `Lnip` (left) and `Rnip` (right) axes and two direct UFO-TW outputs:

- `UFO-TW BLE (StableLink v3)` for genuine UFO-TW devices and ESP32 compatibility firmware;
- `UFO-TW USB Serial` for direct USB control of an updated ESP32-C3 compatibility board.

Intiface is not required.

Place scripts next to the video as `video-name.Lnip.funscript` and `video-name.Rnip.funscript`. Value `50` stops, `0` is 100% reverse, `49` is 2% reverse, `51` is 2% forward, and `100` is 100% forward.

For a genuine UFO-TW, add the BLE output and set `Genuine protocol` to `True`. For ESP32 BLE, select `UFO-ESP` and set it to `False`.

For direct ESP32 USB control, install the dual-channel firmware, add `UFO-TW USB Serial`, and select the port marked `ESP32`. The firmware stops both motors after 500ms without USB heartbeats. USB has priority while active; BLE may resume after USB times out and the motors pass through a stopped state.

Pausing any supported player immediately stops both axes. Playback resumes with the current script values.

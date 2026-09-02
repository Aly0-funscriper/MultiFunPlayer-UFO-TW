# UFO-TW 统一连接版使用说明

本版本在 MultiFunPlayer 的输出区域提供一个原生 `UFO-TW` 输出窗口，同时支持：

- 正版 UFO-TW 的 BLE 协议；
- ESP32 兼容固件的 BLE 协议；
- ESP32 兼容固件的 USB 串口协议。

不需要额外运行 Intiface。BLE 与 USB 串口在同一窗口内选择，并保留双路测试按钮。

## Funscript 文件

将脚本放在视频旁边并命名为：

```text
视频名称.Lnip.funscript
视频名称.Rnip.funscript
```

`Lnip` 是左路，`Rnip` 是右路。数值 `50` 表示停止；`0` 到 `49` 表示反转；`51` 到 `100` 表示正转。

## 连接

在输出区域点击 `+` 并添加 `UFO-TW`：

1. 正版设备选择 BLE 与正版协议。
2. ESP32 兼容板可选择 BLE 与兼容协议，或直接选择 USB 串口。
3. 点击扫描/刷新后只显示名称或服务 UUID 符合 UFO-TW 的 BLE 设备。
4. 连接成功前会验证 GATT 服务、可写特征和停止指令，错误设备不会被保留为 UFO-TW。

BLE 扫描最多 3 秒，完整连接设有 25 秒硬超时。ESP32 USB 串口会优先显示 Espressif 设备。

## 播放与暂停

播放器暂停时只发送一次双路停止值；恢复播放后继续发送当前脚本值。该行为使用 MFP 的统一播放状态，适用于其支持的各种播放器。

## English summary

This fork adds one native `UFO-TW` output window for genuine BLE, ESP32-compatible BLE, and ESP32 USB serial. It loads `video.Lnip.funscript` and `video.Rnip.funscript`, validates BLE candidates before accepting them, limits scanning to three seconds and connection attempts to 25 seconds, and stops both outputs while any supported player is paused.

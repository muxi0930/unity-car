# 光子狐飞车 Unity 客户端

用 Unity 6000.2.10f1 打开 ElevenCity 场景后进入 Play Mode，或运行 macOS 构建。大厅默认提供离线试跑。点击「局域网连接」后填写正在运行的房间服务地址，例如 `192.168.1.10:3000`。客户端使用参考工程的 `/room` WebSocket 协议，个人道具由房间服务结算。

原版界面及 120 Hz 驾驶规则在本地 WebKit 中运行。Unity URP 渲染实际比赛的车模、赛道、摄影机和特效。Unity 客户端负责建立和关闭 WebSocket 连接。车模、赛道、Web 界面和原生插件均已导入，可离线构建。

## 从参考工程更新资源

```sh
node Tools/PhotonClient/build.mjs "/path/to/reference-workspace"
node Tools/PhotonClient/bake-worlds.mjs "/path/to/reference-workspace" "/path/to/node_modules/@napi-rs/canvas"
sh Tools/PhotonClient/build-native.sh
```

第一步使用参考工程已安装的 esbuild 和 Three.js 生成赛前界面及规则包。第二步用 Node Canvas 生成地图程序纹理、发车牌和 Unity 网格。第三步仅需在原生 WebKit 桥接修改后运行，需要 macOS Command Line Tools。Unity 内的「Photon Fox → Build macOS」生成完整客户端。

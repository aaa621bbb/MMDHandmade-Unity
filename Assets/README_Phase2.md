# Phase 2 · 女巨人城 · 踩小人（Android）

> 在 Phase 1「MMD 播放器」之上叠加的沙盒玩法，**不删 Phase 1**。Phase 1 的 `MMDPlayer`、UMT 包、
> 资源库、场景全部保留，作为可复用部件。

## 1. 一句话玩法

**玩家是一个比女巨人一根脚趾还小的角色**，在微缩城市里跑位、躲墙角；**女巨人**（你放进手机的
`.pmx` 模型）会**追你、尝试踩你**。默认被踩到=被击倒并重生附近安全点；打开**无敌模式**后被踩到
毫发无损（仅视觉/震动反馈）。沙盒式，不做强制 Game Over。

## 2. 关键设计：不放大女巨人，而是缩小世界

- **绝对不放大女巨人 `.pmx`**（放大 → Bullet 刚体/关节尺度爆炸 → 衣服乱飞、物理疯）。
- 做法：**把整个世界（城市 + 小人）缩小**到「玩家 ≈ 女巨人一根脚趾」的量级。女巨人保持 UMT 默认
  导入尺度，物理最稳。缩放由 `WorldScaler` 自动按女巨人身高估算（可关掉自动、手动设 `worldScale`）。

## 3. 运行时从「手机文件夹」导入女巨人（核心能力）

- 女巨人不是打包进 App 的，而是运行时从**用户手机上的文件夹**导入的 `.pmx`（含同目录贴图）。
- 默认扫描路径：`Application.persistentDataPath/MMDModels`（Android 上即
  `/data/user/0/<包名>/files/MMDModels`）。
  - 把 `.pmx`（含同名贴图）丢进该文件夹即可；点「换模型」后自动列出导入。
- 导入流程：读 `.pmx` 字节 → `PMXImporter` 分帧解析/构建 → 自定义 `loadTextures` 从同目录按名读贴图
  字节转 `Texture2D`（支持 PNG/JPG，TGA/BMP 走 UMT 自带解码）。**不会卡主线程**（`UMTFrameBudget` 分帧）。
- **任意更换**：随时点「换模型」，卸载当前巨人（`DisposeRuntimeData` + 销毁 root）再载入新的。
- **手机上「从任意文件夹选」**：默认扫描 `persistentDataPath/MMDModels`；若要做真正的 SAF 任意目录选择，
  实现 `IMobileFilePicker` 并 `SetExternalPicker`（见 `MobileModelPicker`）。这是待真机/接入项。

## 4. 如何把女巨人放进手机文件夹（推荐）

1. 构建安装到 Android 后，首次运行会在 `persistentDataPath` 建立 `MMDModels` 目录。
2. 把「.pmx + 同目录贴图」整体拷进该目录：
   - **adb**：`adb push 你的模型.pmx /sdcard/...` 后移动到
     `/data/user/0/<包名>/files/MMDModels/`（需授权 / 或使用设备的文件管理器，若 App 内无导出能力）。
   - **App 内**：后续若接入 `IMobileFilePicker`，可免拷贝直接在任意目录挑。
3. 回到 App 点「换模型」，列表出现即可导入。
> 说明：`/data/user/0/<pkg>/files` 在部分 Android 上用户无法直接浏览，需 adb 或 App 提供入口；
> 为此保留 `IMobileFilePicker` 扩展点接入 SAF（真机/第三方文件选择器）以便「任意文件夹」选。

## 5. 操作（Android）

| 操作 | 方式 |
|---|---|
| 移动小人 | 左半屏虚拟摇杆（推到边缘=奔跑） |
| 视角 | 右半屏拖动环绕（低角度跟随相机） |
| 换模型 | HUD「换模型」 |
| 无敌开关 | HUD「无敌」→ 开启后被踩到不死不惩罚 |
| 重生 | HUD「重生」，或默认被踩到后在安全点重生 |
| 城市重生成 | 调 `GiantCityController.RegenerateCity()`（可加按钮） |

## 6. Build Settings（Android）

- 目标平台：**Android**。Build Settings ▸ 切到 **Android**。
- **Architecture：ARM64**（UMT 原生库在 `Plugins/Android/arm64-v8a`）。
- **Scripting Backend：IL2CPP**。
- 场景入 Build：把 `Assets/Scenes/GiantCity.unity`（用
  **Tools ▸ Giant City ▸ Build GiantCity Scene** 生成）加入 Build 场景列表。
- 渲染：Built-in RP；Active Input Handling 含 **Old** 或 **Both**（游戏用 `Input`/`StandaloneInputModule`）。

## 7. 已知限制 / 待真机验证

- **模型来源依赖文件系统**：默认只扫 `persistentDataPath/MMDModels`；「任意手机文件夹」需接入
  `IMobileFilePicker`（未集成第三方 SAF 包，**待真机/接入**）。
- **贴图读取**：运行时从同目录按名读字节解码；若贴图不在同目录会缺失（模型发灰）。
- **物理**：`livePhysics=true` 时女巨人裙摆走 Bullet，**仅在 Windows/Android/Web 平台**可用；
  macOS/Linux 编辑器无物理。Android arm64 真机可用。
- **缩放数值为估算**：`WorldScaler` 按女巨人身高 × `footLengthFactor(0.15)` 推断脚趾长度；真实模型
  **必须真机实测**微调 `footLengthFactor`/`playerToFootRatio`。
- **脚掌定位按骨骼名关键词匹配**（足首/足/foot/toe 等）；若模型命名特殊可能落到"最低两块骨骼"回退，
  请真机确认踩踏是否对准脚掌，必要时在 `GiantessAnatomy` 增加关键词。
- **破坏反馈为位置/缩放动画**：无粒子/音效/物理破坏，观感朴素。
- **Android 文件选择器未集成**：未引入第三方包，属于"待接入"项；核心导入链路已在编辑器可测。

## 8. 验收：必须在 Android 真机 + 你自己的女巨人上验证

1. 用 Unity 6.6 打开工程，确认 Phase 1 场景仍能进、无编译错误。
2. **Tools ▸ Giant City ▸ Build GiantCity Scene** 生成场景 → 打开 → 加入 Build 列表。
3. 构建 Android（ARM64/IL2CPP），跑 `GiantCity` 场景。
4. 无模型：应出现「占位巨人 + 微缩城市」，摇杆能跑、视角能转；被占位巨人踩到会重生。
5. 放入 `.pmx`（含贴图）到 `persistentDataPath/MMDModels` → 点「换模型」→ 应换成你的女巨人。
6. 女巨人追你、走近抬脚踩、踩中判定、被踩落地；开「无敌」后再踩到不重生。
7. 观察物理：裙摆/头发自然摆动，不飘出体外、不撕裂、不原地乱颤；若抖动请微调 `footLengthFactor` 或该模型刚体参数。
8. 帧率：真机尽量稳 60（UMT 物理锁 60）；帧率不足先降城市密度/贴图，别动物理步长。

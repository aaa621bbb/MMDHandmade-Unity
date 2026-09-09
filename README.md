# MMD Handmade · Unity MMD 播放器（半成品）

一个基于 **Unity 6.6** + **UMT 0.5.1**（`Packages/com.candidumgames.unitymmdtools`）的 MikuMikuDance（MMD）播放器。
把任意 `.pmx` 模型 + `.vmd` 动作丢进 `Assets/MMDResources/`，即可在场景里加载、播放（含 VMD 骨骼动画）、
Bullet 裙摆物理、表情/morph 切换、进度拖拽与环绕相机。

> **渲染管线**：Built-in RP（工程无 URP asset，升级时请保持 Built-in，不要被切成 URP）。
> **编辑器版本**：用户的 **Unity 6.6**（详见下文“首次使用”）。

---

## 0. 这是什么 / 技术栈

| 项 | 值 |
|---|---|
| 引擎 | Unity **6.6**（工程由 2022.3.22f1 创建，首次用 6.6 打开会自动升级） |
| 渲染 | Built-in Render Pipeline（UMT 支持 built-in / URP） |
| 插件 | UMT `com.candidumgames.unitymmdtools` 0.5.1（`.pmx`/`.vmd` 导入、PMX 运行时构建、MMD 骨骼求解 `MMDTransformManager`、Bullet 物理、SDEF 裙摆、VMD→AnimationClip） |
| 平台 | Windows x64 / Android arm64-v8a / Web(WebGL) 有 Bullet 原生物理；**macOS/Linux 编辑器没有物理求解**（预期，非 bug，播放器自动降级） |
| 玩法 | 只做「加载模型 → 播放舞蹈 → 裙摆物理 → 切表情 → 暂停/循环/拖进度」；**Phase 2 巨人踩城/踩小人玩法不在此版本** |

---

## 1. 首次使用（务必按此顺序）

1. **用 Unity 6.6 打开本工程**。工程是旧版 `2022.3.22f1` 创建的，打开时会提示**升级工程**——请选择允许。
   升级过程中依赖会自动解析（`com.unity.burst/collections/mathematics/nuget.newtonsoft-json/ugui` 已在根
   `Packages/manifest.json` 声明）。**保持 Built-in 渲染管线，不要切到 URP。**
2. 让 Unity 完成 Asset 导入与编译。若出现依赖版本冲突或编译错误：见文末「已知限制与排查」。
3. 首次可能需要在 UMT 菜单补一次资源：**Tools ▸ UMT ▸ Create Default Resources**
   （会生成 `UMTResources.asset`，供运行时 `BuildUnityObjects` 使用）。包内自带该资源，通常无需手动执行，
   但若 Console 提示“UMT import resources asset was not found”，执行一次该菜单命令即可。

---

## 2. 三步上手（丢模型就跑）

1. **放模型**：把 `某个模型.pmx` 连同其贴图（同目录）放入 `Assets/MMDResources/Models/`。
   Unity 会用 UMT 的 ScriptedImporter 自动导入，生成 `PMXModel` 子资产。
2. **放动作**：把 `某支舞.vmd` 放入 `Assets/MMDResources/Motions/`。同样自动导入为 `VMDAnimation`。
3. **刷新并播放**：
   - 在 Hierarchy 选 `MMDPlayerController`，Inspector 里点 **「Refresh Library」**（等价于菜单
     **Tools ▸ MMD Player ▸ Refresh Library**）。Console 会打印「找到 N 个模型 / M 个动作」。
   - 打开 `Assets/Scenes/MMDSandbox.unity`（若不存在，用 **Tools ▸ MMD Player ▸ Build MMDSandbox Scene** 生成）。
   - 点 **Play**，在底部面板通过下拉选择模型 / 动作，再点「播放」即可。

> 若 Library 还没生成也没关系：MMDSandbox 场景里的控制器会自动读取库；无模型时底部面板会显示引导文案。

---

## 3. 操作说明

| 操作 | 方式 |
|---|---|
| 播放 / 暂停 / 停止 | 底部面板「播放」「暂停」「停止」按钮 |
| 循环 | 「循环」开关 |
| 拖进度 / 跳转 | 进度条拖动（联动 `当前秒/总秒` 文本） |
| 物理开关 | 「物理」开关；本平台无 Bullet 原生库时置灰并提示（见 §5 已知限制） |
| 表情 morph | 「表情」下拉选择 morph，权重固定为 1（含 morph 的模型才显示该行） |
| 重置姿势 | 「重置姿势」按钮（回到 bind pose） |
| 相机环绕 | 左键拖拽 = 环绕；滚轮 = 推拉；右键/中键拖拽 = 平移；**双击 = 复位** |
| 相机对焦模型 | 加载模型后相机自动摆到「看整体」机位（脚贴地、俯 ~15°） |

---

## 4. 目录约定 / 我新增了什么

```
Assets/
  MMDResources/
    Models/       ← 放 .pmx + 同名贴图
    Motions/      ← 放 .vmd（扫描后生成的 .anim 播放片段也在此）
    Library.asset ← MMDAssetLibrary（由扫描器生成/更新）
  Scenes/
    MMDSandbox.unity ← 播放器场景（用 Tools ▸ MMD Player ▸ Build MMDSandbox Scene 生成）
  Scripts/
    MMDPlayer/
      MMDPlayerController.cs   总控：加载 PMX、驱动播放、物理/表情/进度
      MMDPlaybackUI.cs         uGUI 播放面板（纯转发，逻辑都在 Controller）
      MMDOrbitCamera.cs        环绕相机（环绕/缩放/平移/复位/自动对焦）
      MMDPhysicsGuard.cs       平台物理可用性判定（优雅降级）
      MMDAssetLibrary.cs       ScriptableObject 资产索引（模型↔动作）
      Editor/
        MMDResourcesScanner.cs  编辑器扫描器：扫 Models/Motions、转 VMD→Clip、写库
        MMDSandboxBuilder.cs    一键生成 MMDSandbox 场景
        MMDPlayerControllerEditor.cs   Controller 的 Inspector（含 Refresh Library 按钮）
```

**架构纪律**：UI 只做数据绑定与按钮转发，所有业务/状态都在 `MMDPlayerController`；
骨骼求解唯一权威是 UMT 的 `MMDTransformManager`，我们不在别处写骨骼；动画只用
`VMDAnimationClipConverter` 产出的 clip，绝不手写 VMD 关键帧；从不关闭 SDEF 皮肤。

---

## 5. 已知限制 / 排查

- **macOS / Linux 无 Bullet 物理**：UMT 的原生插件只带 `Windows/x64`、`Android/arm64-v8a`、
  `Web/WebGL` 三份二进制。在 macOS/Linux 编辑器里，`MMDPhysicsGuard` 判定不可用，将强制
  `livePhysics=false`，UI 置灰并提示“当前平台无 Bullet 原生库（仅 Windows/Android/Web 可用），物理已关闭”。
  这是**预期行为**，不是 bug。裙摆仍会以 FK 动画驱动，只是没有实时物理模拟。
- **未装 lilToon**：模型材质走 UMT 的 Built-in 回退（Unlit 观感），非 MMD 卡通着色。想要更好看需另装 lilToon（本版本未集成）。
- **Phase 2 玩法未实现**：巨人踩城 / 踩小人、多模型同屏、动作混合、视频导出、运行时从任意路径加载未导入的原始 pmx、手办摆姿均不在本版本。
- **UMT 0.5.1 未官方保证兼容 Unity 6.6**：包自身声明最低为 Unity 2022.3。6.6 若更新了 burst/collections/mathematics 可能导致 API 差异。若编译报错：
  1. 优先在根 `Packages/manifest.json` 用 **6.6 认可**的版本声明（已写入 UMT 声明的版本；若冲突请改成 6.6 自带的更高版本并解除 UMT 的硬 pin）。
  2. 若仍报 API 变更（如 Collections/Burst 命名空间或方法更名），可在我方 `Assets/Scripts/MMDPlayer` 内做**兼容垫片**，**不要改 UMT 包源码**。
- **当前根 `manifest.json` 已去掉 `com.unity.animation.rigging`**：UMT 的人形 Avatar 由
  `PMXAvatarBuilder` 用 `AvatarBuilder` 直接构建，不依赖该包；去掉它可减少 6.6 下的依赖风险。
  若你希望保留人形 Avatar 的额外功能，可自行加回，不影响本播放器编译。
- **启用物理时会把帧率锁到 60**：UMT 的 `MMDTransformManager` 在 `livePhysics=true` 时设置
  `Application.targetFrameRate = 60`。关闭物理后请留意帧率是否恢复正常（可在 Project Settings 里手动调整）。
- **旧输入（Input Manager）**：UI 与相机用的是旧输入（`Input`/`StandaloneInputModule`）。
  若你把 Active Input Handling 设为「仅新 Input System」，需改为「两者」或「旧 Input Manager」，
  否则事件与相机鼠标操作不生效。
- **纹理加载**：运行时 `PMXImporter.BuildUnityObjects` 通过 `PMXTextureLoader` 从磁盘读取 `.pmx`
  旁的贴图。编辑器内可正常读取；若做成独立构建，通常需把贴图随 `.pmx` 放在可访问路径（本版本未做运行时任意路径加载）。

---

## 6. 许可 / 版权提示

请自行确认所用模型与动作的**分发许可**。MMD 素材（模型、动作）受原作者版权与使用条款约束，
本仓库仅提供播放器代码，**不含任何 MMD 模型/动作二进制**。请勿分发未获授权的素材。

---

## 7. 交付说明

本仓库为「半成品」交付：脚本已通过静态自查，但**未在真实 Unity 6.6 + 真实模型上运行验证**。
首次用真实 `.pmx` + `.vmd` 时请按 §2 流程走一遍，重点确认：
- 场景能打开、无编译错误；
- 模型摆正（脚贴地、朝相机）；
- 播放一条动画、IK/约束正常、裙摆物理（Windows）抖动自然；
- 暂停/循环/拖进度/重置姿势正确；
- 切换表情 morph 生效。

若某一步失败，多为 UMT 0.5.1 在 Unity 6.6 下的兼容性所致，可按 §5「已知限制 / 排查」处理。

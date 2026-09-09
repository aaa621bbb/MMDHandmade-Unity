# Phase 2 · 女巨人城·踩小人（Android）—— 设计说明（实现 AI 撰写）

> ⚠️ **重要前提（必须写在最上面）**：仓库根目录**没有** `TASK_Phase2.md`。实现前已对当前工作区、git 历史、所有分支与远端做彻底排查，均无此文件；`TASK_给实现AI.md` 与 `docs/Unity_MMD_半成品_方案_v0.1.md` 也只把「巨人踩城/踩小人」列为 **Phase 1 不做、Phase 2 再做**，未给任何玩法细节。
>
> 因此本文档**不是**用户提供的 Phase 2 规格，而是实现 AI 依据「女巨人城·踩小人 · Android」这一概念**自拟的规格与方案**，供用户复核与裁量。文中所有玩法数值、交互方式、验收标准均为**假设**，若与你的预期不符，请指出，我会调整。

---

## 1. 概念与目标

用户已在此基础上做了 **Phase 1「MMD 播放器」**（加载任意 `.pmx` + `.vmd` 舞蹈、裙摆物理、表情、相机、播放控制）。Phase 2 把它**叠加**为一个小游戏：

- **女巨人**：用户放入的任意 MMD 模型被放大成巨人（复用 Phase 1 的 `MMDPlayerController`，模型会跳舞）。
- **小人城**：程序化生成的微缩城市，含**建筑**与**小人物**。
- **踩踏玩法（Android）**：单指拖动控制巨人移动；轻点/按钮踩踏；走过或踩到即破坏建筑（倒塌下沉）与小人物（压扁消失），累计**得分**。
- **移动端**：双指捏合缩放相机；HUD 显示得分与操作提示；无模型时用占位胶囊让玩法先跑起来。

## 2. 发布目标平台

**Android**（`Android arm64-v8a`）。游戏玩法只依赖旧版 Input（`Input.touchCount/GetTouch`），与 Phase 1 一致要求 **Active Input Handling = Both 或 Old**。渲染仍然 Built-in RP。

## 3. 与 Phase 1 的关系（叠加，不删 Phase 1）

- Phase 1 的全部脚本、场景 `MMDSandbox.unity`、`MMDResources` 资源库、UMT 本地包**保持不变**。
- 新增一套**独立模块** `Assets/Scripts/MMDGiantGame/`（命名空间 `MMDGiantGame`），只**引用** Phase 1 的公开 API（`MMDPlayerController`、`MMDAssetLibrary`、`MMDPlayerPreferences`），不改动其实现。
- 新增独立场景 `Assets/Scenes/MMDGiantSandbox.unity`（由 Builder 脚本生成），与 Phase 1 场景互不影响。

## 4. 玩法规格（自拟）

### 4.1 巨人
- 来源：Phase 1 资源库里已扫描的 `PMXModel`。优先加载「上次用过」的模型（`MMDPlayerPreferences.LoadModelPath()`），否则资源库第一个。
- 尺寸：`giantScale`（默认 18；MMD 模型约 1.7m，放大后即高楼）。
- 动画：触发 Phase 1 `autoPlayOnLoad`，巨人在被操控的同时循环跳舞。

### 4.2 城市
- 程序化生成：`gridCells×gridCells` 网格（默认 9×9，`cellSize=16`），中心格留空给巨人。
- 每格按 `personChance` 生成一个小人（胶囊、得分 1），否则按 `buildingChance` 生成建筑（立方体、得分 5）。
- 用 `seed` 固定布局，便于复现与调参。

### 4.3 操控（Android 触摸）
- **单指拖动**：把手指位置射线到 y=0 地面，巨人朝该点移动（`moveSpeed=14`）。
- **移动朝向**：`faceMovement` 开启时，巨人转向行走方向（`SetModelYaw`）。
- **踩踏**：轻点（位移 < 24px）或点击 HUD「踩踏」按钮。触发一次下蹲-恢复动画，并破坏 `stompRadius=9` 内的全部道具。
- **走过即破坏**：移动经过 `walkBreakRadius=4` 内的道具自动破坏（持续反馈，不做完整踩踏动画）。
- **相机**：跟随相机（`MMDGiantCamera`）从高点后方跟随；双指捏合缩放（`minDistance=30`、`maxDistance=320`）。

### 4.4 破坏反馈
- 建筑：向一侧倾倒 + 下沉，持续 `breakDuration` 后销毁。
- 小人：压扁缩小 + 下沉，短促销毁。
- 得分回调 → HUD 更新。

### 4.5 HUD
- 左上：得分。
- 得分下方：操作提示（拖动=移动、轻点/按钮=踩踏）。
- 右下：大「踩踏」按钮。
- 左下：「重新开始」（重置得分 + 重新生成城市）。

### 4.6 无模型兜底
- 没有 `PMXModel`（或资源库为空）时，生成一个**占位胶囊**当作巨人，先让整套破坏/移动/得分机制可玩；放入真实模型并 `Refresh Library` 后自动替换为 MMD 巨人。

## 5. 目录与文件

```
Assets/Scripts/MMDGiantGame/           ← 本 Phase 2 模块
  MMDGiantGameManager.cs               总控：移动/踩踏/计分/重启/复用 Phase 1 控制器
  MMDCityBuilder.cs                    程序化生成城市（建筑+小人）
  MMDBreakableProp.cs                  可破坏道具（倒塌/压扁 + 得分回调）
  MMDGiantCamera.cs                    跟随相机 + 双指缩放
  MMDGiantHUD.cs                       uGUI 得分/提示/踩踏/重开
  Editor/MMDGiantSandboxBuilder.cs     “Tools/MMD Giant Game/Build MMDGiantSandbox Scene”
Assets/Scenes/MMDGiantSandbox.unity    （由 Builder 生成）
```

## 6. 与方案文档 v0.1 架构纪律的对照

- **分层**：`MMDGiantGameManager` = 业务/状态；`MMDGiantHUD` 只做转发与显示。
- **不重复写骨骼**：巨人动画仍由 Phase 1 控制器 + UMT `MMDTransformManager` 驱动；Phase 2 只移动 `giantRoot`（整棵模型根）与调 `SetModelYaw`，不碰骨骼。
- **空态安全**：无模型/空库不 NullRef，走占位巨人。
- **不写死资源路径**：模型全部来自 `MMDAssetLibrary` 扫描结果。
- **不碰 UMT 包源码**；新增类都在 `MMDGiantGame` 命名空间。
- **编辑器代码**（用 `UnityEditor`）只放 `Editor/` 子目录。

## 7. 验收清单（待用户在 Unity 6.6 / Android 真机验证）

- [ ] `MMDGiantSandbox` 场景能打开、无编译错误。
- [ ] 无模型时进入场景即有占位巨人 + 城市可踩。
- [ ] 单指拖动能让巨人移动、朝行走方向。
- [ ] 轻点/按钮能踩踏，范围内建筑倒塌、小人压扁，得分增加。
- [ ] 走过道具会自动破坏。
- [ ] 双指捏合能缩放相机。
- [ ] 放入 `.pmx` 并 `Refresh Library` 后，巨人自动变为 MMD 模型并跳舞。
- [ ] 「重新开始」重置得分并重建城市。

## 8. 已知取舍 / 风险

- **未运行 Unity**：以上均为静态自查通过；场景能否打开、相机/触控手感应由用户实机确认。
- **破坏反馈为变换动画**：无粒子/特效、无物理破坏，观感朴素；可后续加粒子与音效。
- **占位巨人**为胶囊，仅用于无素材时验证机制，非真实形象。
- **性能**：GIANT 游戏默认关闭 Bullet 物理（`livePhysics=false`）以保帧率；如需裙摆物理可自行开启。
- **移动范围**：巨人移动没有硬边界，可走出城市网格；如需限制可在 `MMDGiantGameManager` 加 clamp。

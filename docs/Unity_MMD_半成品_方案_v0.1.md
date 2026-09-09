# Unity MMD 项目 · 半成品实现方案 v0.1

> 目标读者：负责实现的 AI / 开发者。本文把「标准 MMD 查看器/播放器该有的能力」定成一份可直接落地的规格。
> 核心诉求：**不是把某个模型塞进包里**，而是把「用户能自己导入任意 .pmx/.vmd → 打开就能播 + 裙摆物理 + 表情」这套能力与工作流搭齐，做成一个**自包含、可扩展的 MMD 半成品**。
> 踩小人（巨人踩城）等玩法模块 = **Phase 2，本方案不做**，只留可插拔入口。

> ⚠️ **编辑器版本 = 用户的 Unity 6.6（非 2022.3）**。当前仓库工程是旧版 `2022.3.22f1` 创建的，**最终会在 Unity 6.6 打开并自动升级**。本方案的验收环境一律按 Unity 6.6，实现必须适配 6.6（含 UMT 包及其依赖在 6.6 下的兼容，见 §1.1 与 §5.8）。

---

## 0. 一句话范围

在现有仓库 `MMDHandmade-Unity`（工程由 Unity 2022.3.22f1 创建 + UMT 0.5.1 空壳，`MainScene` 是默认空场景；**目标编辑器 = Unity 6.6，见 §1.1**）之上，实现一个 **MMDPlayer**：用户把 `.pmx` + `.vmd` 放进 `Assets/MMDResources/`，场景里就能列出模型/动画、加载并播放（含 VMD 骨骼动画、Bullet 裙摆物理、表情/morph 开关、相机跟随）、带播放控制 UI。缺模型也能跑（空状态提示），模型进来即用。

**Definition of Done（半成品验收）**：
1. **用 Unity 6.6 打开无编译错误**，依赖在 6.6 下解析成功，工程完成升级（见 §1.1）。
2. 场景一键进入后出现播放器 UI 与一个可加载模型的控制器。
3. 放入一个合法 `.pmx`（含贴图）+ 一个 `.vmd` 后，不写代码即可在 Inspector/运行时列出并「加载 → 舞蹈播放 → 裙摆物理 → 切表情 → 暂停/循环/拖进度」。
4. 物理在不支持的平台自动降级为关闭并给出提示，不崩。
5. 有 README 写明「丢模型就跑」的用户路径。

---

## 1. 技术前提（已核实，避免实现者重复勘察）

仓库现状与 UMT 能力，均从源码直接确认：

| 项 | 值 |
|---|---|
| UMT 版本 | `0.5.1`，命名空间 `UMT` |
| UMT 所在 | 仓库内本地包 `Packages/com.candidumgames.unitymmdtools`（不在 Package Manager 线上注册，直接以本地包形式 resolve） |
| UMT package.json 依赖 | `com.unity.ugui 1.0.0`、`com.unity.burst 1.8.4`、`com.unity.collections 2.1.4`、`com.unity.mathematics 1.3.3`、`com.unity.nuget.newtonsoft-json 3.2.1`（Unity 打开时会自动拉取；若失败在根 manifest.json 补 pinned 版本） |
| 渲染管线 | **Built-in RP**（工程无 URP asset；lilToon 未安装 → 材质回退 Built-in Unlit） |
| Bullet 原生插件平台 | 仅 **Windows x64 / Android arm64-v8a / Web(wasm)** 带 `.dll/.so/.a`；**macOS/Linux 编辑器没有物理求解**，须自动降级（见 §5.5） |
| 项目根 manifest.json | 目前只有 `com.unity.animation.rigging 1.3.0`（人形 Avatar 用到；留作可选，勿强依赖） |
| **目标编辑器** | **用户的 Unity 6.6**（最终验收环境）。当前工程 `ProjectVersion.txt = 2022.3.22f1`，用 6.6 打开会自动升级并重写工程文件 |
| 当前 `MainScene.unity` | 默认场景（Main Camera/Directional Light/Ground），**0 个脚本引用**，需重建为播放器场景 |

### 1.1 编辑器 = Unity 6.6（升级兼容重点，实现前先读）

老 2022.3 工程用 Unity 6.6 打开时 Unity 会提示升级并**改写 ProjectVersion / ProjectSettings**。实现者要做：

1. **不要手改 ProjectVersion.txt 假装是 6.6**：真实地（或让用户）用 Unity 6.6 打开一次，让编辑器升级工程，再把升级后的 `ProjectVersion.txt`、`ProjectSettings/*.asset`、`Packages/manifest.json`、`Packages/packages-lock.json` 一并纳入提交。给不了真实升级结果时，在 README 写死「首次用 Unity 6.6 打开会升级，允许出现 upgrade prompt」。
2. **Unity 6.6 的新工程默认走 URP/HDRP，但本工程是 Built-in RP**。确保 6.6 打开后**仍是 Built-in 渲染管线**（`GraphicsSettings`/管线资产不被切走），UMT 要求 built-in 或 URP。Unity 6 仍支持 built-in，别让升级把它切到 URP 导致 UMT 材质回退异常。
3. **依赖版本冲突**：UMT 0.5.1 把依赖 pin 成 burst 1.8.4 / collections 2.1.4 / mathematics 1.3.3 / newtonsoft 3.2.1，而 Unity 6.6 自带更新版本（如 Unity 6 的 burst/collections 已更高）。若 6.6 解析冲突：
   - 优先让 Unity 6.6 的更高版本向上兼容并**解除 UMT 的硬 pin**（把 UMT 需要的包在根 manifest 用 6.6 认可的版本声明，删掉 UMT package.json 里过旧的直接依赖或让它用 `>=`）；
   - 编译期报 API 变更（如 Collections 命名空间调整、burst API 变化）时以 **Unity 6.6 为准**做兼容垫片，不反过来降级 Unity。
   - 记录最终能编译通过的依赖版本组合，写回根 manifest 与 README。
4. **UMT 0.5.1 声明的引擎最低要求是 Unity 2022.3**，6.6 满足；但**没有官方保证**，必须用 §5.7 的真实模型冒烟在 6.6 下通过才算数。
5. **`com.unity.animation.rigging 1.3.0`** 在 Unity 6.6 下可能已有更新版或改名；若只为人形 Avatar 用，可保留，否则评估去掉后 `createAvatar=false` 是否可行（优先级低）。
6. 原生 Bullet 插件平台声明不变（Windows x64 / Android arm64 / Web），与引擎版本无关；Unity 6.6 若对 Android .so / wasm 插件有 ABI 要求变化，以 6.6 实际跑通为准。

### 关键运行时 API 锚点（源码确认真实签名）

```csharp
// ① 从 PMXModel(ScriptableObject 子资源) 运行时搭出一整棵可播层级 —— 这是核心入口
namespace UMT {
  public sealed class PMXImportOptions {
    public string sourcePath; public string sourceName;
    public string textureBaseDirectory;
    public Transform parent;                 // 关键：可把结果挂到任意父节点下
    public bool applyRenames = true;
    public UMTResources umtResources;
    public PMXRenameLists renameLists;
    public bool strictVersion = true;
    public bool createAvatar = true;
    public Func<PMXModel, PMXImportOptions, Texture2D[]> loadTextures; // 运行时导入需要
  }
  public sealed class PMXImportResult {
    public GameObject root; public PMXModel model;
    public Transform[] bones;
    public PMXAvatarBuildResult avatarResult;          // .avatar 人形
    public MMDTransformBuildResult mmdTransformResult; // .transformManager/.physicsManager
    public List<PMXImportedMesh> meshes;               // SkinnedMeshRenderer/mesh/morphTable
    public List<Material> materials; public List<Texture2D> textures;
  }
  public struct MMDTransformBuildResult {
    public MMDTransformManager transformManager;
    public MMDPhysicsManager physicsManager;
    public MMDRigidBody[] rigidBodies; public MMDJoint[] joints;
    public int boneComponentCount; public int ikControllerCount; public int constraintCount;
  }
  public static class PMXImporter {
    public static PMXImportResult BuildUnityObjects(PMXModel model, PMXImportOptions options = null);
    public static PMXImportResult Import(string pmxFilePath, ...);   // 运行时解析原始 .pmx
    public static PMXImportResult Import(byte[] pmxBytes, ...);      // 支持 StreamingAssets 读字节
  }
  // ② VMD → 可播 AnimationClip
  public static partial class VMDAnimationClipConverter {
    public static VMDModelClipData Convert(VMDAnimation animation, PMXModel model,
        VMDAnimationClipOptions options = null, ProgressCallback progress = null);
  }
  // ③ 播放驱动核心（场景里自动 LateUpdate，ExecutionOrder=10000）
  public sealed class MMDTransformManager : MonoBehaviour {
    public MMDBoneTransform[] bones; public PMXModel model;
    public MMDPhysicsManager physicsManager; public Animator animator;
    public bool transformEnabled = true; public bool solveConstraints = true;
    public bool solveIK = true; public bool livePhysics = true;
    public bool doSDEFSkinning = true;
    public SDEFSkinningMode sdefSkinningMode; // GPU compute / CPU burst
    public ComputeShader sdefComputeShader;   // null → 强制 CPU
    public bool solveInEditMode = false;
    public void ResetToBindPose(); public void ResetPhysics();
    public void RegisterSDEFSkinners(MMDSDEFSkinner[] skinners);
    public void SolveTransforms(bool accessTransforms);
    public void SolveWithPhysics(bool accessTransforms, float physicsElapsedTime, bool runConstraintsAndIKSolver);
    public void DisposeRuntimeData();
  }
}
```

**行为要点（实现时必须遵守）**：
- `MMDTransformManager`：`[DefaultExecutionOrder(10000)]` + `[ExecuteInEditMode]`，在 `LateUpdate()` 里 sample 骨骼 → 解 IK/约束 → 可选跑 Bullet → flush 回 Transform；`livePhysics` 为真且处于 Play 时会 `Application.targetFrameRate = 60`。
- 动画驱动链条：`AnimationClip`（由 VMD 转换，IK-runtime-solved 或 FK-baked）经**模型根上的 Animator** 写入各骨骼 `MMDBoneTransform` 的 local 变换 → 再由 `MMDTransformManager` 覆盖式求解。因此**转换出的 clip 要打在模型根 Animator 上播**（Animator 通常建在 root）。
- 模型通常每帧由转换器产出 clip，clip 用 PMX 骨骼名 binding 到 root Animator（A 组件带一个能播多 clip 的 Animator 层即可）。**具体 clip 打骨骼的方式以实现者读 `VMDAnimationClipConverter` 产出为准，务必用该包自己的转换结果，别手写 K 帧。**
- morph（表情）：PMX 顶点 morph 作为 blend shape 进 mesh；VMD 的 morph 轨会在转换 clip 里驱动对应 blend shape 权重。UI 需暴露当前可用的 morph 名。
- **模型朝向/单位是必查点**：MMD 模型常见 `+Y up`、初始面朝 `+Z` 或 `+X` 不定；`BuildUnityObjects` 是否已做 90° 预处理必须用一个真实模型实测确认（见 §5.7 冒烟），勿拍脑袋。半成品里做「一键摆正」逻辑：root 缩放按需、y=0 落地、面朝相机，并留手动四元数字段。

---

## 2. 目标场景与资产目录约定

```
Assets/
  MMDResources/                 ← 用户唯一需要打交道的文件夹
    Models/                     ← 放 .pmx + 同名贴图（UMT 自动导入成子资源）
    Motions/                    ← 放 .vmd（导入为 VMDAnimation）
    Library.asset               ← (可选) MMDAssetLibrary ScriptableObject，编辑器自动扫 Models/Motions 填充
  Scenes/
    MMDSandbox.unity            ← 新主场景（播放器）【保留原 MainScene 或覆盖，建议新建避免破坏】
  Scripts/
    MMDPlayer/                  ← 本方案新增的播放器代码（见 §3）
      MMDPlayerController.cs
      MMDPlaybackUI.cs
      MMDOrbitCamera.cs
      MMDPhysicsGuard.cs        ← 平台降级
      Editor/MMDResourcesScanner.cs  ← 编辑器助手：扫描并生成库
      Editor/MMDSmokeTest.cs    ← 一键冒烟（可选）
```

用户路径（写进 README）：把 `某模型.pmx` 拖进 `Models/`，把 `某舞.vmd` 拖进 `Motions/` → 点 `MMDPlayerController` 上的「Refresh Library」→ 运行 → 下拉选模型、选动画 → Play。

> 设计取舍：**MVP 采用「编辑器导入资产 + 运行时 Build」**（即用户拖 .pmx 进 Assets，Unity 用 UMT ScriptedImporter 生成 PMXModel 资产，运行时 `BuildUnityObjects` 搭层级）。这比运行时从 StreamingAssets 读原始字节更稳、更快、可走编辑器工具链。
> 若想要「更像网页版、运行时从文件夹按名加载」，可作为可选进阶：把原始 `.pmx` 放 `StreamingAssets`，运行时 `PMXImporter.Import(bytes)` + `options.loadTextures` 手动配贴图加载。**Phase 1 不强制**，架构预留一个 `IModelSource` 接口即可。

---

## 3. 新增脚本设计（半成品核心）

### 3.1 `MMDPlayerController : MonoBehaviour`（总控，挂在场景空 GO 上）
公开字段：
- `MMDAssetLibrary library`（Inspector 引用 Library asset；含模型/动画索引）
- `bool autoPlayFirst = false`
- `bool livePhysics = true`（交给下方 Guard 判定）
- `Transform modelParentOverride`（可选，默认挂在自身下）
- `Vector3 modelPose = (0,0,0)`、`Vector3 modelForward = (0,0,1)`、`float modelScale = 1`（手动摆正用）

运行时职责：
- `LoadModel(PMXModel modelAsset)`：
  1. `options = new PMXImportOptions { parent = anchor, createAvatar = true, applyRenames = true }`
  2. `result = PMXImporter.BuildUnityObjects(modelAsset, options)`
  3. 记 `_root = result.root`、`_tm = result.mmdTransformResult.transformManager`、`_pm = result.mmdTransformResult.physicsManager`、`_animator = _root.GetComponent<Animator>()`（必要时 Add）
  4. 应用摆正（落地 y=0 / 朝向 / 缩放 / 一个「Reset to bind pose」）
  5. 把 `library` 里与该模型匹配的 motion clips / morph 列表灌给 UI
- `PlayMotion(AnimationClip clip)` / `Pause()` / `Resume()` / `StopAndReset()` / `SetTime(float t)` / `SetLoop(bool)`：驱动 `_animator`（clip 转换已烘焙 IK/物理，直接 Animator.Play 即可；拖进度可用 `AnimationClip.SampleAnimation` 或 Animator 回放，二选一并统一成接口）
- `SetMorph(string name, float weight)`：驱动对应 blend shape
- `TogglePhysics(bool on)`：`_tm.livePhysics = on`，开关时调 `ResetPhysics()`
- `ResetModel()`：`_tm.ResetToBindPose()` 后清空动画
- 可选中 `List<AnimationClip>` 轮播 / 上一首下一首
- **空库/空模型状态友好**：无模型时 UI 显示引导文案，不 NullRef。

事件/回调：`OnModelLoaded(MMDTransformManager)`、`OnClipChanged(...)`、`OnLoadError(string)`，供 UI 订阅。

### 3.2 `MMDPlaybackUI`（播放控制面板）
用 **uGUI**（`com.unity.ugui` 已在依赖里）。挂 Canvas + EventSystem。控制项：
- 模型下拉（来自 library）/ 动画下拉
- 播放 / 暂停 / 停止 / 循环 toggle
- 进度 Slider（播放中联动当前 `time/duration`）+ 当前/总时长文本
- 物理开关 toggle（被 Guard 禁用时置灰+提示）
- 表情 morph 下拉 + 权重滑杆（模型含 morph 才有）
- 重置姿势按钮
- 出错时的 toast/日志行
布局给个能看全模型的半透明底部/侧栏面板即可，不追求精致。**UI 只做数据绑定，逻辑全部走 Controller**，方便将来替换成踩小人 HUD。

### 3.3 `MMDOrbitCamera`（相机）
- 目标 = 模型根；按住拖拽 = 环绕，滚轮 = 拉近拉远，右键/中键可平移；双击复位。
- 提供「看整体」默认机位（模型脚底贴地面 + 相机俯 ~15°）。
- 预留接口给 Camera-VMD：若 `library` 提供相机轨 `.vmd`，转换出的 camera clip 播放时切到 VMD 相机 rig（UMT converter 有 camera 支持），作为进阶开关。

### 3.4 `MMDAssetLibrary : ScriptableObject`（资产索引）
字段：`List<PMXModel> models`、`Dictionary<PMXModel, List<AnimationClip>> motionsByModel`、`Dictionary<PMXModel, List<string>> morphNamesByModel`（morph 名可即时从已加载模型取，不必入库）。
用途：把「Assets 里有什么模型/动画」结构化给 UI 与 Controller。由编辑器扫描器维护。

### 3.5 `MMDPhysicsGuard`（平台降级）
启动/Inspector 时判定：
- 若 `Application.platform` 不在 {Windows x64, Android arm64, WebGL} 且当前构建目标没有对应原生插件 → 强制 `livePhysics=false`，UI 置灰并提示「当前平台无 Bullet 原生库，物理已关闭（仅 Windows/Android/Web 可用）」。用插件存在性 + platform 双保险（调用 `MMDPhysicsManager` 前先 `Application.isEditor && RuntimeInformation` 判一次，失败则降级并记 warning，绝不崩溃）。

### 3.6 编辑器扫描器 `MMDResourcesScanner`（让「自己导入模型」零代码）
- Menu 或 Controller Inspector 上放「Refresh Library」按钮。
- 遍历 `Assets/MMDResources/Models/*.pmx` 与 `Motions/*.vmd`。
- 生成/更新 `MMDAssetLibrary`：把 pmx 主对象（PMXModel）与经 `VMD Clip Converter`（或导入期 VMD Animations 列表）产出的 `.anim` clip 关联。
- 产出内容写进 Library asset 并 `AssetDatabase.SaveAssets`，同时打印「找到 N 个模型 / M 个动作」到控制台。
> 说明：VMD→clip 的转换方式建议直接用 UMT 编辑器能力（Tools▸UMT▸VMD Clip Converter 或导入期在 .pmx importer 加 VMD Animations）。扫描器只负责把产物「收进库」，不重复造转换轮子。若想让动画也在运行时即时转换，走 `VMDAnimationClipConverter.ConvertAsync`（Runtime），并把原始 `.vmd` 也入库，进阶实现。

---

## 4. 场景搭建（MMDSandbox）

层级建议：
```
MMDSandbox
├─ Directional Light（关阴影或柔和，MMD 常用无/软影）
├─ Ground  (可选，供物理地面碰撞；MMDPhysicsManager 自带 ground 选项，确认后可不放)
├─ MMDPlayerController      (空 GO, 挂 §3.1 + §3.2 + §3.4 refs)
│   └─ ModelAnchor          (动态：每帧模型 Build 到此处下)
├─ EventSystem + Canvas     (播放器 UI, 挂 MMDPlaybackUI)
└─ Camera (Main)            (挂 MMDOrbitCamera, 指向 Controller)
```
相机对焦：Controller `OnModelLoaded` 后 OrbitCamera 自动取模型包围盒中心/半径摆默认机位。

---

## 5. 实现注意事项 / 易错点（务必照做）

### 5.1 别手写 VMD K 帧
动画必须来自 `VMDAnimationClipConverter`（runtime-solved sparse 或 IKBakedToFK）。你的播放器只负责「在 Animator 上播转换出来的 clip + 让 MMDTransformManager 在 LateUpdate 接管求解」。clip 转换的骨骼 binding 方式以实现者对包源码的阅读为准，**先做通一个模型一条动画，再上通用列表**。

### 5.2 MMDTransformManager 的时序
它 `ExecutionOrder=10000` 且自己 `LateUpdate`。**不要在别的地方重复写骨骼**。播放器只设 `_tm` 的开关与调 `ResetPhysics/ResetToBindPose`；若你手动 SampleAnimation 设时间，注意要和 solver flush 顺序一致，建议统一用 Animator.Play + `Playable` 时间控制，避免与 solver 打架。

### 5.3 物理开关会锁帧率
`livePhysics=true` 在 Play 时强制 `targetFrameRate=60`。降级/关闭时要记得把帧率选项交还给用户（或保持不变，别留下 60 锁）。

### 5.4 SDEF
模型含 SDEF 顶点时 UMT 用 `MMDSDEFSkinner`（GPU compute 需一个 compute shader，导入期自动 assign；没有则回退 CPU burst）。裙摆/头发 SDEF 是「物理裙摆」观感的一部分；确认 `doSDEFSkinning` 与 compute shader 链路在导入期完整，别在运行时把 SDEF 关了导致布料变形。

### 5.5 平台降级（见 §3.5）是硬要求
实现者若在 macOS/Linux 上跑，物理必然不可用——**这是预期行为**，UI 提示别当 bug。真正的 Bug 判据是「物理可用平台也坏了」或「降级后抛异常」。

### 5.6 资源导入依赖自动解析
开工程若 UMT 缺依赖编译错，把 `com.unity.burst/collections/mathematics/nuget.newtonsoft-json/ugui` 明确加进根 `Packages/manifest.json` 的 dependencies（用 UMT package.json 里的版本）。**但若在 Unity 6.6 下版本冲突，改按 §1.1-3 的规则处理**（以 6.6 认可版本为准并解除硬 pin）。`com.unity.animation.rigging` 已存在，Avatar 用到；若去掉也要能跑。

### 5.7 冒烟测试必须过（半成品红线）
用一个**真实合法** `.pmx`+`.vmd`（实现者从用户网页版工程取 昔涟.pmx / 流萤春日手信.pmx 及对应 vmd，或自备 MMD 素材）验证：
1. 导入无报错，BuildUnityObjects 生成完整骨骼层级与 mesh。
2. 模型**朝姿正确**（朝前、脚落地、不过度放大）——不对就修摆正逻辑或反馈给包（别猜）。
3. Play 一条动画能跳，IK/约束正常，物理裙摆抖动自然。
4. 暂停/循环/拖进度/重置都能回到正确姿势。
5. 切 1~2 个表情 morph 权重生效。
6. 无模型进入：UI 正常引导，无异常。
> 若实现环境无法肉眼验姿，把冒烟做成「自动断言：根节点 y≈0、朝向向量与期望夹角、clip 时长>0、无 NullRef」的 EditorTest，并留截图给用户复核。

---

## 6. 交付物清单

1. 新增脚本 §3 全套（含 Editor 扫描器 + 可选冒烟测试）。
2. `MMDSandbox.unity` 播放器场景（含 UI、相机、控制器、Guard）。
3. `MMDAssetLibrary` 资产与扫描填充逻辑。
4. README（`Assets/README.md` 或仓库根）写明：
   - 环境要求（**Unity 6.6**，Built-in RP，Windows 上才有实时物理；首次打开会从旧版 2022.3 工程升级，允许出现 upgrade prompt）
   - 「把自己模型丢进去就跑」三步路径 + 截图位
   - 已知限制（macOS/Linux 无物理、需 lilToon 才有完整卡通着色否则 Unlit 回退、Phase 2 踩小人未实现、UMT 0.5.1 未官方保证兼容 Unity 6.6 需以真实模型冒烟为准、若改了依赖版本要注明当前组合）
5. 保留现有 UMT 本地包不动（只增不改第三方包源码）。
6. push 回 `aaa621bbb/MMDHandmade-Unity`（main），提交信息语义化。

---

## 7. 范围外 / 后续（明确不做，避免跑偏）

- ❌ Phase 2「巨人踩城 / 踩小人」：**本方案不含**。仅要求 Controller/场景分层清晰、可插拔（新增玩法模式时替换或叠加 Controller 上的模式脚本即可，勿为它预留一堆无用抽象）。
- ❌ lilToon 集成与完整 PBR/卡通材质调优（可选增强，独立任务）。
- ❌ 多模型同屏、动作混合/过渡、物理烘焙导出视频。
- ❌ 运行时从 StreamingAssets 按名加载任意 pmx（留 `IModelSource` 口，见 §2 设计取舍）。
- ❌ 手办摆姿 / 骨骼单独控制（此前另一个方案模块，同样留 Phase 2）。

> 若实现中某点与本文冲突，以「让真实模型跑起来」为最高优先级，并把冲突改回注到文末/README，便于复核。

---

## 8. 验收清单（交回给用户时逐项打勾）

- [ ] 工程在 **Unity 6.6** 下打开并完成升级，无编译错误（依赖冲突已按 §1.1-3 处理）
- [ ] `MMDSandbox` 场景进入即有播放器 UI，无模型时友好引导
- [ ] 拖入任意合法 .pmx+.vmd（冒烟素材）→ Refresh → 运行可加载并播放
- [ ] 播放/暂停/循环/拖进度/重置正常
- [ ] 物理裙摆在工作平台开启、非工作平台优雅降级
- [ ] morph 表情可切换
- [ ] 相机可环绕/缩放/复位
- [ ] README 三步路径清晰
- [ ] 已 push，含一份冒烟截图

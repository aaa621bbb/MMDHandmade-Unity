# 给实现 AI 的完整任务书（必读，从这里开始）

> 你是负责实现本仓库 MMD 播放器的 AI。**阅读顺序**：① 本文件（完整任务书）→ ② 仓库 `docs/Unity_MMD_半成品_方案_v0.1.md`（权威规格，8 章）→ ③ `Packages/com.candidumgames.unitymmdtools/`（UMT 源码，写码前逐个查证签名用）。
> 本文件已尽量把一切要求、限制、坑、交付标准和报告格式讲清楚。若有歧义，以「让真实模型能在 Unity 6.6 里跑起来」为最高准则，合理取舍并在报告里写明，**禁止静默乱来**。

---

## 0. 背景：这是什么项目，用户是谁

- 用户在用 AI 搭建一个 **Unity 版 MMD（MikuMikuDance）播放器**，希望最终能**自己导入任意 `.pmx` 模型 + `.vmd` 动作**，在 Unity 里像网页版 MMD 一样让模型跳舞。
- 本仓库现在是「工程骨架 + UMT 插件」状态，**还完全没有可运行的自研逻辑**（`MainScene.unity` 只是默认空场景）。
- 你只负责按这份任务书把 **MMD 播放器半成品** 搭出来。**“巨人踩城 / 踩小人”等玩法是 Phase 2，本次绝不涉及**，不要预留一堆无关抽象。
- 关键现实约束：**你无法运行 Unity**（只有文本输出 + 改本仓库的能力）。所以你必须写**干净、可静态自查、防御性强**的代码，并在最后如实报告「哪些需要用户在 Unity 6.6 里验证」。

---

## 1. 引擎与依赖（先弄清，别搞错方向）

### 1.1 目标 Unity = 6.6（用户电脑上装的）
- 仓库 `ProjectSettings/ProjectVersion.txt` 现在是 `2022.3.22f1`，但那只是另一个 AI 创建时写的，**不代表用户环境**。用户实际用 **Unity 6.6**。
- **你禁止手改 `ProjectSettings/ProjectVersion.txt`**。用户首次用 6.6 打开工程时会自动升级。你要做的是让**代码与依赖在 Unity 6.6 下成立**。
- 在 `Assets/README.md`（或仓库根 README）写明：「首次请用 Unity 6.6 打开工程，允许出现工程升级提示；此后即可正常使用。」

### 1.2 UMT 插件（核心依赖，只读不改）
- 位置：仓库本地包 `Packages/com.candidumgames.unitymmdtools/`，命名空间 `UMT`，版本 **0.5.1**。
- 它提供：`.pmx` / `.vmd` 的 ScriptedImporter、PMX 模型运行时构建、MMD 骨骼求解器 `MMDTransformManager`、Bullet 物理、SDEF 裙摆、VMD→AnimationClip 转换等。
- **`com.candidumgames.unitymmdtools` 的 package.json 声明的依赖**：
  `com.unity.ugui 1.0.0`、`com.unity.burst 1.8.4`、`com.unity.collections 2.1.4`、`com.unity.mathematics 1.3.3`、`com.unity.nuget.newtonsoft-json 3.2.1`。
- **风险与对策（Unity 6.6 兼容，属你职责范围）**：Unity 6.6 自带的 burst/collections/mathematics 通常更高。若 UMT 的硬 pin 与 6.6 冲突：
  - 允许把 UMT 需要但版本过旧的包，在仓库根 `Packages/manifest.json` 里用 Unity 6.6 认可的版本声明（让 UMT 走传递依赖）；
  - 若仍报编译错，检查是否 UMT 用了被 6.6 移除/改名的 API，必要时**在你的代码里做兼容垫片**，别去改 UMT 源码；
  - **绝不降级 Unity** 去迎合 UMT。
- 根 `Packages/manifest.json` 现有 `com.unity.animation.rigging 1.3.0`：它用于人形 Avatar。若在 6.6 有更新版或已改名，你可评估调整或去掉（此时方案里 `createAvatar` 相关可关），保持工程能编译即可。
- **禁止改动 `Packages/com.candidumgames.unitymmdtools/` 里任何第三方源码/资源**。你只能新增 `Assets/` 下的东西，以及必要时调根 `manifest.json`。

### 1.3 渲染与平台
- 工程走 **Built-in 渲染管线**（仓库没有 URP asset）。Unity 6.6 打开后请确认它仍是 Built-in（升级别被切成 URP），UMT 支持 built-in 或 URP。若 README 需说明，就说明本项目用 Built-in。
- **Bullet 物理原生插件只有 3 个平台**：Windows x64、Android arm64-v8a、Web(WebGL)。**macOS/Linux 编辑器没有物理求解**——这是预期，不是 bug，播放器要能优雅降级（见 §4.4）。

---

## 2. 你要交付什么（逐条列出，禁止缺项）

以下均为 `Assets/` 下新增（除注明外）。**这是完整清单**。

### 2.1 `Assets/Scripts/MMDPlayer/MMDPlayerController.cs`（总控，核心）
职责：加载一个 PMX 模型并驱动播放一个 VMD 动画，串起 UMT 的构建、Animator、MMDTransformManager。

必须实现的公开方法/字段（命名可微调但语义一致）：
- 序列化字段：`PMXModel model`（或经扫描器得到的模型引用）；`List<AnimationClip> motionClips`；`bool autoPlayOnLoad`；`bool loop`；`bool livePhysics = true`（真正生效交给 `MMDPhysicsGuard`）；`Transform modelAnchor`（模型构建到此节点下，可空=自身）。
- `LoadModel(PMXModel modelAsset)`：
  1. 先销毁旧模型（若存在）；
  2. 构造 `PMXImportOptions { parent = anchor, createAvatar = true, applyRenames = true }`；
  3. `PMXImportResult r = PMXImporter.BuildUnityObjects(modelAsset, options)`；
  4. 缓存 `_root = r.root`、`_tm = r.mmdTransformResult.transformManager`、`_pm = r.mmdTransformResult.physicsManager`；`_animator = _root.GetComponent<Animator>()`（必要时 AddComponent）；
  5. 对模型做「摆正」：脚贴地(y≈0)、朝向相机、整体缩放合理；若模型本身朝向/轴有偏差，提供 `Vector3 modelRotationOffset`、`Vector3 modelPositionOffset`、`float modelScale=1` 手动修正；保证和绑定点对齐；
  6. 通知 UI/相机（见 §2.2/2.3 的事件/接口）。
- `PlayMotion(AnimationClip clip)`：在 `_animator` 上播（clip 必须来自 UMT 转换，见 §3）。记录时长、触发 `OnClipChanged`。
- `Pause()` / `Resume()` / `StopAndReset()`（停并 `_tm.ResetToBindPose()`）/ `SetTime(float t)`（回放/跳进度）/ `SetLoop(bool)`。
- `SetMorph(string name, float weight)`：驱动对应表情（blend shape）。若模型无 morph 则空操作并可在 UI 隐藏。
- `TogglePhysics(bool on)`：设 `_tm.livePhysics = on`，切换时调 `_tm.ResetPhysics()`。守卫逻辑见 §4.4。
- `ResetModel()`。
- 事件：`event Action OnModelLoaded; event Action OnClipChanged; event Action<string> OnError;`（UI 订阅）。加载失败调用 `OnError` 并保持无崩溃空状态。
- 空库/加载中：不 NullRef；`LoadModel(null)` 安全。

### 2.2 `Assets/Scripts/MMDPlayer/MMDPlaybackUI.cs`（uGUI 面板）
用 uGUI（`com.unity.ugui` 已在依赖）。Canvas + EventSystem 在场景里给好。
控件与行为：
- 模型下拉：列出扫描到的模型（来自 `MMDAssetLibrary`，见 2.5）。
- 动作下拉：列出该模型的可用 motion（没转好 clip 时为空并给提示）。
- 播放 / 暂停 / 停止 / 循环 toggle。
- 进度 Slider：随播放更新；拖拽可 `SetTime`；旁边显示 `当前秒 / 总秒`。
- 物理开关 toggle：被 `MMDPhysicsGuard` 判定不可用时置灰 + 提示文字。
- 表情 morph 下拉 + 权重滑杆：仅当当前模型含 morph 才显示。
- 「重置姿势」按钮 → `StopAndReset()`。
- 出错行 / 引导文案：无模型时显示「请把 .pmx 放入 Assets/MMDResources/Models、把 .vmd 放入 Assets/MMDResources/Motions，然后点扫描器的 Refresh」。
**UI 只做数据绑定与按钮转发，所有逻辑必须走 `MMDPlayerController`，禁止 UI 里堆业务。**

### 2.3 `Assets/Scripts/MMDPlayer/MMDOrbitCamera.cs`
挂在主相机上，目标 = 当前模型根。
- 鼠标左键拖拽 = 环绕；滚轮 = 推拉；右键/中键拖 = 平移；双击 = 复位。
- 暴露 `FocusOn(Transform t)` / `FocusOn(Bounds b)`：用模型包围盒中心/半径自动摆一个「看整体」机位（脚贴地、俯 ~15°）。
- 提供「复位视角」按钮入口供 UI 调用。

### 2.4 `Assets/Scripts/MMDPlayer/MMDPhysicsGuard.cs`
- 启动/切场景时判定物理是否可用：平台 ∈ {Windows x64, Android arm64, WebGL} **且** 能找到对应原生插件。否则视作不可用。
- 判定不可用时：强制 `MMDPlayerController.livePhysics = false`，并让 UI 把物理开关置灰 + 提示「当前平台无 Bullet 原生库（仅 Windows/Android/Web 可用），物理已关闭」。
- 调用物理前做一次可用性检查，**绝不在缺库时抛异常/崩溃**。判据来源：`Application.platform` + UMT 插件存在性（可在 EDITOR 下 `AssetDatabase`/运行时 `File` 检查插件文件，或稳妥地按平台白名单判断）。

### 2.5 `Assets/Scripts/MMDPlayer/Editor/MMDResourcesScanner.cs`（编辑器工具）
- 扫描 `Assets/MMDResources/Models/**/*.pmx` 与 `Assets/MMDResources/Motions/**/*.vmd`。
- 为每个 `.pmx` 找到其导入生成的 `PMXModel` 主资产；为匹配的 `.vmd` 准备/关联转换出的 `AnimationClip`。
- 生成/更新一个 `MMDAssetLibrary` ScriptableObject（见 2.6），让运行时/UI 有结构化的「有哪些模型、哪些动画」。
- 在 `MMDPlayerController` Inspector 提供「Refresh Library」按钮（`[CustomEditor]`），或在菜单放一个入口。点击后重建库、打印「找到 N 个模型 / M 个动作」到 Console。
- **转换 VMD→Clip 的实现取舍**：优先复用 UMT 的编辑器能力（Tools ▸ UMT ▸ VMD Clip Converter 的逻辑，或 .pmx importer 的 VMD Animations 列表）；不要重造转换轮子。若你选择在运行时用 `VMDAnimationClipConverter.Convert/ConvertAsync` 即时转换，请在报告说明并保证 `PMXModel` 与 `VMDAnimation` 对象都可用。扫描器至少要把「已就绪可播的 AnimationClip」收进库。

### 2.6 `Assets/Scripts/MMDPlayer/MMDAssetLibrary.cs`（ScriptableObject 资产索引）
- 字段：`List<PMXModel> models`；每个模型对应的 `List<AnimationClip> motions`（可用 SerializeReference 或并行 List 维护模型↔动作映射）。
- 由扫描器维护。运行时只读。

### 2.7 `Assets/Scenes/MMDSandbox.unity`（播放器场景）
- 层级建议：`MMDPlayerController`(空GO) ├ `ModelAnchor`(空子物体)；`Canvas`+`EventSystem`(挂 `MMDPlaybackUI`)；`Main Camera`(挂 `MMDOrbitCamera`)；`Directional Light`。
- 手动用 YAML 写 .unity 风险高（易坏引用）。**允许二选一**，但必须在报告说明你选了哪种、为什么：
  A) 手写一个结构正确、能被 Unity 打开的 `MMDSandbox.unity`（保证组件与引用完整、无坏 GUID）；
  B) 提供 `Assets/Scripts/MMDPlayer/Editor/MMDSandboxBuilder.cs`：一个菜单/按钮，运行后用代码把所有层级/组件/引用搭好并保存 `MMDSandbox.unity`（推荐，最稳）。
- 无论如何，交付里必须有一条「用户怎么进到这个场景」的明确路径。

### 2.8 README（仓库根 `README.md`，若已存在则更新/新建）
必须写清：
1. 简介：这是什么（Unity MMD 播放器半成品），技术栈（Unity 6.6、UMT 0.5.1、Built-in RP）。
2. 首次使用：用 Unity 6.6 打开，允许工程升级；依赖自动解析，若冲突按 §1.2 说明处理。
3. 三步上手：把 `.pmx`(含贴图) 放 `Assets/MMDResources/Models/` → 把 `.vmd` 放 `Assets/MMDResources/Motions/` → 选中播放器点「Refresh Library」→ 打开 `MMDSandbox` → Play → 下拉选模型/动作。
4. 已知限制：macOS/Linux 无 Bullet 物理；未装 lilToon 时材质为 Unlit 回退（观感非卡通）；Phase 2 玩法未实现。
5. 许可/版权提示：请用户确认所用模型/动作的分发许可。

---

## 3. UMT 用法与易错点（写码前逐条查证，禁止凭记忆）

以下全部以 `Packages/com.candidumgames.unitymmdtools/` 源码为准，**先用它核对再写**，签名不符就是你写错。

### 3.1 运行时构建模型（核心入口，已确认存在）
```
namespace UMT {
  public sealed class PMXImportOptions {
    public string sourcePath; public string sourceName;
    public string textureBaseDirectory;
    public Transform parent;              // 构建到该节点下
    public bool applyRenames = true;
    public UMTResources umtResources; public PMXRenameLists renameLists;
    public bool strictVersion = true; public bool createAvatar = true;
    public Func<PMXModel, PMXImportOptions, Texture2D[]> loadTextures;
    ...
  }
  public sealed class PMXImportResult {
    public GameObject root; public PMXModel model; public Transform[] bones;
    public PMXAvatarBuildResult avatarResult;      // .avatar
    public MMDTransformBuildResult mmdTransformResult; // .transformManager/.physicsManager
    public List<PMXImportedMesh> meshes; public List<Material> materials;
    public List<Texture2D> textures; public List<string> warnings;
  }
  public static class PMXImporter {
    public static PMXImportResult BuildUnityObjects(PMXModel model, PMXImportOptions options = null);
    public static PMXImportResult Import(string pmxFilePath, ...);
    public static PMXImportResult Import(byte[] pmxBytes, ...);
  }
}
```
- `PMXModel` 是 ScriptableObject 子资产（由 `.pmx` 导入产生）。把 pmx 资产拖进 `Assets/MMDResources/Models/` 后，它自动成为 `PMXModel`。你把这些资产的引用放进 `MMDAssetLibrary`。
- `MMDTransformBuildResult` 含 `transformManager`/`physicsManager`/`rigidBodies`/`joints` 等。

### 3.2 VMD → 可播动画（只用这个，禁止手写 K 帧）
```
public static partial class VMDAnimationClipConverter {
  public static VMDModelClipData Convert(VMDAnimation animation, PMXModel model,
      VMDAnimationClipOptions options = null, ProgressCallback progress = null);
  // 也提供 ConvertAsync(UMTFrameBudget, VMDAnimation, PMXModel, PMXAnimationPaths, ...)
}
```
- 转换结果（`VMDModelClipData`）里是可用的 `AnimationClip`，绑到模型骨骼。
- **铁律：动画只能用这个转换器产出的 clip 来播；绝对禁止你手写 VMD 关键帧、自造骨骼曲线、或猜骨骼驱动方式。**

### 3.3 播放驱动核心 `MMDTransformManager`
- 它是挂在模型上的 `MonoBehaviour`：`[DefaultExecutionOrder(10000)]`、`[ExecuteInEditMode]`、在 `LateUpdate()` 里采样骨骼→解 IK/约束→(可选)跑 Bullet 物理→回写。
- 公开字段（你只设开关，别在别处重复写骨骼）：
  `bones`、`model`、`physicsManager`、`animator`、`transformEnabled`、`solveConstraints`、`solveIK`、`livePhysics`、`doSDEFSkinning`、`sdefSkinningMode`(GPU/CPU)、`sdefComputeShader`、`solveInEditMode`。
- 常用方法：`ResetToBindPose()`、`ResetPhysics()`、`SolveTransforms(bool)`、`SolveWithPhysics(...)`、`RegisterSDEFSkinners(...)`、`DisposeRuntimeData()`。
- **时序**：它的求解在动画(Animator)写入骨骼之后跑，播放 clip 用的是 Animator，你的代码只负责在 Animator 上播 clip、设 `_tm` 的开关、调 reset。
- **物理开启会让 `MMDTransformManager` 设 `Application.targetFrameRate = 60`**。关闭物理时你应能交还帧率（或至少知道、写进 README），别把 60 锁死成玄学。
- `doSDEFSkinning` / SDEF：模型含 SDEF 顶点时用 `MMDSDEFSkinner`（GPU compute 需一个 compute shader，导入期自动 assign；没有则回退 CPU）。别在运行时误关 SDEF，否则裙摆/头发会变形。
- 若 clip 是「runtime-solved sparse + IK on/off」型，动画本身只写 FK/IK 开关，最终 IK/约束由 `_tm` 解——这正说明**必须把 clip 打在该模型对应的人形/骨骼上，配合 `_tm` 一起**，不要想当然另搞一套。

### 3.4 模型朝向/单位（务必用一个真实模型验证，勿拍脑袋）
- MMD 模型单位/轴向不统一（有的面朝 +Z，有的 +X，轴可能翻 90°）。
- 你的「摆正」逻辑要基于「跑一次真实模型后实测」，别写死错误数值。给出手动修正字段（旋转/位移/缩放偏移）并默认值先用安全猜测（脚落 y=0、面朝相机），把实测结果留给用户/你在报告里说明。

### 3.5 潜在 API 变更提醒
- Unity 6.6 下 `Burst`/`Collections`/`mathematics` 的命名空间或 API 可能有别于 UMT 编译期假设。若 UMT 源码编译不过，先判断是不是版本差异：是 → 在根 manifest 调整版本或写垫片（别改 UMT）。把最终能编译通过的依赖版本组合写回 README。

---

## 4. 架构纪律与常见坑（必须遵守）

1. **分层**：Controller(业务/状态) ← UI(纯转发/展示)。别把逻辑写进 UI；别把状态散落到各脚本。
2. **不要重复写骨骼**：骨骼求解唯一权威是 `MMDTransformManager`。你的控制器只在 Animator 上播 clip、设开关、调 reset。手动 `SampleAnimation` 设时间时注意与 `_tm` 求解顺序一致，优先用 Animator/Playable 时间控制。
3. **空态安全**：任何 `LoadModel(null)`、目录为空、加载失败都必须无异常。UI 显示引导。
4. **平台降级**（见 2.4）：物理不是必然可用。降级要优雅。
5. **不写死资源路径/名字**：所有模型/动作靠扫目录发现，绝对不写 `Models/三月七/星穹铁道—三月七3.pmx` 这类路径。仓库里没有模型二进制，别去找、别编造，也不要为了“有东西可播”去硬塞假数据。
6. **不碰 UMT 包源码**、不手写 VMD K 帧、不自造骨骼驱动。
7. **场景交付保证**：无论手写 .unity 还是用 Builder 脚本，必须保证用户能无痛进入场景并运行；坏引用 = 交付失败。
8. **防御命名冲突**：你新增的类都在 `MMDPlayer` 命名空间或 `Assets/Scripts/MMDPlayer` 下，避免和 UMT 或用户未来的代码撞名。
9. **编辑器代码放对目录**：用了 `UnityEditor` 的必须放 `Editor/` 目录（否则编译错）。asmdef 可加可不加，但要保证与你给的工具链一致、别引入循环引用。

---

## 5. 你不能运行 Unity —— 诚实与交付规范

### 5.1 禁止事项
- **禁止声称“已测试 / 已验证可运行 / 我已运行过”**。你只能做静态自查。
- 禁止编造运行结果、截图、日志。

### 5.2 静态自查清单（提交前逐条过）
- [ ] 每个引用的 UMT 类型/方法/字段，与 `Packages/com.candidumgames.unitymmdtools/` 源码签名一致（含命名空间、参数、返回）。
- [ ] 没有引用仓库里不存在的类/文件；没有拼写错误。
- [ ] Controller/UI/Guard/Camera/Scanner 之间引用关系闭合，事件都有订阅方或空安全。
- [ ] 文件路径都用相对/资产路径，没有硬编码模型名。
- [ ] Editor 脚本都在 `Editor/` 目录。
- [ ] 目录结构与 2.1~2.8 一致，无缺项。

### 5.3 交付报告（完成时必须以文本返回，结构如下）
```
① 本次新建/修改文件清单（路径 + 一句话用途）
② 验收清单逐项（抄 docs 第 8 节）：每项标注 [已实现] 或 [待用户在 Unity 6.6 验证]，没把握的一律标待验证
③ 我识别到的、无 Unity 环境无法确认的风险点（如：scene 能否打开、依赖是否在 6.6 编译通过、物理可用性）
④ 给用户的操作指引：如何用 Unity 6.6 打开 → 如何验证（放 pmx/vmd → Refresh → 进 MMDSandbox → Play → 检查播放/物理/表情/拖进度/相机）
⑤ 我做的、与规格不同的取舍及理由（如有）
```

---

## 6. 本次范围边界（再次强调）

**做**：2.1~2.8 的 MMD 播放器半成品，让其满足「丢 .pmx/.vmd 即可加载播放 + 物理 + 表情 + 播放控制 + 相机」。

**不做（Phase 2 / 后续）**：
- 巨人踩城 / 踩小人玩法。
- lilToon 卡通材质集成与渲染调优（只允许提一句“需要 lilToon 才能更好看”）。
- 多模型同屏、动作混合、视频导出。
- 运行时从任意路径加载未导入的原始 pmx（留待后续）。
- 手办摆姿/骨骼单独控制。

**收尾**：全部完成并自查后，commit（信息列明新增文件），push 回 `main`。禁止半成品交付；若时间/能力受限完成不了全部，宁可如实报告已完成部分，也不要假装完成。

# Unity 版 · 巨人踩城 重做方案 v2.1（给实现者）

> **目标仓库**：`aaa621bbb/MMDHandmade-Unity`（Unity + UMT 0.5.1 + Built-in RP，交付平台 Android arm64）
> **基线**：仓库 `main@7fabf06`（Phase1 半成品任务书 + Phase2 玩法任务书已就位，**玩法尚未落地**）
> **本文与仓库现有文档的关系**（先读这三份，再读本文）：
> 1. `TASK_给实现AI.md` = Phase1，MMD 播放器半成品（模型/动作/物理/相机/UI 的底线）
> 2. `TASK_Phase2.md` = Phase2，踩城玩法（女巨人追逐、程序小人、城市、自然物理、无敌沙盒）
> 3. `docs/Unity_MMD_半成品_方案_v0.1.md` = Phase1 的规格细节
>
> **本文不推翻上面任何一份的边界**，它是 **Phase2 的加固版 + 内容扩展版**：
> 把「能不能玩」的门槛（尺度/相机/输入/时序）讲成 Unity 可实现规格，
> 把「值不值得玩」的内容（§7.5 那一整套）按 P0/P1/P2 排好顺序。
>
> **诚实声明**：本文档全部技术结论来自 UMT 0.5.1 源码与 Unity 行为约定，**没有任何一条声称"已运行验证"**。
> §1 诊断表里的"根因"，在 Unity 侧尚未落地代码的前提下，给的是**该现象在 Unity 里最可能的机制与排查顺序**，
> 不是对某份现成代码的定性。若你手上已有一份跑不起来的 Unity 实现，请把出问题的脚本贴出来，我按真实代码再定位一次。

---

## 0. 一句话结论

现有版本不是"缺一个功能"，而是**玩法底层没有成型**：尺度没定（相机近裁面直接吃掉整个世界）、输入没接线、城市是占位几何、巨人没有状态机。
**建议重做玩法模块**，但**不要动 Phase1 的加载与播放链路**：

```
保留（Phase1，别改）        重做（Phase2 玩法，按本文写）
─────────────────────      ──────────────────────────────
PMXImporter / PMXModel      Game/ 六模块：CityKit / Player / CameraRig / GiantAI / HUD / GameMode
VMDAnimationClipConverter   Config（集中所有可调数值）
MMDTransformManager         WorldScaler（尺度系统，§3）
MMDPhysicsManager           CharacterController 玩家（§6）
MMDAssetLibrary / 扫描器    GiantessBrain 状态机 + StompDetector（§7）
```

**两条线并行推进，别串行等**：§3~§12 修底层（能不能玩），§8.5（=内容分层，见 §12 阶段 4/5）补内容（值不值得玩）。

---

## 1. 现状诊断：10 条反馈 → Unity 侧根因 → 对策

> 排查顺序就是优先级：**§3 尺度 → §5 相机 → §6 输入 → §4 城市 → §9 UI → §7 巨人**。
> §3/§5/§6 是"能不能玩"的门槛，先修完并单独验收，否则城市做得再精致也没人看得见。

| # | 反馈的现象 | Unity 里最可能的根因（都有代码级抓手） | 对策 |
|---|---|---|---|
| 1 | **是竖屏的** | `Player Settings ▸ Resolution and Presentation ▸ Default Orientation` 未设成 Landscape（或设了 Auto Rotation 但 Allowed Orientations 里画横屏没勾）；运行时也没 `Screen.orientation` 兜底；挖孔屏没关 `Render outside safe area` | §2.1 + §9（安全区） |
| 2 | **城市建模特别粗糙，只是极简纯色几何体** | 只有 `GameObject.CreatePrimitive(Plane)` 一块地 + 少量无贴图 Primitive；用默认 Standard 材质（无 albedo 贴图、无粗糙度区分） | §4 全节（模块化建筑生成器 + 程序化贴图） |
| 3 | **导入不了 PMX 模型** | 两条路都会错：①编辑器内 `.pmx` 必须经 UMT 的 ScriptedImporter 变成 `PMXModel` 资产，再走 `PMXImporter.BuildUnityObjects()` 构建；②Android 运行时导入必须 `PMXReader` + `PMXImporter.Import(bytes)` + `VMDReader`。贴图必须与 PMX 同目录。失败多半是**静默吞异常**，界面没有回显 | §8（导入管线 + 报错回显 + 归一化） |
| 4 | **UI 按钮太小** | `CanvasScaler` 留在默认 `Constant Pixel Size`，UI 按设计稿像素写死，在高 DPI 手机上物理尺寸被压缩；也没按 48dp 触摸目标规范做 | §9.1（换算公式 + 尺寸表） |
| 5 | **进游戏后什么都看不见，视角穿模到几何体** | **Unity 独有的一号杀手**：`Camera.nearClipPlane` 默认 **0.3**。如果世界被缩到 1/20（§3），那么相机前方 0.3 m 内的**所有东西（包括地面、玩家自己、整条街）都被近裁面裁掉** → 黑屏/只见天空。叠加：出生点落在几何体内部、相机无碰撞 | §3（尺度）+ §5.2（相机碰撞） |
| 6 | **移动不了** | 四个独立原因，按顺序查（§6.1）：①`Player Settings ▸ Other Settings ▸ Active Input Handling` 与新输入 API 不匹配（设为"Input System Package (New)"时 `Input.GetAxis` 会**抛异常**，看起来就是"按了没反应"）；②`CharacterController.minMoveDistance` 默认 **0.001 m**，而缩小世界后**每帧位移本身就接近 1 mm** → 控制器判定"移动量太小"直接丢弃，表现为**原地踏步/抖动**；③摇杆的 uGUI 元素 `Raycast Target` 盖住了整个屏幕，把事件吃光；④速度写死常数，没按尺度推导 | §6（控制器 + 输入区划分 + 调试读数） |
| 7 | **要第一人称/第三人称能切换** | 只有一台固定相机，没有 rig、没有模式状态 | §5.1（三档机位） |
| 8 | **镜头灵敏度要可调** | 没有设置面板；且灵敏度必须按尺度/FOV 换算，否则"调了没感觉" | §5.4 |
| 9 | **游戏完全不成型，根本玩不了，要重做** | 没有状态机边界、没有出生点校验、没有 CONFIG、没有性能预算、没有可验证 HUD | §10 架构 / §11 性能 / §12 分阶段 |
| 10 | **建模要很精致** | 无材质分层、无阴影、无街具、无绿化、无雾、无时间档；且 **UMT 材质在没装 lilToon 时会回退成 `Unlit/Texture`（完全不受光）**，观感必然扁平 | §4 全节 + §4.10 精致验收线 + §4.7（lilToon） |

---

## 2. 重做总原则（Unity 口径）

1. **横屏优先，锁死 Landscape**。
   - 编辑器：`Player Settings ▸ Resolution and Presentation ▸ Default Orientation = Auto Rotation`，**只勾 Landscape Left / Landscape Right**；取消 `Render outside safe area`（挖孔屏）。
   - 运行时兜底（`GameMode.Enter()`）：`Screen.orientation = ScreenOrientation.AutoRotation;` 且把四个 `Screen.autorotateTo*` 里只留横屏。
   - UI 一律按 1920×1080 横屏参考分辨率做，用 `Screen.safeArea` 避让刘海（§9.2）。
2. **模块独立、可单独开关、退出零残留**：`CityKit` / `Player` / `CameraRig` / `GiantAI` / `HUD` / `GameMode`。
   进入游戏 `Enter()`，退出 `Exit()` 必须还原所有被改动的 **Unity 全局状态**（清单见 §10.3，漏一个就会出现"第二次进入越来越卡/画面变暗"）。
3. **所有数值集中到 `GameConfig`（ScriptableObject 或静态类），禁止散落常数**。改手感只改一处。
4. **不做无贴图纯色**。任何可见物体至少：基础色 + 粗糙度区分 + 一张程序化贴图（窗格/砖缝/沥青/标线）。
   程序化贴图**生成一次、全场景复用并缓存**（`static Dictionary<string, Texture2D>`），不要每个物体 new 一张（爆内存 + 卡顿）。
5. **性能是硬约束**（目标机型：骁龙 8 级 / 红米 K90）：见 §11 预算表；超预算**砍数量，不砍质量分级**。
6. **保持 Built-in RP，不要切 URP**。UMT 的材质构建顺序是：`lilToon ▸ URP Unlit ▸ Built-in Unlit`（源码 `PMXMaterialBuilder.GetShader`）。工程一旦切 URP，Phase1 已调好的材质与回退链全部要重验。**要做的是装 lilToon，不是切管线。**
7. **别动 `Packages/com.candidumgames.unitymmdtools/` 里的任何源码**（Phase1 的纪律继续有效）。

---

## 3. 尺度系统（**先定这个，否则其余全是错的**）

### 3.1 结论先行：只有一条路，缩世界，不缩放巨人

**为什么不能缩放巨人**（UMT 源码级理由，不是经验之谈）：

- `MMDConstants.k_MMDUnitToUnityUnit = 0.08f`：UMT 在构建阶段就把 MMD 单位换算成 Unity 米。**一个标准 PMX 进 Unity 后身高约 1.6 m，就是真人高**。
- `MMDPhysicsManager` 创建 Bullet 上下文时把 `mmdUnitToUnityUnit = 0.08` 写进 `NativeConfig`，并在源码注释里明确说明：弹簧阻尼做了 `scale` / `scale²` 补偿，"so the motor-based 6DOF springs reproduce **MMD's unit-scale reference at Unity's meter scale**"。
  → 也就是说：**Bullet 的弹簧参数是按"Unity 米制 + 0.08 换算"这一档标定的**。你把巨人 root 缩放 10 倍，刚体惯量就变成 100 倍，**弹簧直接脱离标定区间 → 裙子/头发乱飞、抖动、穿模**。这正是"衣服乱飞"的头号原因。
- `PMXAvatarBuilder` 里唯一读 `transform.localScale` 的地方只是 Avatar 缩放适配，**缩放不会传播进 Bullet 上下文**。
- VMD/IK/SDEF 也全部假设原生尺度。

**所以：巨人 PMX 的 `transform.localScale` 永远保持 1。**
把 **城市 + 小人 + 所有道具** 放在一个 `WorldRoot` 下，用 `WorldScaler` 统一缩小，巨人站在这个缩小的世界里，自然就是巨人。

> 两种表述等价，但**代码里必须以"小人真实身高 1.7 m"为基准写，不要写 `×0.05` 这种魔法数**。
> 换巨人模型、换比例、调整手感时，改 CONFIG 一个数就够。

### 3.2 数学关系（照抄，别推导错）

```csharp
// GameConfig
public const float GiantNativeHeight = 1.6f;   // UMT 换算后 PMX 的实际身高(米)，实测后写死
public float tinyRealHeight   = 1.70f;         // 小人"现实中"的身高(米) —— 一切推导的基准
public float tinyUnityHeight  = 0.085f;        // 小人最终在 Unity 世界里的身高(米) ≈ 8.5 cm
public float worldScale => tinyUnityHeight / tinyRealHeight;   // ≈ 0.05  (1:20)
public float giantHeightEquivalent => GiantNativeHeight / worldScale; // ≈ 32 m —— 巨人的"体感身高"
```

| 量 | 推导公式 | 默认值（1:20 档） |
|---|---|---|
| `worldScale` | `tinyUnityHeight / tinyRealHeight` | **0.050** |
| 巨人体感身高 | `GiantNativeHeight / worldScale` | **32 m**（10 层楼） |
| 小人 Unity 身高 | 直接配置 | **0.085 m** |
| 小人体感速度（正常走） | `1.4 * tinyRealHeight / 1.7`（≈0.82 身高/秒） | 1.4 m/s（体感），**Unity 里 0.070 m/s** |
| 小人体感速度（跑） | 走 ×1.8 | 2.52 m/s → Unity 0.126 m/s |
| 小人重力 | `Physics.gravity.y * worldScale`（见 §3.3） | −0.49 m/s² |
| 相机近裁面 | `0.02 ~ 0.05 * worldScale` 再按实测微调 | **0.004 ~ 0.008**（但要防深度精度问题，见 §3.3） |
| 相机远裁面 | 城市场景可视距离 / `worldScale`（体感 800 m） | 40 m（Unity 实际单位） |

**出生点校验（强制）**：在候选点做 ① 向下 `Physics.Raycast` 确认脚下有地面（距离 < 0.05 m）；② `Physics.OverlapCapsule`（半径 = `tinyUnityHeight*0.25`，高 = `tinyUnityHeight`）确认为空；③ 头顶 2 倍身高净空。任一不满足就换点，最多 20 次，最后兜底到"城市中央广场预设安全点"。**连续重开 20 次不得有一次卡在几何体里。**

### 3.3 缩放后**必须跟着改**的 Unity 全局量（漏一个就出怪现象）

> 这一节是 Unity 与网页版最大的差别：Unity 有一堆"默认按米制调好的全局参数"。
> `WorldScaler.Apply()` 里集中设置，`Exit()` 时在 §10.3 的还原清单里还回去。

| 类别 | 参数 | 默认（米制） | 缩放后 | 不做的后果 |
|---|---|---|---|---|
| **相机** | `Camera.nearClipPlane` | 0.3 | `0.003~0.01`（实测） | **整个世界被近裁面裁掉 = "什么都看不见"** |
| | `Camera.farClipPlane` | 1000 | 30~60 | 远景被裁 / 深度精度浪费 |
| **光照** | `QualitySettings.shadowDistance` | 40~150（随质量档） | ×`worldScale` 后再收紧到能盖住近景 | 阴影精度全浪费，巨人阴影糊成一团 |
| | `Light.shadowBias` / `shadowNormalBias` | 0.05 / 1.0 | 缩到 1/10~1/50 | shadow acne 或 peter-panning |
| | `QualitySettings.shadowResolution` | 视质量档 | 视预算（§11） | 阴影锯齿 |
| **物理** | `Physics.gravity` | (0,−9.81,0) | `× worldScale`（推荐）或按手感调 | 小人像在月球上飘，跳跃高度与身高不成比例 |
| | `Physics.defaultContactOffset` | 0.01 | `0.01 * worldScale`（≈5e-4） | 小物体抖动/互相穿透 |
| | `Physics.sleepThreshold` | 0.005 | `× worldScale` | 小物体"粘住"或永不休眠 |
| | `Time.fixedDeltaTime` | 0.02 | 保持 0.02（**别动**） | 改步长会连带 Bullet 时序，得不偿失 |
| **玩家** | `CharacterController.skinWidth` | 0.08 | **必须是半径的 5~10%**（≈0.002） | 默认 0.08 ≈ 整个小人身高 → 卡住/穿墙/贴地抽动 |
| | `CharacterController.minMoveDistance` | 0.001 | **0**（强制） | 每帧位移 < 1 mm 时控制器丢弃移动 = **"移动不了"** |
| | `CharacterController.stepOffset` | 0.3 | ≈0.3 × 小人身高（抬腿上台阶用，别设 > 身高） | 小人直接穿过路缘石或卡住 |
| | `CharacterController.slopeLimit` | 45° | 45~55（按城市场景调） | 走不上人行道斜坡 |
| **粒子/音效** | 粒子初始尺寸/速度 | 米制 | ×`worldScale` | 扬尘像云一样大 |
| | `AudioSource.minDistance/maxDistance` | 1/500 | ×`worldScale` | 巨人脚步没有"由远及近"的层次 |
| | 相机震动幅度 | 米制 | ×`worldScale` | 震动把画面甩出屏幕 |
| **UI** | Canvas 参考分辨率 | 任意 | **固定 1920×1080**（与尺度无关，独立体系） | 按钮在不同机器上大小不一 |

**浮动精度保险**：`worldScale=0.05` 时坐标绝对值最大约 40 m，`float` 完全够用，**不需要 double**。
但**必须**：城市生成后把 `WorldRoot` 放在原点附近（不要在城市远端再挂缩小根），且出生点选在城市中心区。

### 3.4 验收

1. 进入游戏第一帧能看到地面与街道（不是黑屏、不是纯色）。
2. 小人站在街上，头顶到巨人脚踝的视觉比例 ≈ 1:20（截图能量出来）。
3. 相机贴地时不会穿进地面，抬头能看到巨人全貌。
4. 连续重开 20 次，出生点无一次卡在几何体内。

---

## 4. 城市美术技术规范（Unity 版）

> 硬要求：**不引入任何外部模型/贴图文件**（包体可控、无版权风险）。
> 全部用运行时**程序化生成 Mesh + 程序化生成 Texture2D**（`new Mesh()` / `new Texture2D()`），生成一次、缓存复用。
> 精致 ≠ 面数多，而是**层次、重复中的变化、材质、光影**。

### 4.1 生成方式选型（Unity 侧的正确做法）

- **一栋楼 = 一个合并好的 Mesh**（基座/标准层/退台/女儿墙/屋顶道具在构建时 `CombineInstance` 合并），每种材质一个 `MeshRenderer`。
  这样一栋楼通常 2~5 个 draw call，几十栋楼也稳。
- **合并网格注意**：顶点数可能超 65535，**必须 `mesh.indexFormat = IndexFormat.UInt32`**，否则截断花屏。
- **重复街道家具**（路灯/锥形桶/护栏段/树）：用 `Material.enableInstancing = true` + `Graphics.DrawMeshInstanced`（或 `MeshRenderer` 开 instancing）；
  **shadowCastingMode 要显式设**（`ShadowCastingMode.On/Off`）——路灯这种细杆可以关投影省性能。
- **不要用 `GameObject.CreatePrimitive` 拼城市**：它带默认 Collider + 默认材质，是"纯色几何体"味道的来源。
- **贴图生成**：`new Texture2D(256,256, TextureFormat.RGBA32, mipChain:true)`，`SetPixels32` + `Apply()`，`wrapMode = Repeat`。
  按 `"building_commercial_a"` 这类 key 缓存复用；Android 上等价的压缩由 Build Settings 的 ASTC 负责（运行时生成的贴图不进 ASTC，**注意控制张数与分辨率**：总量建议 ≤ 12 张 256²/512²）。

### 4.2 建筑生成器（核心）

不要"随机一个盒子"。做一个**模块化建筑生成器**，一栋楼 = 基座 + 标准层 + 顶部，各自独立随机：

- **体量**：宽 8~20 m、进深 8~20 m、高 12~80 m；支持**退台**（上层内缩，形成阶梯轮廓）与**双体量拼合**（让轮廓不是单一立方体）。
- **立面**：程序化窗格贴图 —— 按层高与开间画窗洞，**随机点亮 15~30% 的窗**（夜景关键）；加窗台线、腰线、竖向分隔条。
- **底座（1~2 层）**：商铺/大堂，独立材质（玻璃门 + 暖色内透光 + 招牌色块 + 遮阳棚），与上部立面明确区分。
- **顶部**：女儿墙（0.5~1 m）+ 随机屋顶道具：水箱、空调外机、通风管、天线、电梯机房。
- **变化来源**（避免"一眼 AI 复制粘贴"）：色调从**分区色板**取（商业区偏冷灰蓝 / 住宅区偏暖米 / 老城区偏黄褐）、窗格密度、退台层数、屋顶道具组合。
- **布局**：主街两侧各 6~10 栋，后排 1~2 圈做层次；**沿街立面必须贴合街道边界**（不能歪进马路）。用栅格抢占式布局，保证没有楼压到路面/人行道。
- **远景层**：300~500 m 外放一圈**低模剪影楼群**（无窗、单色、受雾影响）撑天际线。近处细节 + 远处剪影 = 城市感。

### 4.3 路面 / 人行道

- **车道**：沥青深灰 + 程序化贴图（车道虚线/实线、磨损、油渍、补丁），主干道 2~4 车道。
- **人行道**：**抬高 0.15 m**（路缘石），用不同铺装格子贴图（砖缝）。这条"高差"是廉价但极其有效的"城市感"来源。
  ⚠️ **与 UMT 物理的冲突（务必知道）**：UMT 的 Bullet 地面是**一个无限平面**（`MMDBulletPhysics.BuildGround`：`groundNormal` + `groundConstant`），**不是任意 Collider**。
  → 巨人裙摆/头发**只和这个平面碰撞，不会和人行道、楼体碰撞**。所以：给楼加 Collider 解决不了裙摆穿模。
  正确做法：① 让巨人主要在**车道平面（y=0）**活动；② 人行道高差控制在 0.15 m 以内并接受轻微穿插；③ 需要更严时按区域切换 ground 高度（`enableGroundCollision` / groundConstant），但**先按 ①② 做，别一上来就动态改物理地面**。
- **路口**：十字/丁字 + **斑马线** + 停止线 + 转向箭头（贴花纹理，不用几何）。
- **附属**：井盖、雨水篦子、路缘石坡道。
- **Collider**：车道/人行道各一个 `MeshCollider`（或 BoxCollider 拼接），**必须有**，否则小人掉出世界。

### 4.4 街道设施（决定"精致度"的关键，别省）

沿街**按固定间距自动布置**，每类要有细节而不是一个色块：

- **路灯**：灯杆（锥台）+ 弯臂 + 灯头；夜间**灯头发光材质 + 地面一片光斑贴花**（不要真点光源，移动端一个实时光就掉一半帧）。
- **行道树**：树干（细锥台）+ 树冠（2~3 层不规则低模，或**十字交叉 + alpha 叶片面片**，后者更省面更自然）；树下加树池。
- **信号灯 / 路牌 / 指示牌**：立柱 + 牌面（程序化画指针/文字块）。
- **街道家具**：长椅、垃圾桶、消防栓、公交站牌/雨棚、花坛、护栏（沿路缘成段）。
- **电线杆 + 电线**：老城区段加，电线用 `LineRenderer` 拉下垂弧线（成本极低，观感提升明显）。
- **广告牌 / 灯箱**：一面自发光程序化图形，夜景最出效果。

> 这些不是装饰，是**玩法资源**：垃圾桶可藏身、售货机可推倒挡路、消防栓被踩会喷水柱、交通锥可以被踢飞。

### 4.5 绿化 / 天空 / 雾 / 时间档

- 绿化：行道树 + 街角灌木丛（低模簇）+ 草地/花坛（不同色贴地材质）。可选极少量落叶粒子。
- 天空：**渐变天空盒**（顶部深蓝 → 地平线暖橙/淡青），按时间档切换；或用 `RenderSettings.skybox` 的程序化 Cubemap（生成一次缓存）。
- **雾必须开**：`RenderSettings.fog = true; fogMode = FogMode.ExponentialSquared;` 密度让体感 300 m 外明显衰减。既出纵深又遮远景接缝。
- **时间档**：白天 / 黄昏 / 夜晚（**建议默认黄昏或夜晚**，巨人题材最出气氛）。切档要同时改：太阳角度与颜色、环境光、雾色、窗格贴图的"点亮比例"、自发光强度。

### 4.6 光照与阴影（Built-in RP 具体做法）

- **主光**：一个 `Directional Light` 做太阳，**必须开阴影**（`light.shadows = ShadowType.Soft`，`shadowStrength` 调 0.7~0.9）。
  **巨人投在街道和楼上的巨大阴影是"巨人感"的核心来源之一，优先级最高**，比城市细节优先级更高。
- **UMT 材质的重要事实（源码级）**：工程没装 lilToon 时，PMX 材质会回退成 `Unlit/Texture`（完全不受光照）。
  这意味着：**巨人自身不会有明暗变化（观感扁平），但它依然能投影**（只要 `renderer.shadowCastingMode = On`，Unlit 不影响投影）。
  → 所以：① 城市受光（Standard）；② 巨人在阴影里也"有存在感"；③ 要真正好看，**装 lilToon**（免费、MIT、支持 Built-in RP），UMT 会自动优先使用它并获得描边（edge）特性。
- **环境光**：`RenderSettings.ambientMode = AmbientMode.Trilight`（天空色/地平线色/地面色），避免暗部纯黑。
- **夜景补光**：路灯/招牌用自发光材质 + 一层暖色后处理（见 §11 的"轻后处理"取舍），不要加实时光。
- **接触阴影**：小人脚下加一个软圆贴花（一张带 alpha 的 Quad，稍高于地面），消除"悬浮感"。

### 4.7 材质（不许纯色）

每个材质至少：`albedo`（贴图或分块色）、`roughness`（沥青粗糙、玻璃光滑）、`metallic`（金属街具）。
城市用 **Standard shader**（Built-in RP，支持阴影与光照）；夜景自发光用 `_EmissionColor` + `EnableKeyword("_EMISSION")`。
贴图运行时生成、缓存复用，**不要每个物体各画一张**。

### 4.8 批处理与性能（城市部分）

- 合并网格（见 §4.1）+ GPU Instancing（重复件）+ `MeshRenderer.shadowCastingMode` 分级。
- 材料数量控制在 **8~12 种**（建筑/玻璃/沥青/人行道/绿化/金属/自发光/贴花…），材质种类 = draw call 下限。
- 远景楼群合并成一整块网格（1~2 个 draw call）。
- 贴图统一 256~512，开 mipmap，`anisoLevel` 2~4。

### 4.9 "精致"的客观验收线（必须逐条打勾）

1. 站在街上看四周：能看到**至少 6 栋造型各异的楼**（不是同一盒子复制）。
2. 楼有**可辨认的窗格**，且**部分窗户亮着**。
3. 人行道与车道**有高差**，路面有**车道线/斑马线**。
4. 街上有**路灯、树、至少两种街道家具**。
5. 有**雾**，远处有**天际线剪影**，天空不是纯色。
6. **巨人的影子投在街上**，小人脚下有接触阴影。
7. 夜晚档能一眼看出"这是晚上"（窗光 + 路灯发光 + 整体色调）。
8. 截图不看 HUD，别人能认出"这是一条城市街道"，而不是"一堆盒子"。
9. **`Stats` 面板**里 `SetPass calls` 和 `Batches` 落在 §11 预算内（这条把"精致"和"跑得动"绑在一起）。

---

## 5. 相机系统（修"看不见/穿模" + 加切换）

### 5.1 三种机位（一个 `CameraRig` 统一管理）

| 模式 | 说明 |
|---|---|
| **FPS 第一人称** | 眼高 = `tinyUnityHeight * 0.92`；抬头能看到巨人全貌（这玩法最爽的视角）。 |
| **TPS 第三人称** | 弹簧臂跟在角色后上方，体感距离 2~8 m（Unity 单位里要 × `worldScale`）；肩后偏移；能看到自己的小人。 |
| **观察 / 自由** | 环绕轨道，用来欣赏巨人全身；也用于拍照/摆姿联动。 |

- 切换：HUD 上一个**大按钮**循环切换；进入游戏默认 **FPS**。
- 三档共用一台 `Camera`，只是控制策略不同；**不要建三台相机来回启用**（会带来 AudioListener / 深度 / 后处理的三份状态）。

### 5.2 相机碰撞（**直接解决"穿模"**）

- TPS：从角色头部向相机目标位做 `Physics.SphereCast`（半径取相机近裁面的 2~3 倍），命中就拉到命中点之前（留余量）；
  **LayerMask 必须排除玩家自身层**，否则会被自己头顶挡住。
- FPS：`nearClipPlane` 按 §3.3 缩放；**玩家自身网格不参与渲染**（`ShadowCastingMode.ShadowsOnly`，保留影子但不露内壁）。
- 任何情况下相机**不得低于地面**：每帧做一次向下射线，把相机 y 钳到地面之上（余量 ≥ 近裁面）。

### 5.3 时序（**Unity 专属坑，必看**）

`MMDTransformManager` 是 `[DefaultExecutionOrder(10000)]` 且在 `LateUpdate()` 里求解并回写骨骼。
→ **任何依赖骨骼最终位置的东西**（相机跟随巨人的视线、踩踏判定、贴花位置）都必须排在它之后：
- 自写脚本设 `[DefaultExecutionOrder(10001)]` 并在 `LateUpdate` 里读；或
- 在 `WaitForEndOfFrame` / 下一帧 `Update` 里读（1 帧延迟，肉眼不可见）。

**写反了的典型症状**：相机跟随抖动、脚下贴花滞后一帧、踩踏判定永远慢半拍。

### 5.4 相机设置面板（修"灵敏度要可调"）

一个抽屉，全部**大滑杆 + 实时生效 + `PlayerPrefs` 持久化**：

| 参数 | 范围 | 默认 |
|---|---|---|
| 水平灵敏度 | 0.2x ~ 5.0x | 1.0 |
| 垂直灵敏度（**独立**） | 0.2x ~ 5.0x | 1.0 |
| 反转 Y 轴 | 开关 | 关 |
| 视角阻尼 / 平滑 | 0 ~ 1 | 0.15 |
| FOV | 60° ~ 100° | 70°（FPS）/ 60°（TPS） |
| TPS 距离（体感 m） | 2 ~ 8 | 4 |
| TPS 肩部偏移 | −1 ~ 1 | 0.5 |
| 振动/震动强度 | 0 ~ 1 | 0.6 |

> 灵敏度必须与 FOV 联动换算（`sensitivity * (fov / 60)`），否则改 FOV 后手感会变。
> **Cinemachine 可用但非必需**：Phase1 已有自写 `MMDOrbitCamera`，玩法相机建议自写（可控、无额外依赖）；若用 Cinemachine，注意它与 `[DefaultExecutionOrder(10000)]` 的更新时序关系要额外验证。

---

## 6. 玩家控制器与输入（修"移动不了"）

### 6.1 接线排查五步（按此顺序，别跳）

1. **先眼见为实**：屏幕上放调试读数（速度 / 坐标 / `isGrounded` / 相机模式 / 输入原始值）。
2. **`Player Settings ▸ Other Settings ▸ Active Input Handling` 与代码 API 必须一致**：
   - 设为 `Input Manager (Old)` 或 `Both` → 代码里可用 `Input.GetAxis("Horizontal"/"Vertical"/"Mouse X"/"Mouse Y")`；
   - 设为 `Input System Package (New)` → 老 API 会抛 `InvalidOperationException`，**症状就是"按了没反应"**；此时必须用 `InputAction`/`PlayerInput`，或把设置改成 `Both`。
   - 建议：**Phase2 统一用老 Input Manager 或统一用 New Input System，不要混用**；文档里写明选了哪个。
3. **`CharacterController.minMoveDistance = 0`**（默认 0.001 m 在缩小世界里等于"禁止移动"）；`skinWidth` 按 §3.3 改小。
4. **输入区是否被 uGUI 吃掉**：摇杆/按钮之外的所有全屏元素，`Image.raycastTarget` 必须关掉；视角拖动区用一个 `EventTrigger`/`IDragHandler` 的透明层实现，**与摇杆矩形不重叠**。
5. **速度是否按尺度推导**：禁止写死 `1.4f` 直接用（那在 Unity 单位里是 1.4 m/s = 体感的 20 倍速，小人会像子弹）。用 §3.2 的公式。

### 6.2 控制器选型

- **推荐 `CharacterController`**：站立/斜坡/台阶稳定，不需要每帧管刚体。
- 参数表（全部进 CONFIG）：`radius = h*0.25`、`height = h`、`skinWidth = radius*0.08`、`minMoveDistance = 0`、`stepOffset = h*0.3`、`slopeLimit = 50`。
- 重力自己算（`verticalVelocity += gravity * dt`，`isGrounded` 时清零），**不要依赖 `Physics.gravity`**，因为 `Physics.gravity` 已被缩放（§3.3）——两者一致即可，但控制器内部重力更可控。
- 移动：`controller.Move(dir * speed * dt + Vector3.up * verticalVelocity * dt)`，每帧一次，放 `Update`。

### 6.3 输入区划分（横屏）

```
┌──────────────────────────────────────────────────────────┐
│ [退出] [重开]      ⏱ 存活 42s        [无敌] [设置] [视角] │ 顶栏 ≥ 高 56dp
│                                   ╭──────────────────╮   │
│                                   │ 调试读数(可关)    │   │
│                                   ╰──────────────────╯   │
│                        （游戏画面）                        │
│  ╭──────╮                                                │
│  │ 摇杆 │                                    ┌─────────┐ │
│  ╰──────╯                                    │ 动作按钮 │ │
└──────────────────────────────────────────────────────────┘
   ↑ 左下 Φ140~160dp          ↑ 右下：动作（翻滚/道具）
   视角拖动区 = 其余区域（透明层，raycastTarget 开启，但不与摇杆矩形重叠）
```

- 摇杆：uGUI 拖拽实现（`IDragHandler`），`Handle` 用 `RectTransformUtility.ScreenPointToLocalPointInRectangle` 换算，**死区 0.15**，输出归一化向量给控制器。
- 视觉反馈：摇杆底盘半透明 + 手柄高亮；松手回中。
- `EventSystem` 场景里只要有**一个**（重复的会导致输入诡异）。

### 6.4 移动手感

- 加速/减速插值（`Vector3.SmoothDamp` 或自写加速度），不要瞬间启停。
- 行走 / 奔跑两档；**闪避翻滚**（短冲刺 + 0.3~0.5 s 无敌帧）是躲踩的核心手感，P0。
- TPS 下角色朝向平滑转向；FPS 下自身 `ShadowsOnly`。

---

## 7. 巨人系统（Unity/UMT 版）

### 7.1 加载链路（不要自己造）

```csharp
var options = new PMXImportOptions { parent = giantAnchor, createAvatar = true, applyRenames = true };
PMXImportResult r = PMXImporter.BuildUnityObjects(modelAsset, options);
// r.root / r.mmdTransformResult.transformManager / r.mmdTransformResult.physicsManager
var tm = r.mmdTransformResult.transformManager;   // 骨骼求解唯一权威
Animator anim = r.root.GetComponent<Animator>();
```

- 摆放归一化：按包围盒把脚底对齐 y=0、朝向统一（与场景约定一致，面向 −Z 或 +Z 二选一）、记录身高供 §3.2 使用。
- **绝不要自己写 VMD 关键帧、不要自己采样骨骼**（Phase1 纪律，继续有效）。

### 7.2 状态机（时间驱动，别用帧数）

`Idle/Patrol → Detect(发现) → Chase(追) → Windup(逼近+抬脚预备) → Stomp(踩下) → Recover(收脚) → Patrol`

- 每个状态有 **enter/exit + 最小持续时间 + 过渡时长**，用 `Time.deltaTime` 累加，不用帧计数。
- **俯视盯人**：追你时头骨下压、视线跟向玩家（"追着踩"的观感关键）。
- 发现判定：**视线锥（角度+距离）+ 听觉（跑步声半径）**，为潜行玩法的"失去目标"打基础。

### 7.3 步态（Unity 侧的安全做法）

**优先级 1：用 VMD 循环 clip。**
`VMDReader` 读 `.vmd` → `VMDAnimationClipConverter.Convert(...)`（或 `ConvertAsync(UMTFrameBudget, ...)` 做分帧转换，避免加载卡顿）→ 得到 `AnimationClip` → 用 `Animator` 播。走路用"走路 VMD"、踩踏用"踩踏 VMD"，做交叉淡入淡出。

**优先级 2：没有合适 VMD 时，驱动 MMD 的 IK 目标，不要写骨骼曲线。**
MMD 模型的足部由 IK 控制（`MMDBoneTransform.ik` / `MMDIKHandleData`）。做法：
- 找到 `足IK` / `つま先IK` 对应的 `MMDBoneTransform`（`applyRenames=true` 后可按重命名表匹配，或用 `PMXBone.Flags` 里的 IK 标志筛选）；
- 在 **`Update`**（早于 `[DefaultExecutionOrder(10000)]` 的求解）里把 IK 目标 Transform 抬起来/往前挪 → `MMDTransformManager` 在 `LateUpdate` 求解时读到的就是新目标；
- 左右脚交替 + 与平移速度同步。

**关键：步频必须匹配平移速度**，否则脚底打滑，一眼假：

```
speed = stride * cadence          // stride = 单步跨距(米), cadence = 每秒步数
// 例：stride = 8 m(体感), cadence = 0.5 步/秒  → speed = 4 m/s(体感) → ×worldScale = Unity 单位速度
```

改速度时**先改 cadence 的动画播放速度，再算 speed**，别让两者各自独立。

### 7.4 踩踏判定（`StompDetector`）—— 可靠、自然、不过度

- **脚掌判定器挂在 `足首` 骨下**（作为骨骼的子物体，随骨更新世界位）。
- **读位置的时机必须在 `MMDTransformManager`(10000) 求解之后**：`[DefaultExecutionOrder(10001)]` 的 `LateUpdate`，或下一帧 `Update`（§5.3）。
- **两段判定同时成立才命中**：① 该脚处于 `Stomp` 且判定器世界 y **持续下降**；② 小人中心投影落在判定器水平半径内。
- 命中给 **1.5~2 s 免伤**，避免一帧连续判定。
- **无敌模式 ON**：仅视觉反馈（扬尘/震动），不死无惩罚。**OFF（沙盒默认）**：被踩 → 击退 + 重生到附近安全点（不清城市、不重载巨人）。**不做强制 Game Over**。
- 要有**预备动画与延迟**（抬脚 → 停顿 → 踩下），让人"看得清、躲得开"，禁止随机秒。

### 7.5 物理自然性（用户最强调的一条）

1. **不缩放巨人**（§3.1，UMT 弹簧按米制标定）。
2. `tm.livePhysics = true`；切换时 `tm.ResetPhysics()`。
3. **平台支持检查**：Bullet 原生库只有 `Plugins/Windows/x64`、`Plugins/Android/arm64-v8a`、`Plugins/Web` 三份。
   → **Android 构建必须 `Target Architectures = ARM64`**（只勾 ARM64；勾了 ARMv7 而只有 arm64 的 .so，运行时会缺库）。
   → **macOS/Linux 编辑器没有物理**：这是预期，`MMDPhysicsGuard` 的 Unity 版要按 `Application.platform` + 插件存在性判定并**优雅降级**（置灰开关 + 提示），绝不能崩。
4. **地面碰撞是无限平面**（§4.3）：楼体 Collider 不会挡住裙摆。别把"裙摆穿楼"当成 collider 没加的问题。
5. **`MMDTransformManager` 会把 `Application.targetFrameRate` 设成 60**（`ShouldRunLivePhysics()` 时）。
   → `Exit()` 时要**交还帧率**，否则退出玩法后整个 App 被锁 60（或与设置项冲突）。
6. 若某个 PMX 的裙子刚体参数本身很差（抖/飞），给一个"布料物理强度/关闭"的调节与安全预设；但**默认先把"尺度正确 + 地面碰撞 + livePhysics"做对**，不要用"关物理"掩盖问题。
7. `tm.doSDEFSkinning` 保持开启（裙摆/头发形状依赖 SDEF），除非设备确实跑不动（那时按 §11 的取舍顺序处理）。

### 7.6 演出

- 落地扬尘：粒子数**硬上限 200 同屏**，尺寸按 §3.3 缩放。
- 屏幕震动：相机偏移（幅度 × worldScale）或 `Handheld.Vibrate()` / `AndroidJavaObject` 调系统振动（受用户"震动强度"设置控制）。
- 音效：`AudioSource` 3D 空间化，巨人脚步**要有方位感与由远及近的层次**（`minDistance/maxDistance` 按尺度缩放）。

---

## 8. 模型导入（修"导入不了 PMX"）

### 8.1 编辑器内导入（开发期主力）

1. `.pmx` + 贴图放进 `Assets/MMDResources/Models/<模型名>/`，**贴图必须与 PMX 同目录**（子目录需保持相对结构），中文文件名要确认编码正常。
2. UMT 的 ScriptedImporter 会把 `.pmx` 变成 `PMXModel` 资产；构建时走 `PMXImporter.BuildUnityObjects()`（§7.1）。
3. `.vmd` 放 `Assets/MMDResources/Motions/`，用 Phase1 的扫描器/转换器产出 `AnimationClip`，进 `MMDAssetLibrary`。

### 8.2 Android 运行时导入（用户自己的模型，Phase2 范围）

- **UI 路径**：设置页 →「导入模型」；导入后出现在模型列表顶部。
- **SAF 的现实问题（必须解决的坑）**：Android 的单文件选择器（`ACTION_OPEN_DOCUMENT`）**只给文件，不给整个目录** → 拿不到同目录的贴图。
  两条可行方案，建议**两条都做**：
  1. **`ACTION_OPEN_DOCUMENT_TREE` + 目录树遍历**（`AndroidJavaObject` 调 `DocumentFile.listFiles()`，递归把 `.pmx` 与贴图复制到 `Application.persistentDataPath/Models/<name>/`）；需要用户授权整个文件夹。
  2. **让用户选一个 `.zip`**（PMX + 贴图打包），用 `System.IO.Compression.ZipArchive` 解到 `persistentDataPath`。**这条最省事、成功率最高，建议作为默认引导**。
- **运行时构建**：`PMXReader` 读字节 → `PMXImporter.Import(byte[] pmxBytes, ...)`（贴图通过 `PMXImportOptions.loadTextures` 回调提供）→ `.vmd` 走 `VMDReader` + `VMDAnimationClipConverter.Convert/ConvertAsync`。
- 中文/空格路径、大小写、BOM 都要在导入管线里统一处理。

### 8.3 失败必须报错（禁止静默）

把这几项显示在界面上（Phase1 的 `OnError` 事件是现成的）：

- 读取失败的**具体异常文本**；缺失贴图**数量与文件名**；骨骼数；模型包围盒尺寸与换算后身高。
- 故意放一个贴图缺失的模型，界面必须给出明确提示，而不是黑屏/灰模。

### 8.4 归一化（导入后自动）

脚底对齐地面（`y=0`）、朝向统一、缩放为 1（**绝不缩放**）、记录身高 → 回填 §3.2 的 `GiantNativeHeight`。

### 8.5 验收

- 至少用 **2 个不同 PMX**（如昔涟 / 流萤）分别验证：能导入、有材质、站在地上不悬空不陷地、身高符合 1.6 m 量级、物理裙摆自然。
- 运行时装一个**手机本地**的 PMX（用 §8.2 的 zip 或目录方案）验证成功，并验证失败时的报错回显。

---

## 9. UI / HUD 规范（修"按钮太小"，横屏）

### 9.1 尺寸硬标准与换算

- 触摸目标最小 **48×48 dp**；主操作按钮（退出/重开/切换视角/无敌）**64~80 dp**；元素间距 ≥ 12 dp。
- **换算公式**（用 `CanvasScaler` 之后写 UI 的正确姿势）：

```
CanvasScaler: Scale With Screen Size, Reference Resolution = 1920×1080, Match = 0.5
设备像素 → 参考分辨率像素：px_ref ≈ dp * (1080 / 设备可用高度dp)
典型 6.7" 1080p 手机：可用高度 ≈ 360 dp  →  1 dp ≈ 3 px_ref
∴ 48 dp ≈ 144 px_ref ；64 dp ≈ 192 px_ref ；80 dp ≈ 240 px_ref
```

> 所以：**HUD 里的按钮 `RectTransform` 尺寸不要低于 150×150（参考像素）**。滑杆轨道高 ≥ 18，滑块 ≥ 72。

### 9.2 安全区（横屏挖孔/刘海）

```csharp
var sa = Screen.safeArea;                       // 屏幕像素空间，原点左下
var min = sa.position;
var max = sa.position + sa.size;
min = RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, min, null, out var lmin) ? ... ;
// 简化做法：把 canvasRT 的 anchorMin/anchorMax 换算成 sa 的归一化比例
canvasRT.anchorMin = new Vector2(min.x / Screen.width,  min.y / Screen.height);
canvasRT.anchorMax = new Vector2(max.x / Screen.width,  max.y / Screen.height);
canvasRT.offsetMin = canvasRT.offsetMax = Vector2.zero;
```

并在 `Player Settings` 里**取消 `Render outside safe area`**。

### 9.3 布局（横屏）

见 §6.3 的示意图。要点：左下摇杆（Φ140~160 dp）、右下动作按钮、右上/左上状态与按钮、**视角拖动区不与摇杆重叠**、设置抽屉从侧边滑出（宽 ≤ 360 dp）。

### 9.4 视觉

- 半透明深色底 + 细边框；文字加描边/阴影，保证亮暗场景都可读。
- 状态提示（"被踩到了！"）用**大字 + 短时**，别用小字长句。
- 未锁横屏时至少要有"请横屏使用"提示页（本方案直接锁横屏）。
- 一套 UI 材质走 `UI/Default` 的变体，别混用 custom shader。

---

## 10. 架构重构（把"游戏"和"模型查看器"彻底分开）

### 10.1 目录结构

```
Assets/Scripts/
  MMDPlayer/            ← Phase1 原样保留（MMDPlayerController / UI / OrbitCamera / Guard / Library / Scanner）
  Game/
    Config/             GameConfig.cs (ScriptableObject) + 各配置资产（CityPreset / Tier / ItemDef / AchievementDef）
    Core/               WorldScaler / SpawnValidator / ProceduralTextureCache / ObjectPool / DebugHud
    City/               CityGenerator / BuildingFactory / RoadBuilder / StreetProps / Vegetation / SkyTimeOfDay
    Player/             TinyPlayerController / VirtualJoystick / DodgeRoll
    Camera/             CameraRig (FPS/TPS/Orbit) / CameraCollision
    Giant/              GiantLoader / GiantessBrain / GiantLocomotion(IK targets) / StompDetector / GiantVfx
    HUD/                GameHud / SettingsDrawer / ResultPanel / SafeAreaFitter
    GameMode.cs         ← 组合以上，提供 Enter(mode)/Exit()/Restart()
    Game/Editor/        GameModeSceneBuilder.cs（用代码搭场景，避免手写 .unity 坏引用）
```

- 编辑器脚本必须在 `Editor/` 目录下（否则编译错）。
- 可选 asmdef：`Minis.Game`、`Minis.Game.Editor`；若加，注意别与 UMT 形成循环引用，且 IL2CPP 剥离需要 `link.xml`（§11.4）。

### 10.2 `GameMode` 的职责

- `Enter(mode)`：隐藏 Phase1 的播放 UI → 生成城市 → 放置小人（**出生点校验**）→ 加载巨人 → 切相机 → 应用 `WorldScaler` → 挂 HUD → 打开调试读数（可选）。
- `Exit()`：**逐块销毁并还原**（见 10.3），保证"模型查看器"界面回来时和没进过游戏一样。
- `Restart()`：只重置玩法状态（城市保留、巨人保留）。

### 10.3 **必须还原的 Unity 全局状态清单**（漏一个就出玄学 bug）

| 类别 | 项 |
|---|---|
| 渲染 | `RenderSettings.fog / fogColor / fogDensity / fogMode`、`ambientMode / ambientLight / ambientSkyColor / ambientEquatorColor / ambientGroundColor`、`RenderSettings.skybox`、`Light`（太阳）强度/颜色/阴影设置 |
| 质量 | `QualitySettings.shadowDistance`、`shadowResolution`、`antiAliasing`、`vSyncCount`、`Application.targetFrameRate`（**UMT 会设成 60**） |
| 物理 | `Physics.gravity`、`defaultContactOffset`、`sleepThreshold`、`Physics.queriesHitTriggers`、层碰撞矩阵（若改过） |
| 相机 | `mainCamera.clearFlags / backgroundColor / nearClipPlane / farClipPlane / fieldOfView` |
| 输入/UI | 自建的 `EventSystem`、摇杆、`Screen.orientation`、`Screen.sleepTimeout` |
| 时间 | `Time.timeScale`（慢动作特写会改，必须还原） |
| 其它 | `Cursor`（编辑器）、`AudioListener` 数量、粒子与贴图缓存（可留缓存，但要设上限） |

### 10.4 内容数据化

建筑预设、街具清单、道具、成就、模式参数、巨人性格参数**全部走 ScriptableObject 配置资产**，不要散落代码。
加内容 = 加一份资产，不是改逻辑。内容做**分级开关**（低/中/高），低端机自动降级（与 §11 配合）。

---

## 11. 性能预算（Android arm64，骁龙 8 级，目标 60 FPS）

| 项目 | 预算 | 观察方式 |
|---|---|---|
| 目标帧率 | 60 FPS（低电量档允许 30） | `DebugHud` 显示 FPS、ms |
| **SetPass calls** | ≤ 150（城市场景，含巨人） | `Game 视图 ▸ Stats` |
| **Batches** | ≤ 300 | 同上 |
| 三角面 | 城市 ≤ 40 万（巨人 PMX 另计） | 同上 |
| 实时光源 | **1 个投影平行光** + 自发光/贴花替代其余 | — |
| 阴影 | 2048² 以内，`shadowDistance` 收紧到近景 | — |
| 贴图 | 单张 ≤ 512，运行时生成贴图总量 ≤ 12 张 | — |
| 粒子 | 同屏 ≤ 200 | — |
| **GC 分配** | 稳态每帧 **0 B**（对象池、缓存、禁字符串拼接） | Profiler `GC Alloc` |

### 11.1 超预算时的取舍顺序（先保近处细节与巨人阴影）

砍远景楼群数量 → 砍街道家具种类 → 降阴影分辨率/收紧 shadowDistance → 降粒子 → 降巨人材质特性（lilToon → Unlit 回退）→ **最后才降建筑细节**。

### 11.2 移动端工程设置（构建前逐条核对）

- `Scripting Backend = IL2CPP`；`Target Architectures` **只勾 ARM64**（UMT 只有 `arm64-v8a` 的 `.so`）。
- `Scripting Backend` 相关：`Managed Stripping Level = Low/Medium`（太高会剥掉 UMT/Newtonsoft 的反射路径）。
- **`link.xml` 保底**（若开了剥离）：

```xml
<linker>
  <assembly fullname="UMT" preserve="all"/>
  <assembly fullname="Newtonsoft.Json" preserve="all"/>
</linker>
```

- 图形 API：GLES3（Vulkan 可选）；`Multithreaded Rendering` 开；`Graphics Jobs` 视兼容测试。
- 贴图压缩 ASTC；`Optimize Mesh Data` 开；`Static Batching`（城市静态件）与 `Dynamic Batching` 视情况。
- `Screen.sleepTimeout = SleepTimeout.NeverSleep`（玩法中）。
- 首次进入的加载不要卡：分帧构建城市（协程 + 每帧预算）。

---

## 12. 分阶段实施 + 验收（每阶段真机可跑、可截图判对错）

> 沿用 Phase1/Phase2 的交付纪律：**禁止声称"已测试/已验证"**；只做静态自查；如实标注"待真机验证"。

### 阶段 0：地基（不写玩法）
- 锁横屏 + 安全区 + HUD 骨架（大按钮）。
- `WorldScaler` + `GameConfig` + 出生点校验 + 调试读数（FPS/坐标/速度/相机模式/`isGrounded`）。
- **验收**：进游戏能看到地面；能稳定站在安全点；HUD 按钮够大能点中；连续 20 次重开不卡几何体。

### 阶段 1：相机 + 移动（"能玩"的门槛）
- `CameraRig` 三档 + 切换；相机碰撞与地面钳制；near/far 按尺度；设置面板生效并持久化。
- 小人移动 + 摇杆 + 加速插值 + 翻滚（含无敌帧）。
- **验收**：能自由跑动、视角不穿地不穿楼、灵敏度滑杆实时生效；`minMoveDistance=0` 后"移动不了"消失。

### 阶段 2：城市（"精致"）
- `CityKit` 全量：建筑生成器 → 路面/人行道（带 Collider）→ 街具 → 绿化 → 天空/雾/时间档 → 光照与阴影。
- **验收**：§4.9 的 **9 条**全部打勾，且 SetPass calls 在预算内。

### 阶段 3：巨人 + 玩法闭环（P0 内容）
- 巨人加载 + 状态机 + 步态（VMD 优先 / IK 目标兜底）+ 踩踏判定 + 演出 + 免伤 + 结算 + 重开。
- 无敌开关；**生存模式 + 自由沙盒模式**双模式。
- 巨人阴影投在街上（观感验收）；巨人与小人 1:20 比例肉眼可判。
- **验收**：能玩满一局并结算；无敌下反复被踩不判负；退出后回到模型查看器**零残留**（对照 §10.3 清单逐项测）。

### 阶段 4：内容加料（P1，逐项独立提交，每项可单独演示）
- 巨人性格 + 动作库；藏身与搜索（潜行）；道具 5 种。
- 公园/工地/水岸分区 + 地标；夜晚时间档；音效与震动；局内播报；成就；**巨人模式（操控巨人踩 NPC）**。
- **验收**：每加一项都能单独跑通并截图；连续叠加不崩、帧率不出预算。

### 阶段 5：打磨与加成（P2）
- 挑战关卡、拍照/摆姿联动（见 `Unity_摆姿势模块_方案_v0.1.md`）、天气、慢动作特写、收集解锁、水洼/涂鸦细节。
- 性能压测与降级开关；**连续进出游戏模式 10 次不崩不残留**（内存不持续增长）。
- **验收**：真机 60 FPS 稳定；内容分级（低/中/高）有效。

---

## 13. 内容分层（P0/P1/P2，**本次重点，别一次全铺开**）

> 判断标准：一个没看过说明的人上手，能自己发现"原来还能这样玩"。

### 13.1 游戏模式（主菜单可切换）

| 模式 | 玩法 | 优先级 |
|---|---|---|
| ① 生存逃脱（默认） | 城里躲巨人追踩，存活计时、擦身奖励、连击、无敌开关 | **P0** |
| ② 自由沙盒（无死亡） | 不判负、巨人踩不死你（=无敌强制开）。用来逛城市、看巨人、试操控，**天然的新手引导** | **P0** |
| ③ 巨人模式（反转） | **玩家操控巨人**走街，踩满街 NPC 小人（踩成贴地纸片 + 计数/连击）；踩车会压扁、踩断路灯 | **P1** |
| ④ 挑战关卡 | 短关卡：30 s 从 A 到 B；躲过 5 次踩踏；被发现前潜入地铁口 | **P1** |
| ⑤ 拍照 / 摆姿 | 冻结 AI，自由机位 + 给巨人摆姿势，与城市同框出片（打通摆姿模块） | **P2** |

> ①② 必做：② 是唯一能让用户"看清楚这游戏到底有什么"的模式。
> "什么都看不见、玩不了"的教训说明：**先让人能看见、能走动，才谈得上好玩。**

### 13.2 城市内容扩展（从"一条街"到"一座城"）

**分区**（P0 做 2 个，P1 做齐）：商业街（高层+玻璃幕墙+霓虹招牌+底层商铺）/ 住宅区（低矮公寓+**阳台/晾衣杆/空调外机/防盗窗**，这三样是"生活感"关键）/ 公园（草地+树+长椅+喷泉+儿童设施）/ 工地（塔吊+围挡+钢筋堆+脚手架，**可攀爬点**）/ 水岸（河+桥+护栏+泊船，做一个立刻拔高层次）。

**地标**（P1，1~2 个）：电视塔 / 钟楼 / 大型商场 / 体育馆。作用 = 远景锚点 + 方向感 + 出片。

**街道家具**（P0 ≥ 8 种）：路灯、红绿灯、斑马线、车道线、护栏、公交站牌、**报刊亭**、**电话亭**、**自动售货机**、长椅、垃圾桶、消防栓、井盖、交通锥、路障、广告灯箱、霓虹招牌、遮阳棚、无障碍坡道。

**车辆**（P1）：路边停车（可踩扁成"纸箱"、可踢飞）+ 少量行驶车辆。
> **车被踩扁的瞬间是本玩法最好的"尺度感"演示**，比任何 UI 数字都直观。

**动态元素**（P1/P2）：NPC 行人（见巨人尖叫逃散）、飞鸟群、气球、飘旗横幅、随风滚的纸片落叶。
> 静态城市是"模型"，有动态才叫"活的城"。

**细节层**（P2）：地面水洼、污渍、裂缝、墙面涂鸦、褪色海报。
**垂直内容**（P1）：地下通道/地铁口/**可达楼顶** —— 有垂直空间，"躲"才有策略。

### 13.3 玩家能力与道具

- **基础（P0）**：走 / 跑 / **闪避翻滚**（短冲刺 + 短暂无敌帧，躲踩的核心手感）。
- **进阶（P1）**：跳、蹲下（缩小被踩判定）、被震到踉跄（巨人靠近的被动演出）。
- **道具（P1，城市散落并定时刷新）**：能量饮料（加速）/ 护盾（挡一次）/ 烟雾弹（巨人短暂失去目标）/ **钩爪**（勾楼顶快速上高处）/ 诱饵（引开注意力）。
- **藏身机制（P1，潜行核心）**：垃圾桶 / 车底 / 地下通道 / 楼内 → 巨人**失去目标进入"搜索"**（张望、低头找、踩开垃圾桶）。
  > 这条把玩法从"体力活"变成"斗智"，强烈建议做；配套巨人要有视线锥 + 听觉，潜行才有意义。

### 13.4 巨人行为扩展

- **性格系统（P1）**：不同 PMX 配不同人格，直接影响参数与动作选择 —— 暴躁（追得凶、踩得频繁）/ 好奇（蹲下盯着看、伸手指戳）/ 玩闹（踢来踢去、当玩具）/ 温柔（动作慢、多是恐吓）。
  > 用户会换不同 PMX（昔涟/流萤…），性格系统正好让"换模型"产生真实的玩法差异，而不是同一个游戏换张皮。
- **动作库（P1）**：踩 / 碾（脚掌前后滚）/ 踢 / 抓（捏起来看）/ 蹲下伸手指戳 / 吹气。
- **场景互动（P1/P2）**：踩扁车、踩断路灯、踩塌楼顶、踩水洼溅水、踩消防栓喷水柱、惊起鸟群。**互动量 = "巨人感"的厚度。**
- **搜索状态（P1）**：失去目标后不立刻重置，而是"原地张望 → 走向最后目击点 → 翻查藏身处"。

### 13.5 反馈、成长与收集

- **计分（P0）**：存活时间 + **擦身而过奖励**（脚掌距你 < 体感 1 m 时给分并弹提示）+ 连击；结算页显示历史最佳（`PlayerPrefs`）。
- **局内播报（P1）**："擦身而过！""巨人暴怒了！""完美闪避 ×3"。成本极低、爽感极高，性价比第一。
- **成就（P1）**：首次贴脸躲过 / 无伤活满 60 s / 靠藏身处脱险 3 次 / 看着巨人踩扁 5 辆车…
- **收集（P2）**：城市内隐藏 N 个彩蛋/明信片，集齐解锁新巨人外观或城市皮肤。
- **慢动作特写（P2，强烈推荐）**：被踩中/擦身瞬间切运镜特写（从街角仰拍压下来的脚），`Time.timeScale = 0.3`。
  > 做这游戏的原始动机就是"看巨人踩"，**特写镜头才是核心爽点**，别只做成躲闪小游戏。
  > 注意：`Time.timeScale` 影响 `MMDTransformManager`（它用 `Time.deltaTime`），慢动作下物理与 IK 会同步变慢（这正是想要的），但**退出时必须还原 timeScale**。

### 13.6 氛围与视听

- **时间档（P0 基础 → P1 做全）**：清晨 / 正午 / 黄昏 / **夜晚（霓虹 + 窗户点亮）**。夜景性价比极高。
- **天气（P2）**：晴 / 雨（地面湿反光 + 雨丝）/ 雾。
- **音效（P1）**：巨人脚步（**方位感 + 由远及近的层次**，压迫感的一半）/ 落地重低音 / 地鸣 / 玻璃碎裂 / 警报 / NPC 尖叫 / 危险时心跳与 BGM 动态切换。
- **震动（P1）**：踩下 / 巨人靠近 / 命中。

### 13.7 摆姿模块 ↔ 游戏 的联动（P2，很划得来）

摆姿模式摆好的姿势**直接投放进城市当巨人的一个动作**；拍照模式与城市同框截图。
保存的"姿势卡"让巨人随机取用 = **无限动作库**（省掉做 VMD 的成本）。详见 `Unity_摆姿势模块_方案_v0.1.md`。

### 13.8 内容优先级总表（照这个顺序做）

| 级别 | 内容 |
|---|---|
| **P0 必须** | 生存模式、自由沙盒、街区+街具+车辆、NPC 行人、闪避翻滚、无敌开关、结算与最佳成绩 |
| **P1 应该** | 巨人性格 + 动作库、藏身与搜索（潜行）、道具 5 种、公园/工地/水岸、地标、夜晚档、音效与震动、局内播报、成就、巨人模式踩 NPC |
| **P2 加分** | 挑战关卡、拍照/摆姿模式、天气、慢动作特写、收集解锁、水洼涂鸦细节 |

> **不要一口气全铺开。** 先把 P0 做扎实并真机验证，再按 P1 逐项加，每加一项都能单独截图演示。
> 内容量不是难点，**能稳定叠加而不崩，才是这个项目真正的难点。**

---

## 14. 风险与注意事项（Unity 专属）

1. **别缩放巨人 PMX**（§3.1，Bullet 弹簧按米制标定，缩放 → 衣飞）。
2. **相机 near clip 是第一杀手**：尺度变了它必须跟着变，否则"黑屏看不见"。
3. **`CharacterController.minMoveDistance` 必须置 0**，否则"移动不了/原地抖"。
4. **输入 API 与 `Active Input Handling` 必须一致**，否则输入直接抛异常。
5. **一切依赖骨骼最终位置的东西都要排在 `[DefaultExecutionOrder(10000)]` 之后**。
6. **Bullet 地面是无限平面，不是 Collider**：别指望给楼加 Collider 能挡住裙摆。
7. **Android 只勾 ARM64**：UMT 原生库只有 `arm64-v8a`；IL2CPP 剥离要配 `link.xml`。
8. **退出游戏必须还原全局状态**（§10.3），否则第二次进入越来越卡/画面变暗/帧率被锁。
9. **`Application.targetFrameRate` 被 UMT 设成 60**，退出要交还。
10. **程序化贴图必须缓存复用**，别每个物体生成一张（爆内存 + 卡顿）。
11. **阴影别贪**：一个投影平行光足够，路灯用自发光；多个实时光直接掉一半帧。
12. **运行时生成的所有 Mesh/Texture/ScriptableObject 都要 `Destroy`**，否则反复进出泄漏。
13. **`Time.timeScale` 慢动作后必须还原**（还影响 UMT 物理与 IK 时序）。
14. **中文文件名/路径**（中国用户环境很常见）要在导入管线里统一处理。
15. **Spawn 点必须校验**，否则"开局卡在楼里"会被当成"游戏做坏了"。
16. **场景别手写 .unity YAML**：优先用 `GameModeSceneBuilder` 代码搭场景（Phase1 的纪律），避免坏 GUID 引用。
17. **`Exit()` 里别忘了销毁巨人**：`MMDTransformManager.DisposeRuntimeData()` / `MMDPhysicsManager.DisposePhysics()` 有对应清理入口，漏了会留下原生内存。
18. **不要用 `GameObject.CreatePrimitive` 拼城市**（默认 Collider + 默认材质的味道一眼假）。

---

## 15. 交付要求（给实现者）

1. 按 §12 分阶段提交，**每阶段可独立运行、可截图验证**。
2. `GameConfig` 集中所有可调数值，并在文档里列一张参数表（名/默认值/范围/作用）。
3. HUD 内保留**调试开关**（FPS / SetPass / 坐标 / 状态机状态 / 相机模式 / `isGrounded`）。
4. 提交说明写清：本阶段解决了 §1 表里哪几条、**如何验证**（具体到点哪个按钮、看哪个数字）。
5. **完成标准** = §1 的 10 条全部有明确结论 + §12 各阶段验收通过 + §13.8 的 **P0 内容全做齐** + 真机 60 FPS + 退出零残留。
6. 内容按 §13.8 优先级推进，**不要并行铺开**；每项 P1/P2 都要能单独跑通并截图演示。
7. 如实报告：哪些只能在真机上验证（模型/贴图/物理自然度/帧率/SAF 导入/振动），别编数值糊弄。

---

## 附 A：Unity 侧坑速查表（现象 → 根因 → 定位 → 修法）

| 现象 | 最可能根因 | 怎么定位 | 修法 |
|---|---|---|---|
| 进游戏一片黑/只见天空 | 相机 `nearClipPlane` 远大于小世界尺度 | 选中 Main Camera 看 near 值；把 near 调到 0.001 试 | §3.3 按 `worldScale` 缩放 near/far |
| 世界"被拉近/穿模" | 相机在几何体内 + 无碰撞 | Scene 视图看相机位置 | 出生点校验 + 相机 SphereCast |
| 按摇杆没反应 | ① `Active Input Handling` 与 API 不匹配 ② 被全屏 uGUI 吃掉事件 | Console 有没有 `InvalidOperationException`；关掉疑似 Image 的 `raycastTarget` 试 | §6.1 五步 |
| 原地踏步/抖动/走不动 | `CharacterController.minMoveDistance` 默认 1 mm ≥ 每帧位移 | 打日志看 `controller.velocity`/位移 | `minMoveDistance = 0`；`skinWidth` 缩小 |
| 小人像在月球上 | `Physics.gravity` 没随世界缩放 | 打印落体高度曲线 | `Physics.gravity *= worldScale` |
| 卡在墙里/穿墙 | `skinWidth` 太大（默认 0.08 ≈ 小人身高） | 看 CC 参数 | 半径 5~10% |
| 裙摆乱飞 | 缩放了巨人 root | 看 giant root 的 `localScale` | 保持 1，改缩世界 |
| 裙摆穿楼/穿人行道 | UMT 地面是无限平面，不是 Collider | 读 `BuildGround` 逻辑 | §4.3 的三条缓解法 |
| 相机跟随抖动/贴花滞后 | 时序排在 `MMDTransformManager`(10000) 之前 | 看脚本执行顺序 | 设 10001 或下一帧读 |
| 踩踏判定慢半拍 | 同上 | 同上 | 同上 |
| 退出后画面变暗/变卡 | 全局状态没还原 | 对照 §10.3 清单 | 逐项还原 |
| 第二次进入内存持续涨 | Mesh/Texture/原生物理没释放 | Profiler 内存曲线 | §14.12 / §14.17 |
| 阴影糊成一片 | `shadowDistance` 按米制默认值 | QualitySettings / Light | §3.3 |
| 模型是纯色/不受光 | 没装 lilToon → 回退 `Unlit/Texture` | 看材质 shader 名 | 装 lilToon（Built-in RP 也支持） |
| 安卓上物理不工作 | 构建架构不是 ARM64 / 剥离把 UMT 剥了 | Logcat 看缺库或反射异常 | 只勾 ARM64 + `link.xml` |
| 装不了手机里的模型 | SAF 单文件拿不到同目录贴图 | 看导入日志 | 目录树授权 或 走 zip |

## 附 B：与 Babylon 版 v2.0 的口径映射（防止拿旧文档照抄）

| 主题 | Babylon 版（v2.0，作废口径） | Unity 版（本文，正确口径） |
|---|---|---|
| 模型加载 | `mmdRuntime.createMmdModel(mesh)` | `PMXImporter.BuildUnityObjects(model, options)` |
| 动作播放 | AnimationGroup / VMD loader | `VMDReader` + `VMDAnimationClipConverter.Convert/ConvertAsync` → `Animator` |
| 骨骼求解 | babylon-mmd runtime | `MMDTransformManager`（`[DefaultExecutionOrder(10000)]`，`LateUpdate`） |
| 物理 | babylon-mmd + Ammo/Havok | `MMDPhysicsManager` + Bullet 原生插件（Win x64 / Android arm64 / Web） |
| 单位 | 自定 metre | `k_MMDUnitToUnityUnit = 0.08` → PMX ≈ 1.6 m |
| 程序化贴图 | `DynamicTexture` | 运行时 `new Texture2D()` + 静态缓存 |
| 实例化 | thin instances / `mergeMeshes` | GPU Instancing + `CombineMeshes` + `IndexFormat.UInt32` |
| 每帧循环 | `onBeforeRenderObservable` / `runRenderLoop` | `Update` / `LateUpdate` + `[DefaultExecutionOrder]` |
| 触控 | DOM/CSS `env(safe-area-inset-*)` | uGUI `CanvasScaler` + `Screen.safeArea` |
| 震动 | `navigator.vibrate` / Android 桥 | `Handheld.Vibrate()` / `AndroidJavaObject` |
| 文件导入 | 本地代理 URL + SAF | `ACTION_OPEN_DOCUMENT_TREE` 或 zip 解压到 `persistentDataPath` |
| 模块边界 | `src/game/` + esbuild | `Assets/Scripts/Game/` + asmdef（可选） |

## 附 C：`GameConfig` 参数表（起点，全部可在真机调）

| 字段 | 默认 | 范围 | 作用 |
|---|---|---|---|
| `tinyRealHeight` | 1.70 | 1.4~2.0 | 小人"现实"身高，一切推导基准 |
| `tinyUnityHeight` | 0.085 | 0.05~0.20 | 小人在 Unity 里的身高（决定世界比例） |
| `giantNativeHeight` | 1.6 | 实测 | 巨人 PMX 在 Unity 里的身高（归一化后回填） |
| `walkBodyPerSec` | 0.82 | 0.5~1.2 | 走速（身高/秒）→ 体感速度 |
| `runMultiplier` | 1.8 | 1.4~2.5 | 跑/走比 |
| `dodgeImpulse` | 体感 4 m/s | 2~8 | 翻滚冲刺速度 |
| `dodgeInvuln` | 0.4 s | 0.2~0.8 | 翻滚无敌帧 |
| `playerRadiusFactor` | 0.25 | 0.2~0.35 | 胶囊半径 / 身高 |
| `stepOffsetFactor` | 0.3 | 0.15~0.4 | 台阶高 / 身高 |
| `gravityFactor` | 1.0 | 0.5~2.0 | 重力 = 9.81 × worldScale × 该值 |
| `camFovFps/Tps` | 70 / 60 | 60~100 | 视野 |
| `camTpsDistBody` | 体感 2.5 m | 1~5 | TPS 距离（× 身高） |
| `sensH/sensV` | 1.0 / 1.0 | 0.2~5 | 灵敏度（独立） |
| `giantWalkSpeedBody` | 体感 12 m/s | 6~25 | 巨人移动速度（与步态 cadence 同步） |
| `giantStompDamageWindow` | 1.8 s | 1.0~3.0 | 踩中后免伤时长 |
| `invincible` | true（沙盒） | — | 无敌开关 |
| `cityBlocks` | 3×3 | 1×1~6×6 | 街区规模（性能档位） |
| `contentTier` | Mid | Low/Mid/High | 内容分级（性能降级） |

---

*文档版本 v2.1（Unity 口径），与 `TASK_Phase2.md` 配套使用；实现开始时先读 `TASK_给实现AI.md` 的交付纪律。*

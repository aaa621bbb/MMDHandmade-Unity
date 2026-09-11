# 巨人踩城 · 制作文档 v1.0（Unity 版）

> **目标仓库**：`aaa621bbb/MMDHandmade-Unity`
> **技术栈**：Unity 6.6 + UMT 0.5.1（`Packages/com.candidumgames.unitymmdtools`）+ **Built-in RP**，交付 **Android arm64**
> 配套 `Unity_01_需求文档_v1.0.md`（**需求以那份为准，本文只讲怎么做**）。
> 本文只定义技术口径、坑位、验收方式与交付纪律，**不写具体代码**。
>
> **本文所有技术结论都来自 UMT 0.5.1 源码与 Unity 行为约定；没有任何一条声称"已运行验证"。**

---

## 0. 三条工程铁律

1. **永远不要缩放巨人 PMX。** 她的 `transform.localScale` 永远是 1。
   **理由（源码级）**：`k_MMDUnitToUnityUnit = 0.08`，把 MMD 单位换算成 Unity 米；`MMDPhysicsManager` 创建 Bullet 上下文时把该值写进 `NativeConfig`，并在注释里写明弹簧阻尼做了 `scale` / `scale²` 补偿，
   *"so the motor-based 6DOF springs reproduce MMD's unit-scale reference at Unity's meter scale"*。
   一缩放，惯量成倍变化，**弹簧脱离标定区间 → 裙子头发乱飞**。缩放也不会传播进 Bullet 上下文。
   → 正确做法：**缩世界**（§1）。
2. **禁止魔法数。** 眼高、速度、碰撞半径、相机距离、阴影距离、粒子尺寸、音效距离 —— 全部由 `worldScale` 与"玩家身高"推导。
3. **进入 / 退出玩法必须零残留。** 退出后回到原 MMD 播放界面要**跟没进过一样**。必须还原的 Unity 全局状态见 §10。**连续进出 10 次不崩、不涨内存。**

---

## 1. 尺度系统（**先定这个，否则其余全是错的**）

### 1.1 唯一可行路线：缩世界

| 层级 | 说明 |
|---|---|
| **巨人** | 加载进来的 PMX，**原生尺度**。Unity 里她的实际身高 ≈ **1.6 m**（UMT 换算后），`localScale` 永远 1 |
| **玩家小人** | 现实身高 1.7 m，在这个世界里只到她的脚踝以下 |
| **世界根 `WorldRoot`** | 城市 + 小人 + 所有道具放在它下面，**统一缩放 `worldScale`** |
| **比例** | 她的"体感身高" = `1.6 / worldScale` ≈ **30 m**（`worldScale ≈ 0.05`） |

```csharp
// GameConfig
public const float GiantNativeHeight = 1.6f;   // UMT 换算后 PMX 的实际身高(米)；用真实模型实测后写死
public float tinyRealHeight  = 1.70f;          // 小人"现实中"的身高(米) —— 一切推导的基准
public float tinyUnityHeight = 0.085f;         // 小人最终在 Unity 世界里的身高(米)
public float worldScale => tinyUnityHeight / tinyRealHeight;   // ≈ 0.05
```

### 1.2 缩放后**必须跟着改**的 Unity 全局量（漏一个就出怪现象）

> 这一节是 Unity 与网页版最大的差别：Unity 有一堆"默认按米制调好的全局参数"。
> 在 `WorldScaler.Apply()` 里集中设置，退出时按 §10 还回去。

| 类别 | 参数 | 默认（米制） | 缩放后 | 不做的后果 |
|---|---|---|---|---|
| **相机** | `Camera.nearClipPlane` | **0.3** | `0.003~0.01`（实测） | **整个世界被近裁面裁掉 = "什么都看不见"** |
| | `Camera.farClipPlane` | 1000 | 30~60 | 远景被裁 / 深度精度浪费 |
| **光照** | `QualitySettings.shadowDistance` | 40~150（随质量档） | 收紧到能盖住近景 | 阴影精度全浪费，她的大阴影糊成一团 |
| | `Light.shadowBias` / `shadowNormalBias` | 0.05 / 1.0 | 缩到 1/10~1/50 | shadow acne 或 peter-panning |
| **物理** | `Physics.gravity` | (0,−9.81,0) | `× worldScale`（推荐） | 小人像在月球上飘，跳跃与身高不成比例 |
| | `Physics.defaultContactOffset` | 0.01 | `× worldScale`（≈5e-4） | 小物体抖动 / 互相穿透 |
| | `Physics.sleepThreshold` | 0.005 | `× worldScale` | 小物体"粘住"或永不休眠 |
| | `Time.fixedDeltaTime` | 0.02 | **保持不动** | 改步长会连带 UMT 的物理时序 |
| **玩家** | `CharacterController.skinWidth` | **0.08** | **半径的 5~10%**（≈0.002） | 默认 0.08 ≈ 整个小人身高 → 卡住 / 穿墙 |
| | `CharacterController.minMoveDistance` | **0.001** | **必须 = 0** | 每帧位移 < 1 mm 时控制器丢弃移动 = **"移动不了"** |
| | `CharacterController.stepOffset` | 0.3 | ≈0.3 × 小人身高 | 穿过路缘石或卡住 |
| | `CharacterController.slopeLimit` | 45° | 45~55 | 走不上人行道斜坡 |
| **粒子/音效** | 粒子初始尺寸/速度 | 米制 | `× worldScale` | 扬尘像云一样大 |
| | `AudioSource.minDistance/maxDistance` | 1 / 500 | `× worldScale` | 她的脚步没有"由远及近"的层次 |
| **UI** | Canvas 参考分辨率 | 任意 | **固定 1920×1080**（与尺度无关，独立体系） | 按钮在不同机器上大小不一 |

**浮动精度**：`worldScale=0.05` 时坐标绝对值最大约 40 m，`float` 完全够用，**不需要 double**。
但**必须**：城市生成后把 `WorldRoot` 放在原点附近，出生点选在城市中心区。

### 1.3 出生点校验（强制，这是"一进游戏就卡在楼里"的根治手段）

在候选点做三件事：① 向下 `Physics.Raycast` 确认脚下有地面；② `Physics.OverlapCapsule`（半径 = `身高*0.25`，高 = `身高`）确认为空；③ 头顶净空 ≥ 2×身高。
任一不满足就换点，最多 20 次，最后兜底到"城市中央广场预设安全点"。

### 1.4 验收

1. 第一帧能看到地面与街道（不是黑屏、不是纯色）。
2. 小人头顶大概到她脚踝（截图能量出比例）。
3. 连续重开 20 次，无一次卡在几何体内。

---

## 2. 相机系统

### 2.1 三档机位（一个 `CameraRig` 统一管理）

| 模式 | 说明 |
|---|---|
| **FPS 第一人称** | 眼高 = `tinyUnityHeight * 0.92`；抬头能看到她全身（**这玩法最爽的视角**） |
| **TPS 第三人称** | 弹簧臂跟在后上方，距离可调；能看到自己的小人 |
| **观察 / 自由** | 环绕轨道，用来看她全身；也用于拍照 / 摆姿 |

- 一个**大按钮**循环切换；进入玩法默认 **FPS**。
- **不要**建三台相机来回启用（会带来三份状态：AudioListener、深度、后处理）。

### 2.2 相机碰撞（**直接解决"视角穿模"**）

- TPS：从角色头部向目标机位做 `Physics.SphereCast`，命中就拉到命中点之前（留余量）。
  **LayerMask 必须排除玩家自身层**，否则会被自己顶住。
- FPS：`nearClipPlane` 按 §1.2 缩放；**玩家自身网格不参与渲染**（`ShadowCastingMode.ShadowsOnly`，保留影子但不露内壁）。
- 任何情况下相机**不得低于地面**：每帧一次向下射线，把相机钳到地面之上。

### 2.3 时序（**Unity 专属坑，必看**）

`MMDTransformManager` 是 `[DefaultExecutionOrder(10000)]`，在 `LateUpdate()` 里采样骨骼 → 解算约束/IK/物理 → **回写 Transform**。
→ **任何依赖骨骼最终位置的东西**（相机跟随她的视线、踩踏判定、贴花、爬上她的吸附点）**都必须排在它之后**：
- 自写脚本设 `[DefaultExecutionOrder(10001)]` 并在 `LateUpdate` 里读；或
- 在下一帧 `Update` 里读（1 帧延迟，肉眼不可见）。

**写反了的典型症状**：相机跟随抖动、脚下贴花滞后一帧、踩踏判定永远慢半拍。

### 2.4 设置面板（"灵敏度要可调"）

全部**大滑杆 + 实时生效 + `PlayerPrefs` 持久化**：
水平灵敏度 / **垂直灵敏度（独立）** / 反转 Y 轴 / 视角阻尼 / FOV / TPS 距离 / 肩部偏移 / 摇杆灵敏度。
> 灵敏度要与 FOV 联动换算，否则改 FOV 手感会变。

---

## 3. 玩家控制器与输入（"移动不了"的根治）

### 3.1 排查五步（按顺序，别跳）

1. **先放调试读数**（速度 / 坐标 / `isGrounded` / 相机模式 / 输入原始值）—— 眼见为实。
2. **`Player Settings ▸ Other Settings ▸ Active Input Handling` 与代码 API 必须一致**：
   - `Input Manager (Old)` 或 `Both` → 可用 `Input.GetAxis("Horizontal"/"Vertical"/"Mouse X"/"Mouse Y")`；
   - `Input System Package (New)` → 老 API 会抛 `InvalidOperationException`，**症状就是"按了没反应"**。
   - **统一用一套，不要混用**，并在 README 写明选了哪个。
3. **`CharacterController.minMoveDistance = 0`**（默认 0.001 m 在缩小世界里等于"禁止移动"）；`skinWidth` 按 §1.2 改小。
4. **输入区是否被 uGUI 吃掉**：摇杆/按钮之外的全屏元素 `Image.raycastTarget` 必须关掉；视角拖动区用透明层实现，**与摇杆矩形不重叠**。
5. **速度是否按尺度推导**：禁止直接写 `1.4f`（在 Unity 单位里那是体感的 20 倍速，小人会像子弹）。

### 3.2 控制器选型与参数

**推荐 `CharacterController`**（站立/斜坡/台阶稳定，不用每帧管刚体）。

参数（全部进 `GameConfig`）：`radius = h*0.25`、`height = h`、`skinWidth = radius*0.08`、`minMoveDistance = 0`、`stepOffset = h*0.3`、`slopeLimit = 50`。

重力自己算（`verticalVelocity += gravity * dt`，`isGrounded` 时清零），与 `Physics.gravity` 保持一致即可。
移动 `controller.Move(...)` 每帧一次，放 `Update`。

### 3.3 输入区划分（横屏）

```
┌──────────────────────────────────────────────────────────┐
│ [退出] [重开]      ⏱ 存活 42s        [无敌] [设置] [视角] │ 顶栏 ≥ 56dp
│                                                          │
│                        （游戏画面）                        │
│  ╭──────╮                                                │
│  │ 摇杆 │                                    ┌─────────┐ │
│  ╰──────╯                                    │ 动作按钮 │ │
└──────────────────────────────────────────────────────────┘
   ↑ 左下 Φ140~160dp        ↑ 右下：翻滚等动作
   视角拖动区 = 其余区域（透明层，raycastTarget 开，但不与摇杆矩形重叠）
```

- 摇杆用 uGUI 拖拽实现，**死区 0.15**，输出归一化向量给控制器。
- `EventSystem` 场景里只能有**一个**（重复会导致输入诡异）。

### 3.4 移动手感

- 加减速插值（不要瞬间启停）。
- 行走 / 奔跑两档。
- **闪避翻滚**（短冲刺 + 0.3~0.5 s 无敌帧）—— 躲踩的核心手感，**P0**。
- TPS 下朝向平滑转向；FPS 下自身 `ShadowsOnly`。

---

## 4. 城市生成（城市是玩具，不是布景）

> 硬要求：**不引入任何外部模型/贴图文件**（包体可控、无版权风险）。
> 全部用**程序化生成 Mesh + 运行时生成的 `Texture2D`**，生成一次、缓存复用。
> **精致 ≠ 面数多**，而是层次、重复中的变化、材质、光影。

### 4.1 生成方式选型（Unity 侧的正确做法）

- **一栋楼 = 一个合并好的 Mesh**（基座/标准层/退台/女儿墙/屋顶道具在构建时用 `CombineInstance` 合并），每种材质一个 `MeshRenderer`。一栋楼通常 2~5 个 draw call。
- **合并网格必须 `mesh.indexFormat = IndexFormat.UInt32`**（顶点数可能超 65535，否则截断花屏）。
- **重复街道家具**（路灯/交通锥/护栏段/树）：`Material.enableInstancing = true` + `Graphics.DrawMeshInstanced`；
  `shadowCastingMode` 要显式设（细杆类可关投影省性能）。
- **不要用 `GameObject.CreatePrimitive` 拼城市**（自带默认 Collider + 默认材质，是"纯色几何体"味道的来源）。
- **贴图生成**：`new Texture2D(256, 256, TextureFormat.RGBA32, mipChain: true)`，`SetPixels32` + `Apply()`，`wrapMode = Repeat`。
  按 key 缓存复用；**运行时生成的贴图不进 ASTC，注意控制张数与分辨率（建议 ≤ 12 张 256²/512²）**。

### 4.2 建筑生成器（核心，不要"随机一个盒子"）

一栋楼 = **基座 + 标准层 + 顶部**，各自独立随机：

- **体量**：宽 8~20 m、进深 8~20 m、高 12~80 m；支持**退台**与**双体量拼合**（轮廓不是单一立方体）。
- **立面**：程序化窗格贴图，**随机点亮 15~30% 的窗**（夜景关键）；加窗台线、腰线、竖向分隔条。
- **底座（1~2 层）**：商铺/大堂 —— 玻璃门 + 暖色内透光 + 招牌色块 + 遮阳棚，与上部立面明确区分。
- **顶部**：女儿墙 + 随机屋顶道具（水箱、空调外机、通风管、天线、电梯机房）。
- **变化来源**：分区色板（商业区冷灰蓝 / 住宅区暖米 / 老城区黄褐）× 窗格密度 × 退台层数 × 屋顶道具组合。
- **布局**：主街两侧各 6~10 栋，后排 1~2 圈做层次；**沿街立面必须贴合街道边界**；栅格抢占式布局，保证没有楼压到路面。
- **远景层**：300~500 m 外一圈**低模剪影楼群**（无窗、单色、受雾影响）撑天际线。

### 4.3 路面 / 人行道

- **车道**：沥青 + 程序化贴图（车道虚线/实线、磨损、油渍、补丁），主干道 2~4 车道。
- **人行道**：**抬高 0.15 m**（路缘石），不同铺装格子贴图。这条"高差"是廉价但极有效的城市感来源。
- **路口**：十字/丁字 + 斑马线 + 停止线 + 转向箭头（贴花）。
- **附属**：井盖、雨水篦子、无障碍坡道。
- **Collider**：车道/人行道**必须有 Collider**，否则小人掉出世界。
- ⚠️ **UMT 物理的硬限制（务必知道）**：Bullet 的世界里地面是**一个无限平面**
  （`MMDBulletPhysics.BuildGround(groundNormal, groundConstant)` + `enableGroundCollision`），
  **不是任意 Collider**。给楼加 Collider **解决不了她裙摆的穿模** —— 那是两个不同的物理世界。
  → 正确做法：① 让她主要在**车道平面（y=0）**活动；② 人行道高差控制在 0.15 m 内并接受轻微穿插；
  ③ 真要更严，只能按区域切换物理地面，**先按 ①② 做，别一上来就动物理地面**。

### 4.4 街道设施（决定精致度的关键，别省）

沿街**按固定间距自动布置**，每类要有细节：
路灯（灯杆 + 弯臂 + 灯头；夜间**灯头发光材质 + 地面光斑贴花，不要真点光源**）／行道树（树干 + 2~3 层树冠，或十字交叉 alpha 叶片面片）／信号灯与路牌／长椅、垃圾桶、消防栓、公交站牌雨棚、花坛、护栏／老城区加电线杆 + 电线（下垂弧线，成本极低观感提升明显）／广告灯箱（自发光，夜景最出效果）。

### 4.5 绿化 / 天空 / 雾 / 时间档

- 绿化：行道树 + 街角灌木 + 草地花坛；可选极少量落叶粒子。
- 天空：渐变天空盒，按时间档切换（生成一次缓存）。
- **雾必须开**：`RenderSettings.fog = true; fogMode = FogMode.ExponentialSquared;` 密度让体感 300 m 外明显衰减。
- **时间档**：白天 / 黄昏 / **夜晚**（**建议默认黄昏或夜晚**）。切档要同时改：太阳角度与颜色、环境光（`RenderSettings.ambientMode = Trilight`）、雾色、窗格点亮比例、自发光强度。

### 4.6 光照与阴影（Built-in RP）

- **一个 `Directional Light` 做太阳，必须开阴影**（`shadows = ShadowType.Soft`，`shadowStrength` 0.7~0.9）。
  **她投在街道和楼上的巨大阴影是"巨人感"的核心来源之一，优先级最高**，比城市细节优先级更高。
- **UMT 材质的源码级事实**：`PMXMaterialBuilder.GetShader` 的顺序是
  **lilToon（`_lil/lilToonMulti`，描边用 `Hidden/lilToonMultiOutline`）→ URP Unlit → Built-in `Unlit/Texture`**。
  即：**工程没装 lilToon 时，PMX 会回退成完全不受光的 Unlit** —— 她自身不会有明暗变化（观感扁平），
  **但依然能投影**（只要 `renderer.shadowCastingMode = On`）。
  → 要真正好看，**装 lilToon**（免费、MIT、Built-in RP 可用），UMT 会自动优先使用它。**不要为了这个切 URP。**
- **夜景补光用自发光 + 贴花，不要加实时光源**（多个投影光源直接掉一半帧）。
- **接触阴影**：小人脚下加一个软圆贴花（带 alpha 的 Quad，稍高于地面），消除"悬浮感"。

### 4.7 材质与批处理

- 每个材质至少：基础色（贴图或分块色）+ 粗糙度 + 金属度。城市用 **Standard shader**；夜景自发光用 `_EmissionColor` + `EnableKeyword("_EMISSION")`。
- **材料种类控制在 8~12 种**（材质种类 = draw call 下限）。
- 程序化贴图**一次生成、全场景复用并缓存**，不要每个物体各画一张。

### 4.8 「精致」的客观验收线（逐条打勾）

1. 站在街上看四周：能看到**至少 6 栋造型各异的楼**（不是同一盒子复制）。
2. 楼有**可辨认的窗格**，且**部分窗户亮着**。
3. 人行道与车道**有高差**，路面有**车道线/斑马线**。
4. 街上有**路灯、树、至少两种街道家具**。
5. 有**雾**，远处有**天际线剪影**，天空不是纯色。
6. **她的影子投在街上**，小人脚下有接触阴影。
7. 夜晚档能一眼看出"这是晚上"（窗光 + 路灯发光 + 整体色调）。
8. 截图不看 HUD，别人能认出"这是一条城市街道"，而不是"一堆盒子"。
9. **`Stats` 面板**里 `SetPass calls` 与 `Batches` 落在 §9 预算内。

---

## 5. 巨人系统

### 5.1 加载链路（不要自己造）

```csharp
var options = new PMXImportOptions { parent = giantAnchor, createAvatar = true, applyRenames = true };
PMXImportResult r = PMXImporter.BuildUnityObjects(modelAsset, options);
// r.root / r.mmdTransformResult.transformManager / r.mmdTransformResult.physicsManager
```

- 摆放归一化：按包围盒把脚底对齐 y=0、朝向统一（面向 −Z 或 +Z 二者择一）、**缩放保持 1**、记录身高供 §1 使用。
- **绝不自己写 VMD 关键帧、绝不自己采样骨骼**（Phase1 纪律继续有效）。
- 动作播放走 `VMDReader` + `VMDAnimationClipConverter.Convert / ConvertAsync(UMTFrameBudget, ...)` → `Animator`。
  分帧转换（`ConvertAsync`）可避免加载时卡顿。

### 5.2 行为状态机（**时间驱动，不用帧数**）

`巡游 → 发现 → 追 → 逼近抬脚（蓄力）→ 踩下 → 收脚 → 巡游`，中间穿插 **搜索** 与 **日常**。
每个状态有 enter/exit + 最小持续时间 + 过渡时长。

- **追你时头要下压、视线跟着脚边的小人** —— 观感关键。
- **发现判定** = 视线锥（角度 + 距离）+ 听觉（声音半径）。
- **搜索状态**：失去目标后不立刻重置，而是「原地张望 → 走向最后目击点 → 翻查藏身处」。

### 5.3 步态（**最容易出戏的地方**）

- **优先级 1：用 VMD 循环 clip**（走路 VMD / 踩踏 VMD），做交叉淡入淡出。
- **优先级 2：没有合适 VMD 时，驱动 MMD 的 IK 目标 —— 不要自己写骨骼旋转曲线。**
  MMD 的足部由 IK 控制（`MMDBoneTransform.ik` / `MMDIKHandleData`）。
  在 **`Update`**（早于 `[DefaultExecutionOrder(10000)]` 的求解）里把 `足IK` / `つま先IK` 的目标 Transform 抬起 / 前移，
  `MMDTransformManager` 在 `LateUpdate` 求解时读到的就是新目标。左右脚交替 + 与平移速度同步。
- **铁律：步频必须匹配平移速度**，否则脚底打滑，一眼假：

```
speed = 单步跨距 × 每秒步数
```

改速度时**先改步态节奏，再算速度**，不要让两者各自独立。

### 5.4 踩踏判定（可靠、自然、不过度）

- **判定体挂在 `足首` 骨下**（作为骨骼子物体，随骨更新世界位置）。
- **读位置的时机必须在 `MMDTransformManager`(10000) 求解之后**（`[DefaultExecutionOrder(10001)]` 的 `LateUpdate`，或下一帧 `Update`）。
- **两个条件同时成立才算命中**：① 该脚处于 `Stomp` 且判定体世界 y **持续下降**；② 玩家中心投影落在判定体水平半径内。
- 命中给 **1.5~2 s 免伤**（避免一帧连续判定）。
- **必须有预备动作与延迟**（抬脚 → 停顿 → 踩下），让人"看得清、躲得开"，**禁止随机秒**。
- **无敌 ON**：仅演出，不死无惩罚。**OFF（沙盒默认）**：击退 + 重生到附近安全点（不清城市、不重载她）。**不做强制 Game Over。**

### 5.5 演出

- 踩下与收脚的演出：扬尘（粒子，**同屏硬上限 200**）、地面碎屑、屏幕震动。
- 被踩中时给明确反馈（击退 / 重生到安全点），**不做强制 Game Over**。
- 相机：她落地与逼近时给轻微机位震动（幅度 × `worldScale`）。
- 音效：`AudioSource` 3D 空间化，她的脚步**要有方位感与由远及近的层次**（`minDistance/maxDistance` 按尺度缩放）。

### 5.6 物理守则（用户明确要求"衣服不能乱飞"）

1. **不缩放她**（§0 铁律一）。
2. `tm.livePhysics = true`；切换时调 `tm.ResetPhysics()`。
3. **地面碰撞存在**（`enableGroundCollision`），否则裙摆会与地面穿插。
4. **`MMDTransformManager` 会在 `ShouldRunLivePhysics()` 时把 `Application.targetFrameRate` 设成 60** ——
   **退出时要交还帧率**（见 §10）。
5. `tm.doSDEFSkinning` 保持开启（裙摆/头发形状依赖 SDEF），除非设备确实跑不动（那时按 §9 取舍顺序处理）。
6. 若某个 PMX 的裙子刚体参数本身差（抖/飞），提供"布料物理强度 / 关闭"的调节与安全预设；
   **但默认必须先把"不缩放 + 地面碰撞 + livePhysics"做对**，不要用"关物理"掩盖问题。
7. **验收观感**：她走动 / 追你时，裙摆与头发自然跟随摆动 —— **不飘出体外、不撕裂、不原地乱颤**。

### 5.7 爬上她（技术要点）

她的**鞋带、鞋跟、裙摆、脚背**是可攀爬、可站立的位置（需求 §5.3.4）。
→ 这些位置全部由**骨骼世界坐标**驱动：站立、吸附、判定都要跟着骨骼走，**不要用静态包围盒**（她一动就错位）。
→ 从上面摔下来要算一次明确的伤害 / 反馈。

### 5.8 平台降级（`MMDPhysicsGuard`）

Bullet 原生插件**只有三份**：`Plugins/Windows/x64`、`Plugins/Android/arm64-v8a`、`Plugins/Web`。
- **Android 构建必须只勾 ARM64**（勾了 ARMv7 而只有 arm64 的 `.so`，运行时会缺库）。
- **macOS / Linux 编辑器没有物理** —— 这是预期，不是 bug。要按 `Application.platform` + 插件存在性判定并**优雅降级**（置灰开关 + 提示），**绝不崩**。

---

## 6. 模型导入（"导入不了 PMX"的根治）

### 6.1 编辑器内导入（开发期主力）

1. `.pmx` + 贴图放进 `Assets/MMDResources/Models/<模型名>/`，**贴图必须与 PMX 同目录**（子目录保持相对结构），中文文件名要确认编码正常。
2. UMT 的 ScriptedImporter 会把 `.pmx` 变成 `PMXModel` 资产；构建走 `PMXImporter.BuildUnityObjects()`（§5.1）。
3. `.vmd` 放 `Assets/MMDResources/Motions/`，用 Phase1 的扫描器/转换器产出 `AnimationClip`，进 `MMDAssetLibrary`。

### 6.2 Android 运行时导入（用户自己的模型）

- **SAF 的现实问题（必须解决）**：Android 单文件选择器（`ACTION_OPEN_DOCUMENT`）**只给文件、不给目录** → 拿不到同目录贴图。
  两条可行方案，**建议以第 2 条为默认引导**：
  1. `ACTION_OPEN_DOCUMENT_TREE` + 目录树遍历（`AndroidJavaObject` 调 `DocumentFile.listFiles()`，递归复制到 `Application.persistentDataPath/Models/<name>/`）；
  2. **让用户选一个 `.zip`**（PMX + 贴图打包），用 `System.IO.Compression.ZipArchive` 解到 `persistentDataPath`。**最省事、成功率最高。**
- **运行时构建**：`PMXReader` 读字节 → `PMXImporter.Import(byte[] pmxBytes, ...)`（贴图经 `PMXImportOptions.loadTextures` 回调提供）；
  `.vmd` 走 `VMDReader` + `VMDAnimationClipConverter.Convert/ConvertAsync`。
- 中文/空格路径、大小写、BOM 要在导入管线里统一处理。

### 6.3 失败必须报错（禁止静默）

把这几项显示在界面上（Phase1 的 `OnError` 事件是现成的）：
读取失败的**具体异常文本**、缺失贴图**数量与文件名**、骨骼数、包围盒尺寸与换算后身高。
故意放一个贴图缺失的模型，界面必须给出明确提示，而不是黑屏 / 灰模。

### 6.4 验收

至少用 **2 个不同 PMX** 分别在真机验证：能导入、有材质、站在地上不悬空不陷地、身高符合 1.6 m 量级、裙摆物理自然。
再验证一次**运行时导入**（zip 或目录），以及失败时的报错回显。

---

## 7. UI / HUD

### 7.1 尺寸换算（用 `CanvasScaler` 之后的正确姿势）

- 触摸目标最小 **48×48 dp**；主操作按钮 **64~80 dp**；间距 ≥ 12 dp。

```
CanvasScaler: Scale With Screen Size, Reference Resolution = 1920×1080, Match = 0.5
设备像素 → 参考分辨率像素：px_ref ≈ dp × (1080 / 设备可用高度dp)
典型 6.7" 1080p 手机：可用高度 ≈ 360 dp → 1 dp ≈ 3 px_ref
∴ 48dp ≈ 144px_ref ；64dp ≈ 192px_ref ；80dp ≈ 240px_ref
```

> 所以：**按钮 `RectTransform` 不要低于 150×150（参考像素）**。滑杆轨道高 ≥ 18，滑块 ≥ 72。

### 7.2 安全区（横屏挖孔/刘海）

```csharp
var sa = Screen.safeArea;                      // 屏幕像素空间
canvasRT.anchorMin = new Vector2(sa.xMin / Screen.width,  sa.yMin / Screen.height);
canvasRT.anchorMax = new Vector2(sa.xMax / Screen.width,  sa.yMax / Screen.height);
canvasRT.offsetMin = canvasRT.offsetMax = Vector2.zero;
```

并在 `Player Settings` 里**取消 `Render outside safe area`**。

### 7.3 布局 / 视觉

见 §3.3 示意图。设置抽屉从侧边滑出（宽 ≤ 360 dp），**滑杆要粗**。
半透明深色玻璃底 + 细边框；文字加描边/阴影；状态提示（「被踩到了！」）用**大字 + 短时**。
一套 UI 材质走 `UI/Default` 变体，别混用 custom shader。

---

## 8. 架构（把"游戏"和"模型查看器"彻底分开）

```
Assets/Scripts/
  MMDPlayer/           ← Phase1 原样保留（Controller / UI / OrbitCamera / Guard / Library / Scanner）
  Game/
    Config/            GameConfig.cs (ScriptableObject) + 配置资产（CityPreset / Tier / ItemDef / AchievementDef）
    Core/              WorldScaler / SpawnValidator / ProceduralTextureCache / ObjectPool / DebugHud
    City/              CityGenerator / BuildingFactory / RoadBuilder / StreetProps / Vegetation / SkyTimeOfDay
    Player/            TinyPlayerController / VirtualJoystick / DodgeRoll
    Camera/            CameraRig (FPS/TPS/Orbit) / CameraCollision
    Giant/             GiantLoader / GiantessBrain / GiantLocomotion / StompDetector / GiantVfx / MMDPhysicsGuard
    HUD/               GameHud / SettingsDrawer / ResultPanel / SafeAreaFitter
    GameMode.cs        ← 组合以上，提供 Enter(mode) / Exit() / Restart()
    Editor/            GameModeSceneBuilder.cs（用代码搭场景，避免手写 .unity 坏引用）
```

- 编辑器脚本必须在 `Editor/` 目录下（否则编译错）。
- 可选 asmdef：`Minis.Game`、`Minis.Game.Editor`；若加，注意别与 UMT 形成循环引用，且 IL2CPP 剥离需要 `link.xml`（§9）。
- **内容数据化**：建筑预设、街具清单、成就、模式参数、她的性格参数**全走 ScriptableObject 配置资产**，加内容 = 加一份资产，不是改逻辑。

### `GameMode` 职责

- `Enter(mode)`：隐藏 Phase1 播放 UI → 生成城市 → **校验出生点**放小人 → 加载她 → 切相机 → 应用 `WorldScaler` → 挂 HUD。
- `Exit()`：**逐块销毁并还原**（§10 清单），保证模型查看器回来时和没进过一样。
- `Restart()`：只重置玩法状态（城市保留、她保留）。

---

## 9. 性能预算与构建配置

| 项目 | 预算 | 观察方式 |
|---|---|---|
| 目标帧率 | 60 FPS（低电量档允许 30） | `DebugHud` |
| **SetPass calls** | ≤ 150（城市场景，含她） | `Game 视图 ▸ Stats` |
| **Batches** | ≤ 300 | 同上 |
| 三角面 | 城市 ≤ 40 万（她的 PMX 另计） | 同上 |
| 实时光源 | **1 个投影平行光** + 自发光/贴花替代其余 | — |
| 阴影 | 2048² 以内，`shadowDistance` 收紧到近景 | — |
| 贴图 | 单张 ≤ 512；运行时生成贴图总量 ≤ 12 张 | — |
| 粒子 | 同屏 ≤ 200 | — |
| **GC 分配** | 稳态每帧 **0 B**（对象池、缓存、禁字符串拼接） | Profiler `GC Alloc` |

**超预算时的取舍顺序**：砍远景楼群 → 砍街道家具种类 → 降阴影分辨率/收紧 shadowDistance → 降粒子 → 降她身上的材质特性（lilToon → Unlit 回退）→ **最后才降建筑细节**。

### 9.1 Android 工程设置（构建前逐条核对）

- `Scripting Backend = IL2CPP`；`Target Architectures` **只勾 ARM64**（UMT 只有 `arm64-v8a` 的 `.so`）。
- `Managed Stripping Level = Low/Medium`（太高会剥掉 UMT / Newtonsoft 的反射路径）。
- **`link.xml` 保底**：

```xml
<linker>
  <assembly fullname="UMT" preserve="all"/>
  <assembly fullname="Newtonsoft.Json" preserve="all"/>
</linker>
```

- 图形 API：GLES3（Vulkan 可选）；`Multithreaded Rendering` 开；贴图压缩 ASTC；`Optimize Mesh Data` 开。
- `Screen.sleepTimeout = SleepTimeout.NeverSleep`（玩法中）。
- 城市分帧构建（协程 + 每帧预算），避免进入玩法时卡住。

---

## 10. **退出玩法时必须还原的 Unity 全局状态清单**（漏一个就出玄学 bug）

| 类别 | 项 |
|---|---|
| 渲染 | `RenderSettings.fog / fogColor / fogDensity / fogMode`、`ambientMode / ambientLight / ambientSkyColor / ambientEquatorColor / ambientGroundColor`、`RenderSettings.skybox`、太阳光的强度/颜色/阴影设置 |
| 质量 | `QualitySettings.shadowDistance`、`shadowResolution`、`antiAliasing`、`vSyncCount`、**`Application.targetFrameRate`（UMT 会设成 60）** |
| 物理 | `Physics.gravity`、`defaultContactOffset`、`sleepThreshold`、`queriesHitTriggers`、层碰撞矩阵（若改过） |
| 相机 | `clearFlags / backgroundColor / nearClipPlane / farClipPlane / fieldOfView` |
| 输入/UI | 自建的 `EventSystem`、摇杆、`Screen.orientation`、`Screen.sleepTimeout` |
| 时间 | **`Time.timeScale`**（慢动作特写会改） |
| 其它 | `AudioListener` 数量、粒子与贴图缓存上限、**`MMDTransformManager` 与 `MMDPhysicsManager` 的释放**（`DisposeRuntimeData()` / `DisposePhysics()` 有对应清理入口，漏了会留下原生内存） |

---

## 11. 分阶段实施（每阶段都要能真机跑、能截图判对错）

> 原则：**禁止把问题攒到最后一起调。**

### 阶段 0：地基（不写玩法）
- 锁横屏（`Player Settings ▸ Default Orientation = Auto Rotation`，只勾 Landscape Left/Right；取消 `Render outside safe area`）+ 安全区 + HUD 骨架（大按钮）。
- `WorldScaler` + `GameConfig` + 出生点校验 + 调试读数（FPS / SetPass / 坐标 / 速度 / 相机模式 / `isGrounded`）。
- **验收**：进游戏能看到地面；能稳定站在安全点；HUD 按钮够大能点中；连续 20 次重开不卡几何体。

### 阶段 1：相机 + 移动（"能玩"的门槛）
- `CameraRig` 三档 + 切换；相机碰撞与地面钳制；near/far 按尺度；设置面板生效并持久化。
- 小人移动 + 摇杆（与视角区不重叠）+ 加速插值 + 翻滚（含无敌帧）。
- **验收**：能自由跑动；视角不穿地不穿楼；灵敏度滑杆实时生效；`minMoveDistance = 0` 后"移动不了"消失。

### 阶段 2：城市（"精致"）
- `CityKit` 全量：建筑生成器 → 路面/人行道（带 Collider）→ 街具 → 绿化 → 天空/雾/时间档 → 光照与阴影。
- **验收**：§4.8 的 **9 条**全部打勾，且 `SetPass calls` 在预算内。

### 阶段 3：她 + 玩法闭环（P0 内容）
- `GiantLoader` + 状态机 + 步态（VMD 优先 / IK 目标兜底）+ 踩踏判定 + 免伤 + 演出 + 结算 + 重开。
- **视线锥 + 听觉**（让她真的"看得见 / 听得见"，藏身才有意义）。
- **自由沙盒模式（先做）+ 生存模式**。
- **验收**：需求文档 §7 的"上手 30 秒 / 2 分钟"全部成立；退出后零残留（对照 §10 逐项测）。

### 阶段 4：内容加料（P1，逐项独立提交，每项可单独演示）
- 她的日常、搜索行为、换鞋、情绪、动作库、连锁破坏、NPC 出卖你、爬上她。
- 公园/工地/水岸 + 地标；夜晚档；心跳、慢动作特写、局内播报、音效全量；巨人模式踩 NPC。
- **验收**：每加一项都能单独跑通并截图；连续叠加不崩、帧率不出预算。

### 阶段 5：打磨与加成（P2）
- 拍照 / 摆姿联动（见 `Unity_摆姿势模块_方案_v0.1.md`）、天气、成就、细节层。
- 性能压测与降级开关；**连续进出玩法 10 次不崩不残留**。
- **验收**：真机 60 FPS 稳定。

---

## 12. 坑清单（Unity 专属）

1. **缩放她 → 衣服乱飞。** 缩世界，不缩模型。
2. **相机 `nearClipPlane` 是第一杀手** —— 尺度变了它必须跟着变，否则"黑屏看不见"。
3. **`CharacterController.minMoveDistance` 必须置 0**，否则"移动不了 / 原地抖"。
4. **输入 API 与 `Active Input Handling` 必须一致**，否则输入直接抛异常。
5. **一切依赖骨骼最终位置的东西，都要排在 `[DefaultExecutionOrder(10000)]` 之后。**
6. **Bullet 地面是无限平面，不是 Collider** —— 给她裙摆"加楼 Collider"没有用。
7. **Android 只勾 ARM64**；IL2CPP 剥离要配 `link.xml`。
8. **退出必须还原全局状态**（§10），否则第二次进入越来越卡 / 画面变暗 / 帧率被锁。
9. **`Application.targetFrameRate` 被 UMT 设成 60**，退出要交还。
10. **程序化贴图必须缓存复用**，别每个物体生成一张。
11. **阴影别贪**：一个投影平行光足够，路灯用自发光；多个实时光直接掉一半帧。
12. **运行时生成的 Mesh / Texture / ScriptableObject 都要 `Destroy`**，否则反复进出泄漏。
13. **`Time.timeScale` 慢动作后必须还原**（还影响 UMT 的物理与 IK 时序）。
14. **中文文件名 / 路径**要在导入管线里统一处理。
15. **出生点必须校验**，否则"开局卡在楼里"会被当成"游戏做坏了"。
16. **场景别手写 `.unity` YAML**：优先用 `GameModeSceneBuilder` 代码搭场景，避免坏 GUID 引用。
17. **`Exit()` 里别忘了销毁她**：`MMDTransformManager.DisposeRuntimeData()` / `MMDPhysicsManager.DisposePhysics()`。
18. **不要用 `GameObject.CreatePrimitive` 拼城市**（默认 Collider + 默认材质的味道一眼假）。
19. **没装 lilToon 时 PMX 会回退成 `Unlit/Texture`**（不受光、扁平）—— 这是"看起来没有质感"的常见原因，不是你的代码错了。
20. **macOS / Linux 编辑器没有 Bullet 物理**，别以为是 bug；`MMDPhysicsGuard` 要优雅降级。

---

## 13. 交付纪律（**必须遵守**）

1. **禁止声称"已测试 / 已验证可运行 / 我运行过"**。只能做静态自查。
2. **禁止编造运行结果、截图、日志。**
3. 每阶段提交时必须写清：**这一版解决了什么、怎么验证**（具体到点哪个按钮、看什么现象）。
4. 完成不了的部分**如实说明**，不要假装完成。
5. 报告格式：
   - ① 本次新增 / 修改文件清单（路径 + 一句话用途）
   - ② 对照需求文档的验收项，逐项标注 `[已实现]` / `[待真机验证]`（没把握的一律标待验证）
   - ③ 我识别到但无法确认的风险点
   - ④ 给用户的操作指引（怎么进玩法、怎么验证）
   - ⑤ 与规格不同的取舍及理由

---

## 14. 版本记录

- **v1.0（Unity 版）** —— 从 Babylon 口径转写为 Unity/UMT 口径；
  确立三条工程铁律（缩世界不缩模型 / 禁止魔法数 / 退出零残留）；
  补全 Unity 全局量缩放清单（§1.2）、`MMDTransformManager` 执行顺序（§2.3）、Bullet 地面是无限平面（§4.3）、
  材质回退链与 lilToon（§4.6）、ARM64 与 `link.xml`（§9.1）、退出还原清单（§10）；
  阶段 3 的必须项为「状态机 + 步态 + 踩踏判定 + 视线与听觉 + 沙盒/生存双模式」。

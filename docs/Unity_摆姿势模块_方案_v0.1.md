# Unity 版 · 摆姿势模块 方案 v0.1（手办模特式骨骼摆姿）

> **目标仓库**：`aaa621bbb/MMDHandmade-Unity`（Unity + UMT 0.5.1 + Built-in RP，Android arm64）
> **定位**：Phase2 之外的独立模块（与踩城玩法平级），通过顶栏模式切换进入：
> `播放 VMD` ⟷ `踩城玩法` ⟷ `摆姿势`
> **参考交互范式**（市面上"手办模特/摆姿势"App 的通用做法，无需照抄某个 App）：
> 点选部位 → 出现旋转圈 gizmo → 拖拽旋转；也可用**单轴滑杆**精调；支持 FK 链、镜像、姿势存档（9 格）；可细到**每根手指的每一节**。
>
> **诚实声明**：本文全部结论基于 UMT 0.5.1 源码；**没有一条声称已运行验证**。凡"只能在真机上确认"的，都标了【待真机】。

---

## 0. 一句话结论

摆姿模块的全部难点只有一个：**UMT 的 `MMDTransformManager` 才是骨骼的唯一权威，它在 `LateUpdate`（`[DefaultExecutionOrder(10000)]`、`accessTransforms: true`）里**采样 Unity Transform → 解算约束/IK → 再回写 Transform。
所以：

> **你要先把 Animator 与求解器"按住"，你的手才能按得动骨头；松手后想让它保持，就必须持续把姿势写回去。**

这句话决定了本模块的全部结构（§2、§3）。做对了，其他都是 UI 活。

---

## 1. 目标与体验定义

| 项目 | 要求 |
|---|---|
| 可控范围 | 全身主骨（头/颈/腰/胸/上腕/前腕/手首/大腿/膝/足首/足先），**并支持到每根手指的每一节**（MMD 标准模型含 5 指 × 2~3 节） |
| 交互 | ① 点击选中部位（高亮 + 名称显示）② 旋转圈 gizmo 拖拽旋转 ③ 单轴滑杆精调（三轴各一根）④ FK 链联动（子骨跟随） |
| 附加 | 镜像（左右对称）、姿势存档 9 格 + 双手单独存档 9 格、复位（bind pose）、不等比缩放（可选）、导入/导出 JSON |
| 性能 | 摆姿模式下**不跑物理也不跑动画**，交互必须跟手（≥ 30 交互帧感）；**但退出后必须完全还原** |
| 输出 | 存下来的姿势可用于：① 拍照模式与城市同框；② 作为巨人的"定格动作"投放进玩法（§7） |

---

## 2. 核心机制：如何"按住"求解器（本模块最容易做错的地方）

### 2.1 现状（源码事实）

- `MMDTransformManager`：`[DefaultExecutionOrder(10000)]`、`[ExecuteInEditMode]`、`LateUpdate()` → `TransformAll(Time.deltaTime, accessTransforms: true, shouldRunLivePhysics)`。
- 骨骼是挂在骨骼 GameObject 上的 `MMDBoneTransform`（`MonoBehaviour`），带 `initialLocalPosition` / `initialLocalRotation`（复位基准）。
- `transformEnabled` 为 false 时，`LateUpdate` **整段求解被跳过**（连 SDEF 回写都只保留 reconcile 分支）。
- `ResetToBindPose()` 用 `initialLocalPosition/Rotation` 复位；`ResetPhysics()` 重置物理；`solverResetPending` 用于在下次采样时清掉该骨的原生解算缓存。
- 物理默认 `livePhysics` 受平台限制（§5.3 见主方案）。

### 2.2 结论：三种子模式，按需要选

| 子模式 | 求解器设置 | 用途 | 说明 |
|---|---|---|---|
| **A. 纯 FK 模式**（默认） | `tm.transformEnabled = false`；`Animator.enabled = false`；物理关 | 绝大多数摆姿操作 | 你写的骨骼局部姿态**不会被覆盖**，最稳定、最好预测 |
| **B. FK + 约束模式** | `Animator.enabled = false`；`tm.transformEnabled = true`；`solveIK = false`；`solveConstraints = true` | 需要 MMD 的"回転付与/捩り"等约束生效（腰→胸、腕→捩り骨） | 你写 FK 局部旋转，求解器附加约束效果，观感更还原 |
| **C. IK 模式**（可选） | `Animator.enabled = false`；`tm.transformEnabled = true`；`solveIK = true` | 想"拖动脚/手到位置"而不是旋转关节 | 通过移动 IK 目标 Transform 驱动；国内"摆姿势"类 App 的高级功能，**P2** |

> **实现建议**：默认 A；UI 右上给个"启用 MMD 约束（实验）"开关切 B。
> **不要**在 `Animator.enabled = true` 的情况下摆姿——Animator 会每帧覆盖骨骼，你看到的现象就是"手一松姿势就弹回去"。

### 2.3 进入 / 退出摆姿模式（必须逐项做全）

**进入 `PoseMode.Enter(modelRoot)`：**

1. 记录并保存当前状态：`Animator.enabled`、`Animator.speed`、`tm.transformEnabled`、`tm.solveIK`、`tm.solveConstraints`、`tm.livePhysics`。
2. `Animator.enabled = false`（**不要用 `Playable` 暂停来凑，直接禁**）。
3. `tm.livePhysics = false; tm.ResetPhysics();`（物理关掉，否则衣服会自己动、并和你抢骨骼）。
4. `tm.transformEnabled = false`（子模式 A）；或按 §2.2 选 B/C。
5. 开启摆姿相机（复用主方案 §5 的"观察/自由"机位，`FocusOn(bounds)` 对准角色）。
6. 开启摆姿 HUD（骨骼列表 + 滑杆 + gizmo 层），隐藏播放 UI / 玩法 HUD。
7. 若进入前在玩法里，先 `GameMode.Exit()`（城市保留但冻结，或直接隐藏，看产品取舍）。

**退出 `PoseMode.Exit()`（逐项还原，缺一项就会出现"退出后不对劲"）：**

1. `tm.transformEnabled` / `solveIK` / `solveConstraints` / `livePhysics` 还原（`livePhysics` 若开着要 `ResetPhysics()`）。
2. `Animator.enabled` / `speed` 还原。
3. 相机 mode / FOV / near-far 还原（主方案 §10.3 清单）。
4. HUD 与 gizmo 销毁；`Time.timeScale` 若改过要还原。
5. 若做了"姿势作用于玩法"，按用户选择决定是保留姿势还是 `ResetToBindPose()`。

---

## 3. 交互实现（Unity 版）

### 3.1 部位选择

- **主选方式：骨骼列表面板**（分组：头颈 / 躯干 / 左臂 / 右臂 / 左手 / 右手 / 左腿 / 右腿 / 显示骨）。理由：手指点 3D 骨骼在手机上**极难精准**，列表是可靠性兜底。
- **辅助方式：3D 点选**。每个可选骨挂一个**不可见的球体 Collider**（半径按角色尺寸，建议体感 3~5 cm 换算），`Physics.Raycast` 从相机发射；
  命中判定要**优先近处**（`Physics.RaycastAll` 后按距离排序），并且**排除不可选骨**（`捩り`、`操作中心`、`ダミー`、`表示枠` 类）。
- 选中反馈：该骨高亮（材质换色或描边）+ HUD 显示骨名 + gizmo 出现在该骨位置。
- 骨名显示建议保留 MMD 原名（日文），因为用户在自己的模型里就是这么认的；`applyRenames=true` 后的名字可在括号里附注。

### 3.2 旋转 gizmo（运行时，不能用 Editor Handles）

Android 上没有 `UnityEditor.Handles`，必须自建：

- 三个圆环（X 红 / Y 绿 / Z 蓝），用细环面（`Torus` 程序化生成，或用带 alpha 的环状面片），**每个环一个独立 Collider**（环带厚一点，方便手指命中）。
- 拖拽逻辑：按下环 → 记录起点射线与圆环平面的交点 → 角度差 = 旋转角 → **绕该环的轴**旋转骨骼局部旋转（`bone.localRotation = Quaternion.AngleAxis(delta, axis) * startLocalRot`）。
- 显示模式：选中骨时只显示三种环（避免遮挡），可切"世界轴 / 局部轴"。
- 屏幕空间补偿：手机手指遮挡严重，gizmo 环半径建议按**屏幕像素**固定（例如 120~180 px 等效），而不是世界尺寸；缩放随相机距离动态调整。

### 3.3 单轴滑杆（真正好用的精调）

- 三根滑杆：X / Y / Z 欧拉角，范围 −180 ~ 180（或按骨类型限制，例如膝只有 X 轴有意义 → 其余轴置灰）。
- 滑杆值 ↔ 骨骼 `localRotation` 双向同步（**必须双向**：拖 gizmo 时滑杆跟着动，拖滑杆时模型跟着动）。
- 欧拉角显示与写回要注意万向锁：内部存 `Quaternion`，UI 用 `localEulerAngles`（Unity 的 `localEulerAngles` 与 MMD 的欧拉顺序可能不同，**显示用即可，写回统一走四元数**，避免绕圈跳变）。
- **数值要能精确输入**（长按滑杆弹输入框），这是"手办摆姿"用户的真实习惯。

### 3.4 FK 链

- 摆上臂 → 前腕/手首**跟随**（因为它们本来就是子节点，Unity 层级天然跟随）。
- 提供"链式旋转"开关：开启时，旋转父骨时子骨**保持世界朝向**（`Quaternion.Inverse(parentDelta) * childWorldRot`），适合"肩膀转但手掌不掉"的姿态调整。
- 提供"仅根骨"模式：只允许调约束根，其余自动。

### 3.5 镜像

- MMD 标准骨名成对：`左/右`（或重命名后的 `.L` / `.R`）。用 UMT 的 `PMXRenameLists`（`PMXBoneBuilder`/`PMXRenameUtilities` 生成的重命名表）来做**成对匹配**，比手写映射可靠。
- 镜像规则：`rotation → (x, -y, -z)`（对 X 轴对称；具体符号按实测校准【待真机】），`position.x → -position.x`。
- 提供"整体镜像"与"仅当前链镜像"。

### 3.6 不等比缩放（可选，P2）

- 直接改 `bone.localScale`。**注意**：缩放骨骼会影响 SDEF/蒙皮观感，且 MMD 本身不支持骨骼缩放；
  建议只作为"造型夸张"玩具提供，并在 UI 上注明"可能让网格变形看起来不自然"。

### 3.7 复位

- 单骨复位：`initialLocalRotation` / `initialLocalPosition`（UMT 已经存在 `MMDBoneTransform` 上）。
- 整体复位：`tm.ResetToBindPose()`（然后别忘了 `tm.solverResetPending` 机制会自动清理原生解算缓存，无需手动干预）。
- 复位后如仍开着物理，`tm.ResetPhysics()`。

---

## 4. 姿势存档

- 格式：**纯数据 JSON**（不要用 AnimationClip，理由见 §7.2）：

```json
{
  "name": "打招呼",
  "version": 1,
  "bones": [
    { "name": "腕.L", "rot": [0.0, 0.0, 0.0, 1.0], "pos": [0,0,0], "scale": [1,1,1] }
  ]
}
```

- 存档位置：`Application.persistentDataPath/poses/*.json`（手机可读写，不会被 Asset 只读限制挡住）。
- UI：9 格姿势 + 9 格"双手"（只存 手首/指 的骨），长按可改名/删除，可导出分享（可选）。
- 应用姿势：逐骨匹配 **骨名**；找不到的骨跳过并计入"未匹配 N 根"提示（不同模型骨骼命名不同，必须容错）。

---

## 5. 相机与显示（摆姿模式专属）

- 机位：复用主方案 §5 的"观察/自由"档（环绕 + 推拉 + 平移 + 双击复位），并提供"对准全身/对准上半身/对准手部"三个快捷取景按钮。
- 快捷网格与地面：一个淡色地面网格（`GL`/LineRenderer 或程序化网格贴图）+ 阴影，给"手办站在台座上"的感觉（可选台座圆盘）。
- 灯光：摆姿模式建议切到**三点光**（主光 + 补光 + 轮廓光），并且——如果装不了 lilToon，UMT 会回退 `Unlit/Texture`（模型完全不受光）——**这一点对摆姿模式尤其致命**（看不出体积感）。
  → 摆姿模式的验收前提是：**装 lilToon**（免费、MIT、Built-in RP 可用），或用"高光贴图 + 描边"的自定义替代方案。
- 可选：截图/录像按钮（`ScreenCapture.CaptureScreenshot`），出片用。

---

## 6. 关键风险（Unity/UMT 专属）

1. **Animator 没禁 → 姿势弹回**（头号坑，§2.3）。
2. **求解器没停 → 你写的 FK 被 IK/约束重解**（子模式选错，§2.2）。
3. **物理没关 → 衣服自己动 + 跟你抢骨骼**；而且没调 `ResetPhysics()` 时旧速度会残留。
4. **退出没还原 → 回到播放模式后模型诡异地保持姿势/相机不对/帧率被锁**（§2.3 的五项 + 主方案 §10.3）。
5. **骨骼选择不准 → 用户以为"点不动"**：列表兜底 + 放大 Collider + 排序取近。
6. **`.L/.R` 配对靠猜 → 镜像错乱**：用 UMT 的重命名表，不要手写。
7. **欧拉角直接写回 → 万向锁跳变**：内部四元数。
8. **改缩放骨骼 → 蒙皮/SDEF 观感崩**：只作为玩具，且要能一键复位。
9. **手指骨数量多 → UI 卡**：骨骼列表要虚拟化/分页，gizmo 只对选中骨创建。
10. **在 Android 上做运行时 gizmo → 触摸精度**：环带加厚、屏幕空间半径、拖拽死区（起始 8~12 px 才认定开始拖）。

---

## 7. 与游戏玩法的联动（P2，性价比很高）

### 7.1 拍照模式

冻结 AI 与物理，自由机位 + 摆好的姿势 + 城市同框 → 截图。这是"手办摆姿"用户最想要的产品形态之一。

### 7.2 作为巨人动作（**注意边界，别踩 Phase1 的红线**）

- Phase1 的红线是：**禁止手写 VMD 关键帧、禁止自造骨骼曲线去替代 VMD**。这条红线继续有效。
- 因此联动只做两件**不越线**的事：
  1. **定格投放**：把姿势作为"静态定格"（`Animator.enabled=false` + `transformEnabled=false`）用于入场、结算、特写（慢动作定格），这不需要任何动画曲线。
  2. **开发期转 clip**：若确实要变成会动的动作，在**编辑器**里用 `AnimationUtility` 把姿势数据生成 `AnimationClip`（挂在 MMD 骨上、并保留 `_tm` 求解），由**离线工具**完成，运行时不做。
- 报告里必须写清选了哪条，不要含糊。

---

## 8. 交付物清单

```
Assets/Scripts/Pose/
  PoseMode.cs              // Enter/Exit + 状态保存还原（§2.3 五步）
  PoseBoneRegistry.cs      // 骨骼发现/分组/可选过滤（排除捩り/操作中心/ダミー）
  PoseSelection.cs         // 列表选择 + 3D 射线选择
  PoseGizmo.cs             // 运行时三环 gizmo（§3.2）
  PoseSliders.cs           // 三轴滑杆 + 精确输入（§3.3）
  PoseMirror.cs            // 基于 UMT 重命名表的左右配对（§3.5）
  PoseStore.cs             // JSON 存档 9+9 格（§4）
  PoseHud.cs               // 摆姿 UI 布局（横屏，尺寸按主方案 §9.1）
  PoseCameraRig.cs         // 复用主方案 CameraRig 的观察档 + 三个取景快捷
  Editor/PoseClipBaker.cs  // （可选）开发期把姿势转 AnimationClip（§7.2）
```

- 全部新增，**不改 UMT 与 Phase1 代码**；新增内容都在 `Assets/Scripts/Pose/` 与 `Assets/Resources/Pose/` 下。
- UI 必须走主方案 §9 的横屏尺寸规范（参考分辨率 1920×1080，触摸目标 ≥ 48 dp ≈ 144 px_ref）。

---

## 9. 验收清单

**静态可查（不需要真机）**
- [ ] 进入摆姿：Animator 已禁、物理已关、求解器状态按所选子模式设置，且都做了"状态保存"。
- [ ] 退出摆姿：§2.3 的五项全部还原（逐行对照代码）。
- [ ] 选择、gizmo、滑杆、镜像、存档的数据流是单向闭环（UI → 数据 → 骨骼），没有两处同时写骨骼。
- [ ] 存档 JSON 的读写路径在 `persistentDataPath` 下，失败有回显。

**【待真机验证】**
- [ ] 拖拽 gizmo 时骨骼跟手，松手后**姿势保持不弹回**（证明 Animator/求解器被按住了）。
- [ ] 滑杆与 gizmo 双向同步无跳变；膝/肘这类单轴骨限制正确。
- [ ] 镜像后左右对称（按实测校准符号）。
- [ ] 手指每节能单独弯曲（Mmd 手指骨齐全时）。
- [ ] 退出后回到播放模式，模型、相机、物理、帧率全部正常；连续进出 10 次不残留、不涨内存。
- [ ] 摆姿模式下模型的体积感/光影正常（验证 lilToon 是否生效）。

---

*文档版本 v0.1（Unity 口径）；与 `Unity_巨人踩城_重做方案_v2.1.md`、`TASK_Phase2.md` 配套。*

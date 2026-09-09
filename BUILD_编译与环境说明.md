# 编译与环境说明书（给后续 AI / 用户）

> 用途：说明目前项目处在什么阶段、目标产物是什么、如何从源码得到「安卓可运行 App」，以及最终验证要过哪些关。请在动手改代码前阅读本文件，避免方向错误。

---

## 1. 项目现状一句话
仓库 `aaa621bbb/MMDHandmade-Unity` 是一个 **Unity 6.6 工程**，目标平台 **Android**（arm64）。目前仓库内只有：
- UMT 插件（`Packages/com.candidumgames.unitymmdtools/`，第三方，只读别改）
- 三份说明文档（见 §4）
- `Assets/Scenes/MainScene.unity`（旧的默认空白场景，非目标产物）

**尚未存在**一份能直接构建出 App 的自研代码/场景。真正的 MMD 播放器（Phase1）与「女巨人城·踩小人」（Phase2，见 `TASK_Phase2.md`）都还没实现。仓库当前 `main` 在 `89122df`。

## 2. 目标产物
一台 **Android 手机**上可安装运行、可玩「女巨人城·踩小人」的 App：
- 玩家是极小人（≈女巨人脚趾），在程序生成的城市里被女巨人追、尝试躲避/不被踩。
- 女巨人 pmx **由用户在手机文件夹放入并运行时导入、可随意更换**。
- 程序小人、程序城市、Bullet 物理（安卓 arm64 原生可用）、无敌开关（沙盒）。
- 更完整玩法与硬性要求见 `TASK_Phase2.md`。

## 3. 编译（从源码到安卓 App）——这是最终交付必须走通的一步
**任何「只写代码不构 Build」的交付都不算完成。** 但需明确：**Build 必须在装有 Unity 6.6 + Android 模块的真机/电脑上执行**。AI 若没有 Unity/无法 Build，则交付范围是：把代码与工程配置写对到「用户照本文件步骤即可顺利 Build」，并在报告里标注哪些需用户真机验证。

### 3.1 需要的环境（用户侧）
- **Windows 或 macOS 电脑**。
- **Unity 6.6**（编辑器版本与此仓库兼容），安装时勾选 **Android Build Support + Android SDK/NDK + OpenJDK**（或后续在 Preferences 指认）。
- 一台 **Android 手机**（arm64，开「开发者选项 + USB 调试」）或可接受先出 .apk 再侧载。

### 3.2 首次打开（必做）
1. 用 Unity 6.6 打开工程 → 允许「升级工程」弹窗 → 等待导入/编依赖完成。
2. 打开后若 **Console 有大量错误**：极可能是 UMT 依赖与 Unity 6.6 版本冲突。处理优先级（不要降级 Unity）：
   a. 在根 `Packages/manifest.json` 用 Unity 6.6 认可的版本声明完成缺失/冲突包（burst / collections / mathematics / nuget.newtonsoft-json / ugui）。
   b. 若 UMT 用了已改名的 API：在用户自己的代码里写兼容垫片（不改 UMT 源码）。
   c. 记录最终能编译通过的依赖版本组合，写回 manifest 与 README。
   - 判定：**Console 无红错** 才算可下一步。

### 3.3 Build Settings 配置（务必正确，缺一会 Build 失败或黑屏）
1. `File → Build Settings` → 平台 **Android** → `Switch Platform`（首次会下载 Android 支持，耗时耐心等）。
2. **把目标场景加入 Scenes In Build**：
   - Phase2 完成应提供能在手机玩的目标场景（如 `GiantCity` / `MMDSandbox`），把**那个场景**加进列表。若同时存在多个玩法场景，确保手机启动进的是你要的玩法场景（可放 Build 列表首位）。缺这一步 → 打包出来只有默认空场景/黑屏。
3. 点 `Player Settings…`，至少确认：
   - `Company Name` / `Product Name`（随便填，但不能空）。
   - `Other Settings ▸ Scripting Backend` = **IL2CPP**（UMT 原生插件通常要求 IL2CPP，且 Mono 在部分平台受限）。
   - `Target Architectures` = **ARM64**（勾选）。
   - `Minimum API Level` 用一个常见手机会装的（如 26+），太高导致老手机装不了、太低可能缺 API，按 UMT/Unity 6 常规即可。
   - `Internet Access`/权限：若运行时从手机存储读 pmx，可能需要**存储相关权限**（视目标 API 级别：Android 13+ 用 SAF/系统文件选择器通常不需要 `READ_EXTERNAL_STORAGE` 全权限，但要按你实现的读取方式来声明）。实现者需在此处与 `MobileModelPicker` 一致配好清单权限。
4. 连手机（USB 调试开）或直接点心形 Build：
   - `Build`（生成 .apk 文件到指定路径，可选签名后装到手机）；
   - 若 Build 时报 **NDK/SDK 缺失** → 回 Preferences ▸ External Tools 指认，或装官方 Android Build Support。
5. 装机 → 授权 → 打开玩法场景。

### 3.4 常见 Build 失败排查（写给后续 AI，别让用户一遍遍试）
- **「Mono/IL2CPP 平台插件不匹配」**：UMT Bullet 插件在 Android arm64 是 `.so`，确认工程目标 ARM64 且脚本后端 IL2CPP。
- **「场景没进 Build」导致黑屏**：见 §3.3.2。
- **依赖冲突红错**：见 §3.2.2。
- **SAF/存储读 pmx 失败**：Android 高版本对文件访问收紧；确保用系统文件选择器（Intent/SAF）拿到授权 `content://` URI 再读字节，别假设能直接路径读。
- 任何 Build 在 Unity 端报的错，把**完整报错**贴回去给实现者，别只给一句「打不开」。

## 4. 仓库现有文档（互相引用，按需读）
| 文件 | 内容 |
|---|---|
| `TASK_给实现AI.md` | Phase1：Unity MMD 播放器半成品完整需求 + 交付纪律（诚实、报告格式、编码约束） |
| `TASK_Phase2.md` | **Phase2 本案**：女巨人城·踩小人完整玩法规格（Android、女巨人手机导入+换、程序小人、城市、物理不漏飞、无敌沙盒） |
| `docs/Unity_MMD_半成品_方案_v0.1.md` | UMT 用法权威参考 + Phase1 规格（读 UMT 前先看这份） |

## 5. 谁的职责边界（重要，别互相甩锅）
- **用户**：提供 Unity 6.6 环境、Android 手机、真实女巨人 `.pmx`（用户自备），执行最终 Build 与真机试玩。
- **实现 AI**：把 `TASK_Phase2.md` 的玩法 + 代码 + 工程配置（Player Settings、场景进 Build、存储权限、android 构建必要项）写到「用户照 §3 即可 Build」的程度；因为无法运行 Unity，必须**明确标识哪些是「用户需真机验证」**，不得谎称已 Build 成功。
- **Build/真机执行本身**：通常由用户在电脑 Unity 完成，或由有 Unity 的 AI/环境完成。交付完成标志 = 用户真机跑通 **§6 验收清单**，不是「AI 说写完了」。

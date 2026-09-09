# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [0.5.1] - 2026-09-02

### Fixed

- PMX import failed with `UMTResources is missing m_PMXRenameListsJson` when the package was installed as an immutable UPM package (e.g. via git URL into `Library/PackageCache`). Seven resource `.meta` files (`PMXRenameLists.json`, `PMXHumanoidAvatarMappings.json`, `PMXVRMSpringBoneGroups.json`, and the four Kawazu dictionary `.bytes` files) declared `DefaultImporter`, so Unity imported them as `DefaultAsset` instead of `TextAsset` and every `TextAsset` reference on `UMTResources.asset` resolved to null. The metas now declare `TextScriptImporter`; GUIDs are unchanged, so existing references keep working. (#1)

## [0.5.0] - 2026-07-10

### Added

- `MMDBulletPhysics.SetConfig(in NativeConfig)` and the corresponding native `MMDBulletPhysicsSetConfig` entry point. `NativeConfig` exposes tunable context parameters (ground plane, MMD unit scale, convex collision margin, Spring6DOF translation/rotation spring damping, locked-translation reinforcement); native defaults reproduce the previous hardcoded behavior, and `MMDPhysicsManager` now applies the configuration to every context it creates.

### Fixed

- Kinematic (bone-driven) rigid bodies now move along an interpolated path from their previous-frame targets across fixed substeps, pushed without clearing velocity so Bullet derives proper kinematic velocities, replacing the once-per-frame teleport with zeroed velocity. Variable frame times are carried in a time accumulator so they map onto whole substeps without drifting. This gives the constraint solver continuously-moving anchors it can track at MMD's 4 solver iterations, so translation-locked spring-less chains (long hair) no longer blow apart during fast motion.
- Spring6DOF rotation springs now reproduce MMD's unit-scale reference at Unity's meter scale via spring damping compensation (`solverIterations / (referenceIterations * scale^2)`). Bullet's motor-based spring target velocity carries no inertia and is not invariant under uniform length scaling, so the meter-scale rotation springs were previously much softer than in MMD.
- Joints whose translation limits are fully locked get redundant point-to-point constraint copies (2 by default) that re-solve those equality rows once more per solver iteration, so locked chains converge at MMD's 4 iterations without changing the converged pose.

## [0.4.0] - 2026-07-05

### Changed

- MMD transform solver optimization pass, behaviour-preserving (physics bakes verified bit-exact against reference clips). `BoneSolverData` is split into a read-only `BoneSolverConfig` and a mutable `BoneSolverState` (hot/cold), and the Burst solver reads/writes elements by reference instead of copying full structs. Redundant work removed from the hot path: `float4x4.TRS` with unit scale replaced by the rotation/translation constructor, constraint-target loads hoisted into the constrained branch, and dead IK locals deleted.
- Bone transform sampling and flushing, and rigid-body transform flushing, now run as Burst `IJobParallelForTransform` jobs over cached `TransformAccessArray`s, replacing per-bone managed `Transform` calls. The per-frame managed mirror copy (`MMDBoneTransform.runtimeData`) is gone; external pose resets are signalled through a `solverResetPending` flag instead.
- Physics stepping memoizes bone world matrices within a step and iterates precomputed simulated/kinetic rigid-body index lists instead of re-filtering and re-sorting them every step.
- Play-mode GPU SDEF skinning is now zero-GC per frame. The skinner swaps in the same pass-through mesh the CPU path uses and writes skinned vertices into its vertex buffer through a compute command buffer encoded once at initialization, instead of re-acquiring the renderer's ping-ponged output buffer (and re-encoding) every frame; Unity's own skinning consumes the pass-through mesh, keeping motion vectors correct. Edit mode keeps the previous write-to-renderer-output behaviour. Bone skinning matrices and quaternions are computed by a single parallel Burst transform job shared by the GPU and CPU paths.
- The VMD Clip Converter window now saves generated `.anim` files with text serialization.

### Added

- `MMDSDEFSkinner.NotifyRigChanged()` — call before re-rigging a skinner's renderer (changing `bones`/`rootBone`) so cached bone accessors and GPU resources are rebuilt on the next dispatch.

### Removed

- Breaking: `MMDBoneTransform.BoneSolverData` (replaced by `BoneSolverConfig`/`BoneSolverState`), the public `MMDBoneTransform.runtimeData` mirror field, and the unused `boneName`/`localMatrix` solver fields. VMD converter helper signatures were updated to take the split config/state arrays.

## [0.3.1] - 2026-07-03

### Added

- CPU SDEF skinning. `MMDSDEFSkinner` now supports a Burst-job skinning path (`MMDSDEFSkinJob`) in addition to the existing GPU compute path, selected per `MMDTransformManager` via the new `sdefSkinningMode` (`SDEFSkinningMode.GPU`/`CPU`). CPU mode computes skinned vertices on the CPU and uploads them into an all-root-bone-weighted runtime pass-through mesh, so SDEF works on platforms without compute-shader support. A null compute shader forces CPU mode regardless of the serialized setting, and toggling SDEF or the mode at runtime takes effect immediately (restoring the renderer's original mesh when disabled). Both modes share one set of native buffers.

### Fixed

- Fixed flickering poses by clearing IK rotation for bones before establishing the pose from FK.

## [0.3.0] - 2026-07-02

### Added

- SDEF skinning. SDEF vertices are now deformed on the GPU via the new `MMDSDEFSkinner` component and its compute shader instead of being approximated as BDEF2. Each affected `SkinnedMeshRenderer` runs a per-frame compute pass that applies vertex-morph blend shapes and bone skinning (linear blend for BDEF vertices, the SDEF formula for SDEF vertices) into the renderer's output vertex buffer, driven by the owning `MMDTransformManager` after the bone solve.

### Changed

- Async load/bake APIs now return `System.Threading.Tasks.Task<T>` instead of Unity `Awaitable<T>` for Unity 2022.x compatibility. Affects `PMXReader.ReadAsync`, `PMXImporter.BuildUnityObjectsAsync`, `VMDReader.ReadAsync`, `VMDAnimationClipConverter.ConvertAsync`/`ConvertCameraAsync`, and `PMXAnimationPathBuilder.BuildAsync`.
- Lowered the `com.unity.ugui` dependency from `2.0.0` to `1.0.0` to broaden Unity 2022.x compatibility.

## [0.2.1] - 2026-06-30

### Changed

- Minor package updates to meet Unity Asset Store publishing requirements.

## [0.2.0] - 2026-06-29

### Added

- WebAssembly support.
- Face and eye material heuristics for lilToon shadow tuning.

### Changed

- Material transparency detection now runs on the CPU with a Burst job sampling decoded source-file pixels, replacing the GPU compute-shader path.
- Create asynchronous version of most hot path functions for loading models and baking animtions runtime.
- lilToon materials now also set `_ShadowReceive`, `_ShadowBorder`, and `_lilShadowCasterBias`. Faces
  use a 0.3 shadow border, eyes 0.1, everything else 0.5; faces and eyes also get a 0.05 shadow caster
  bias.

### Removed

- Unused P/Invoke entry points `MMDBulletPhysicsSetRigidBodyTransform` and
  `MMDBulletPhysicsGetRigidBodyMotionTransform`. The batched `MMDBulletPhysicsGetRigidBodyMotionTransforms`
  replaces them.

## [0.1.1] - 2026-06-28

### Added

- Per-slot external material overrides. `PMXImportOptions.materialOverrides` supplies a `Material` per
  generated slot instead of a generated one, surfaced in the importer inspector via a new **Materials**
  tab (Standard/Override creation modes, per-slot remap list, and an "Extract Materials..." action).
- Tabbed PMX importer inspector (Model, Rig, Animation, Materials), split into per-tab editors under
  `Editor/Importers/`.
- Camera clip frame-rate selection (30 / 60 / 120 fps) in the VMD Clip Converter. The native 30 fps VMD
  camera timeline is sub-sampled at higher integer multiples, preserving real-time duration and MMD hard
  cuts.
- `MMDConstants.k_VMDNativeFrameRate` (30 fps) constant.

### Changed

- Edge-drawing PMX materials now use lilToon's outline-capable variant (`Hidden/lilToonMultiOutline`);
  the plain `lilToonMulti` shader has no outline pass. URP and built-in fallbacks still drop edges.
- Reorganized editor scripted importers and inspectors into `Editor/Importers/`, with shared VMD
  progress reporting extracted to `VMDClipProgress`.

### Fixed

- lilToon outline width now converts correctly into lilToon's 1 cm slider unit (previously over-scaled),
  and outline color is no longer affected by scene lighting, matching MMD's flat edges.

## [0.1.0] - 2026-06-23

First release of Unity MMD Tools (UMT).

### Added

- PMX 2.0 model import (with PMX 2.1 read compatibility): meshes, materials, bones with bindposes, and
  vertex-morph blend shapes, generated as sub-assets of the imported `.pmx`.
- MMD runtime: `MMDTransformManager` for MMD transform order, constraints, and IK solving, plus an
  optional Bullet-backed `MMDPhysicsManager` for rigid bodies and joints.
- VMD motion conversion to `AnimationClip` with runtime-solved and baked-FK modes, optional physics
  baking, and morph/IK-toggle curves.
- Optional humanoid `Avatar` generation for retargeting.
- Japanese (Kawazu) and Chinese (PinyinNet) name romanization for materials, bones, and morphs.
- BMP and TGA texture decoding in addition to Unity's built-in PNG/JPG support.
- Native physics plugin for Windows x64 and Android arm64-v8a.
- Editor entry points: scripted importers for `.pmx` and `.vmd` assets, plus **Tools ▸ UMT ▸ VMD Clip
  Converter** and **Tools ▸ UMT ▸ Create Default Resources** menu commands.

# CLAUDE.md

给 Claude Code 的代码库指南。

## 概述

ColonistModification（制式改造）是一个 RimWorld mod，为游戏后期提供殖民者批量标准化改造功能——统一的基因植入或统一的仿生/植入物手术。

## 反编译源码参考

反编译的 RimWorld C# 源码位于 `F:\RiderProjects\Assembly-CSharp`。目标框架 .NET Framework 4.7.2，C# 7.3（不能用目标类型 `new()`，不能用 `ValueTuple` 解构）。

本 mod 使用的关键 RimWorld API：
- **DefDatabase<T>** — 所有游戏定义（ThingDef、RecipeDef、HediffDef、XenotypeDef 等）
- **GameComponent** — 自动注册的持久组件，用于管理器 tick 循环
- **Bill_Medical** — 医疗/手术 Bill 基类。有 `xenogerm` 字段、`GiverPawn`、`Notify_IterationCompleted`
- **BillStack** — 每个 pawn 的 Bill 队列（`Pawn.BillStack`，大写 B）
- **IExposable** — Scribe 存档/读档。**重要**：Scribe 使用 `GetUninitializedObject` 绕过字段初始化器。`Scribe_Collections.Look` 之后必须对集合字段做 null 守卫
- **Window / WindowStack** — UI 系统。`Dialog_MessageBox` 支持最多 3 个按钮（buttonA/buttonB/buttonC）
- **MainButtonDef + MainButtonWorker** — 通过 XML Def 注册底部栏按钮
- **Mod / ModSettings** — mod 入口和跨存档设置持久化。设置文件路径：`%AppData%\..\Config\Mod_{pkgId}_{className}.xml`
- **RecipeDef** — `addsHediff`、`workerClass`（继承自 `Recipe_InstallArtificialBodyPart`/`Recipe_InstallNaturalBodyPart`）、`appliedOnFixedBodyPartGroups`、`appliedOnFixedBodyParts`、`AllRecipes`（缓存）
- **BodyDefOf.Human.AllParts** — 将身体部位组解析为实际左右标签（如 "Legs" → "左腿""右腿"）
- **BodyPartRecord** — `LabelCap`（含左右）、`groups`（List<BodyPartGroupDef>）

## 构建

项目引用 RimWorld DLL，路径为 `F:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64_Data\Managed\`。使用 `dotnet build`，目标 .NET Framework 4.7.2。输出路径：`publish/1.6/Assemblies/ColonistModification.dll`。所有引用必须设 `<Private>False</Private>` 避免复制游戏 DLL 到输出目录。

## 当前架构

### 数据流

```
ModSettings（跨存档）               GameComponent（单存档）
┌──────────────────────┐          ┌──────────────────────────┐
│ UserTemplate[]       │          │ assignedTemplateIds       │
│   id, name           │◄─────────│   pawnID → templateID     │
│   recipeDefNames[]   │          │ pawnRecords               │
│   autoRetryOnFailure │          │   pawnID → Record[]       │
│   maxRetriesPerStep  │          │     completedRecipes      │
│   targetBodyDefName  │          │     status, condition     │
│   requireConfirm     │          └──────────────────────────┘
└──────────────────────┘                    │
                                    GameComponentTick（每250 tick）
                                            │
                      ┌─────────────────────┼──────────────────┐
                      ▼                     ▼                  ▼
                  Pass 1:              Pass 2:             Pass 3:
               统计进行中的           构建物资缓存         逐殖民者检查
               手术数（M）、          bound items          → 开始/排队/确认
               医生池

                      M >= MaxConcurrent？ → 排队
                      M < MaxConcurrent？  → 检查资源 → 创建 Bill → M++
```

### 关键设计决策

1. **模板跨存档（ModSettings）**：`UserTemplate` 存储在 `ColonistModificationSettings.templates` 中。玩家创建一次模板，所有存档共享。
2. **分配单存档（GameComponent）**：`assignedTemplateIds` 映射 pawn thingID → template ID。通过 `ExposeData` 单存档持久化。
3. **模板纯游戏内配置**：无 XML 模板定义（已移除）。所有模板通过"模板编辑"标签页创建/编辑。
4. **无序手术**：每个选中的配方是独立目标。无步骤顺序。`completedRecipeDefNames: HashSet<string>` 追踪每个配方完成状态。
5. **并发手术限制**：`MaxConcurrentSurgeries` 防止所有人躺床。超出者进入 `Queued` 状态，Bill 完成时自动出队。
6. **无资源预留**：每次创建 Bill 时检查实际地图状态。不做虚拟"预留"。实时资源检查自然防止重复分配（被消耗的物品直接消失）。
7. **种族筛选（BodyDef）**：模板有 `targetBodyDefName`。下拉框只显示有 `IsSurgery` 配方的人类like种族。分配模板时按殖民者身体模板过滤。
8. **弹窗确认**：Pending 时弹出 `Dialog_MessageBox`，含"开始改造""稍后""忽略"三个按钮。每个 tick 最多弹一个。
9. **Scribe null 安全**：所有 `List<T>` 和 `HashSet<T>` 字段在 `Scribe_Collections.Look` 之后必须判空，因为 Scribe 绕过字段初始化器。

### 文件映射

| 文件 | 用途 |
|------|------|
| `ColonistModificationMod.cs` | Mod 入口，静态 Instance，settings 宿主 |
| `ColonistModificationSettings.cs` | `List<UserTemplate>`、`EnsureDefaults()`、`ResolveAllReferences()` |
| `UserTemplate.cs` | 模板数据模型：id、name、recipeDefNames、resolvedRecipes、设置、`MedicineCategory` 枚举 |
| `ColonistModificationManager.cs` | GameComponent：tick 循环（3-pass）、pawn 分配、records、序列化、`MaxConcurrentSurgeries` |
| `Bill_ColonistModification.cs` | `Bill_Medical` 子类：`templateId`（string 非 Def 引用）、自动重试、多步骤、ExposeData |
| `ColonistModificationUtility.cs` | 静态工具：`CheckSurgeryConditionsFast`、`GetImplantRecipesByGroup`（缓存）、`BuildMaterialCache`、`GetAllBoundItems` |
| `Dialog_ColonistModification.cs` | 4 标签页 UI：概览（每人下拉分配）、待处理列表、已完成、模板编辑（植入物+基因子标签） |
| `MainButtonWorker_ColonistModification.cs` | 底部栏按钮 → 打开 Dialog_ColonistModification |
| `ColonistModificationDialogUtility.cs` | `OpenDialog()` 入口方法 |

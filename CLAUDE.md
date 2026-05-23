# CLAUDE.md

给 Claude Code 的代码库指南。

## 概述

ColonistModification（制式改造）是一个 RimWorld mod，为游戏后期提供殖民者批量标准化改造功能——统一植入物手术。

## 反编译源码参考

反编译的 RimWorld C# 源码位于 `F:\RiderProjects\Assembly-CSharp`。目标框架 .NET Framework 4.7.2，C# 7.3（不能用目标类型 `new()`，不能用 `ValueTuple` 解构）。

本 mod 使用的关键 RimWorld API：
- **DefDatabase<T>** — 所有游戏定义（ThingDef、RecipeDef、HediffDef、XenotypeDef 等）
- **GameComponent** — 自动注册的持久组件，用于管理器 tick 循环
- **Bill_Medical** — 医疗/手术 Bill。用 `new Bill_Medical(recipe, null)` 创建，`Part` 必须在 `AddBill` 之后设置
- **BillStack** — 每个 pawn 的 Bill 队列（`Pawn.BillStack`，大写 B）
- **IExposable** — Scribe 存档/读档。**重要**：Scribe 使用 `GetUninitializedObject` 绕过字段初始化器。`Scribe_Collections.Look` 之后必须对集合字段做 null 守卫
- **Window / WindowStack** — UI 系统。`PreOpen` 在首次渲染前调用。`Dialog_MessageBox` 支持最多 3 个按钮
- **MainButtonDef + MainButtonWorker** — 通过 XML Def 注册底部栏按钮
- **Mod / ModSettings** — mod 入口和跨存档设置持久化。设置文件路径：`%AppData%\..\Config\Mod_{pkgId}_{className}.xml`
- **RecipeDef** — `addsHediff`、`workerClass`、`GetPartsToApplyOn(pawn, recipe)`、`appliedOnFixedBodyPartGroups`
- **BodyPartRecord** — `LabelCap`（含左右）、`groups`

## 构建

项目引用 RimWorld DLL，路径为 `F:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64_Data\Managed\`。使用 `dotnet build`，目标 .NET Framework 4.7.2。输出路径：`publish/1.6/Assemblies/ColonistModification.dll`。所有引用必须设 `<Private>False</Private>`。

## 当前架构

### 数据流

```
ModSettings（跨存档配置文件）     GameComponent（单存档 .rws）
┌──────────────────────┐       ┌──────────────────────────┐
│ UserTemplate[]       │       │ assignedTemplateIds       │
│   id, name           │◄──────│   pawnID → templateID     │
│   recipeDefNames[]   │       │ pawnRecords               │
│   resolvedRecipes[]  │       │   pawnID → Record[]       │
│   设置（确认/延迟等） │       │     status, delayedTick  │
└──────────────────────┘       └──────────────────────────┘
                                        │
                        GameComponentTick（每250 tick）
                                        │
                        RefreshAllCaches() → 更新 recipeStatus 缓存
                                        │
                        CheckAllTemplates() → 读缓存 → 弹窗/加Bill
```

### 关键设计决策

1. **模板跨存档（ModSettings）**：`UserTemplate` 存储在配置文件中。`recipeDefNames` 存盘，`resolvedRecipes` 运行时解析（不序列化）。首次 `RefreshAllCaches` 时惰性调 `ResolveAllReferences`。
2. **分配存盘**：`assignedTemplateIds` 序列化到存档。`pawnRecords` 只存玩家决策相关状态（`PendingConfirmation`/`Delayed`/`Dismissed`）。
3. **无自定义 Bill**：使用原版 `Bill_Medical`。不再有 `Bill_ColonistModification`。
4. **bodyPart+recipe 分立追踪**：recipe 按 `GetPartsToApplyOn` 展开为部位行（左臂/右臂独立）。`IsRecipePartCompleted(pawn, recipe, part)` 实时查 hediff 判断完成。
5. **无状态推进**：tick 循环检测 → 条件满足加 Bill → 手术完成 hediff 出现 → 下次 tick 检测已完成 → 跳过该部位 → 加下一个。失败后 hediff 不出现 → 下次 tick 重新加 Bill。
6. **缓存驱动 UI**：`RefreshAllCaches` 填充 `record.recipeStatus`（key→null=通过/`__HAS_BILL__`=有单/失败原因）。UI 只读缓存，不调 `CheckSurgeryConditions`。
7. **每 tick 一个弹窗**：确认模式下每 tick 最多弹一个 `Dialog_MessageBox`。

### 存档数据结构

**在存档 .rws 中**：
- `assignedTemplateIds` — pawn ID → template ID
- `pawnRecords` — 每条含 `templateId`、`status`、`delayedUntilTick`
- `disabledTemplates`、`globallyIgnoredPawns`

**不在存档中**（运行时计算）：
- `surgeryLog`、`recipeStatus`、`conditionFailReason`、手术完成状态（从 hediff 实时检测）

### 文件映射

| 文件 | 用途 |
|------|------|
| `ColonistModificationMod.cs` | Mod 入口，静态 Instance，settings 宿主 |
| `ColonistModificationSettings.cs` | `List<UserTemplate>`、`EnsureDefaults()`、`ResolveAllReferences()` |
| `UserTemplate.cs` | 模板数据模型：id、name、recipeDefNames、resolvedRecipes、设置 |
| `ColonistModificationManager.cs` | GameComponent：tick 循环、`RefreshAllCaches`、`CheckAllTemplates`、序列化 |
| `ColonistModificationUtility.cs` | 静态工具：`CheckSurgeryConditions`、`GetImplantRecipesByGroup`、`HasRequiredMaterials` |
| `Dialog_ColonistModification.cs` | 5 标签页 UI：概览、未完成、已完成、日志、模板编辑 |
| `MainButtonWorker_ColonistModification.cs` | 底部栏按钮 → 打开对话框 |
| `ColonistModificationDialogUtility.cs` | `OpenDialog()` 入口 |

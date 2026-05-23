# CLAUDE.md

给 Claude Code 的代码库指南。

## 概述

ColonistModification（制式改造）是一个 RimWorld mod，为游戏后期提供殖民者批量标准化改造功能——为指定殖民者按模板批量添加植入物手术。

## 反编译源码参考

反编译的 RimWorld C# 源码位于 `F:\RiderProjects\Assembly-CSharp`。目标框架 .NET Framework 4.7.2，C# 7.3（不能用目标类型 `new()`，不能用 `ValueTuple` 解构）。

关键 RimWorld API：
- **DefDatabase<T>** — 所有游戏定义（ThingDef、RecipeDef、HediffDef、XenotypeDef 等）
- **GameComponent** — 自动注册的持久组件，`GameComponentTick()` 每 tick 调用
- **Bill_Medical** — 手术 Bill。用 `new Bill_Medical(recipe, null)` 创建，`Part` 必须在 `pawn.BillStack.AddBill(bill)` 之后设置
- **BillStack** — 每个 pawn 的 Bill 队列（`Pawn.BillStack`，大写 B）
- **IExposable** — Scribe 存档/读档。**重要**：Scribe 使用 `GetUninitializedObject` 绕过字段初始化器，`Scribe_Collections.Look` 之后必须对集合字段做 null 守卫
- **Window / WindowStack** — UI 系统。生命周期：构造 → `PreOpen()`（首帧前一次） → `DoWindowContents(Rect)`（每帧）
- **Dialog_MessageBox** — 确认弹窗，最多 3 个按钮（buttonA/buttonB/buttonC）
- **RecipeDef** — `addsHediff`（手术添加的 hediff）、`GetPartsToApplyOn(pawn, recipe)` 返回可用部位列表
- **BodyPartRecord** — `LabelCap`（含左右标签，如"左臂""右眼"）

## 构建

`dotnet build`，目标 .NET Framework 4.7.2，输出 `publish/1.6/Assemblies/ColonistModification.dll`。所有 DLL 引用设 `<Private>False</Private>` 避免复制游戏 DLL。

## 架构设计

### 一、数据模型

#### UserTemplate（跨存档，ModSettings 配置文件）
```
UserTemplate
├── id: string (GUID)           — 唯一标识
├── name: string                — 显示名称
├── recipeDefNames: List<string> — 手术配方名列表（序列化）
├── resolvedRecipes: List<RecipeDef> — 解析后的 RecipeDef 对象（运行时，不序列化）
├── autoRetryOnFailure: bool    — 失败自动重试
├── maxRetriesPerStep: int      — 最大重试次数
├── minColonyWealth: float      — 最低殖民地财富门槛
├── targetBodyDefName: string   — 目标种族身体模板
├── colonistsOnly / includeSlaves — 适用筛选
├── requirePlayerConfirmation: bool — 是否需要玩家确认
├── delayDays: int              — 延迟天数
├── minMedicineCategory: enum   — 最低药品等级
├── StepCount → resolvedRecipes.Count
└── ResolveReferences()         — 从 DefDatabase 解析 recipeDefNames → resolvedRecipes
```

#### PawnModificationRecord（单存档，GameComponent .rws）
```
PawnModificationRecord
├── templateId: string          — 关联的模板 ID
├── status: ModificationStatus  — Idle/PendingConfirmation/Completed/Dismissed/Delayed
├── delayedUntilTick: int       — 延迟到何时（Delayed 状态用）
├── conditionFailReason: string — 失败原因汇总（运行时）
├── recipeStatus: Dictionary<string, string> — 手术检测结果缓存（运行时）
│     key: "InstallBionicArm|左臂" 或 "ImplantXenogerm"
│     value: null = 条件通过
│            其他字符串 = 失败原因
│     （已有手术单通过 HasModificationBillForRecipe 实时查 BillStack）
└── ExposeData() — 序列化 templateId、status、delayedUntilTick
```

#### PendingRecipeItem（运行时展开）
```
PendingRecipeItem                — recipe+部位展开后的单条待处理项
├── recipe: RecipeDef           — 手术配方
├── part: BodyPartRecord        — 具体部位（null = 无部位手术）
├── Label → "安装仿生臂 (左臂)" — 显示名
└── Key → "InstallBionicArm|左臂" — 缓存 key
```

#### ModificationStatus 枚举
| 状态 | 含义 | 存盘 |
|------|------|------|
| `Idle` | 等待条件满足 | ✓ |
| `PendingConfirmation` | 弹出确认窗，等待玩家响应 | ✓ |
| `Completed` | 全部手术完成 | ✓ |
| `Dismissed` | 玩家点了"忽略" | ✓ |
| `Delayed` | 玩家点了"稍后提醒" | ✓ |

### 二、核心数据流

```
                        打开对话框 / 250 tick / 点刷新
                                    │
                                    ▼
                          RefreshAllCaches()
                                    │
                    ┌───────────────┼───────────────┐
                    ▼               ▼               ▼
              遍历所有殖民者    惰性 ResolveAll   逐 recipe+部位
              查 assignedId     References      调 CheckSurgeryConditions
                    │               │               │
                    ▼               ▼               ▼
             GetPendingRecipeItems  resolvedRecipes  recipeStatus[key] = 结果
             展开为部位列表         从 defNames 解析   + 写日志
                    │
                    ▼
              写入 record.recipeStatus 缓存
              写入 record.conditionFailReason
              （UI 后续直接读，不调 CheckSurgeryConditions）
                                    │
                                    ▼
                          CheckAllTemplates()
                          （仅 tick 循环，actOnResults=true）
                                    │
                    统计 activeSurgeries（全局已有手术单数）
                    遍历 pawn → 读缓存 → can?
                                    │
                    ┌───────────────┼───────────────┐
                    ▼               ▼               ▼
              确认模式         非确认模式        条件不满足
              ≥阈值: return    ≥阈值: return     跳过继续下一个
              <阈值: 弹窗      <阈值: CreateAnd
              PendingConf      AddBill++
              return           activeSurgeries++
```

### 三、手术完成判定

不用任何存储字段。实时查殖民者身上的 hediff：

```csharp
bool IsRecipePartCompleted(Pawn pawn, RecipeDef recipe, BodyPartRecord part)
{
    if (recipe.addsHediff == null) return false;
    if (part != null)
        return pawn.health.hediffSet.hediffs.Any(h =>
            h.def == recipe.addsHediff && h.Part == part);
    return pawn.health.hediffSet.HasHediff(recipe.addsHediff);
}
```

- 有部位：查该部位上是否有 recipe 添加的 hediff。例如左臂上有 `BionicArm` hediff → 左臂已完成
- 无部位：查全身是否有该 hediff。例如有 `Xenotype` hediff → 基因植入已完成

### 四、Bill 创建

```csharp
CreateAndAddBill(pawn, template, item)
    → new Bill_Medical(recipe, null)
    → pawn.BillStack.AddBill(bill)
    → bill.Part = item.part  // 必须在 AddBill 之后
    → 写日志 + 发消息
```

调用路径：
- **tick 非确认模式**：`CheckAllTemplates` 检查 `activeSurgeries < MaxConcurrentSurgeries` 后调
- **tick 确认模式**：弹窗 → 玩家点"开始改造" → 回调调 `CreateAndAddBill`
- **手动添加**：UI"添加"按钮 → `AddSurgeryForRecipe` → `CreateAndAddBill`

### 五、并发控制

`MaxConcurrentSurgeries`（当前=1）限制全局同时进行的手术数。`CheckAllTemplates` 开头遍历所有 BillStack 统计匹配本 mod 配方的 `Bill_Medical` 数量 → `activeSurgeries`。确认弹窗和直接加手术均检查 `activeSurgeries >= 阈值`，达到则 `return` 停止处理。手术后游戏自动移除 Bill → 下次 tick 计数下降 → 自动解封。

### 六、缓存值约定

`record.recipeStatus[key]` 的三个含义：

| 值 | 含义 | UI 显示 |
|----|------|---------|
| `null` | 条件通过 | 绿色"条件满足" + `[添加]` 按钮 |
| 其他字符串 | 条件不满足 | 灰色 + 具体原因 |
| BillStack 已有单 | 实时查 HasModificationBillForRecipe | 蓝色"已添加手术单" |

此约定在 `RefreshAllCaches` 中写入，UI 的 `DrawPendingList` 和 tick 的 `CheckAllTemplates` 都从缓存读取，不再调 `CheckSurgeryConditions`。

### 七、UI 渲染

5 个 tab，在 `DoWindowContents` 中按 `selectedTab` switch：
- **Tab 0 模板概览**：每殖民者一行，下拉选模板，显示 `已完成X 未完成Y (共Z台手术)`，hover 显示失败原因
- **Tab 1 未完成**：调用 `GetPendingRecipeItems` 获取待处理手术行，逐行显示条件状态和 `[添加]` 按钮
- **Tab 2 已完成**：调用 `GetAllRecipeItems` 获取全部手术行，✓/✗ 标记完成状态
- **Tab 3 日志**：从 `Manager.GetSurgeryLog()` 读，倒序显示检测记录和手术添加记录，可清除
- **Tab 4 模板编辑**：左侧模板列表，右侧编辑区（植入物 checkbox + 基因植入 + 参数设置）

刷新按钮在内容渲染前检测点击，确保同一帧数据更新。

### 八、存档策略

**序列化**（.rws 存档文件）：
- `assignedTemplateIds: Dictionary<int, string>` — 殖民者分配了哪个模板
- `pawnRecords: Dictionary<int, List<PawnModificationRecord>>` — 每条含 templateId、status、delayedUntilTick
- `disabledTemplates`、`globallyIgnoredPawns`

**不序列化**：
- 模板定义 → ModSettings 配置文件（跨存档共享）
- `recipeStatus`、`conditionFailReason` → 每次 `RefreshAllCaches` 重新计算
- `surgeryLog` → 纯运行时，日志过多可清除
- 完成状态 → 从 hediff 实时检测

加载存档时 `ExposeData` 会清除指向不存在模板的失效分配。

### 九、关键方法速查

| 方法 | 文件 | 作用 |
|------|------|------|
| `RefreshAllCaches()` | Manager | 统一缓存刷新入口，遍历所有 pawn 的所有 recipe+部位，检测条件，写入 recipeStatus |
| `CheckAllTemplates(tick, actOnResults)` | Manager | tick 动作循环：调 RefreshAllCaches 后读缓存，弹窗或加手术。actOnResults=false 时只刷新不动作 |
| `ForceCheckNow()` | Manager | 公开刷新入口（PreOpen、刷新按钮调用），调 RefreshAllCaches |
| `GetPendingRecipeItems(pawn, template)` | Manager | 返回未完成的 recipe+部位列表（展开部位，排除已完成的） |
| `GetAllRecipeItems(pawn, template)` | Manager | 返回全部 recipe+部位列表（含已完成，用于已完成 tab 和概览计数） |
| `IsRecipePartCompleted(pawn, recipe, part)` | Manager | 查殖民者身上 hediff 判断手术是否完成 |
| `HasModificationBillForRecipe(pawn, recipe, part)` | Manager | 查 BillStack 上是否已有对应手术单 |
| `CreateAndAddBill(pawn, template, item)` | Manager | 创建 Bill_Medical 加入 BillStack，用 activeSurgeries 计数控制并发 |
| `MaxConcurrentSurgeries` | Manager | 全局并发上限常量（当前=1） |
| `AssignTemplate / UnassignTemplate` | Manager | 分配/取消模板，调 RefreshAllCaches |
| `GetOrCreateRecord(pawn, template)` | Manager | 两级查找（pawnID → templateID），自动创建 |
| `CheckSurgeryConditions(pawn, recipe, map, minMed)` | Utility | 检查手术条件（部位、医生、药品、材料） |
| `GetImplantRecipesByGroup(bodyDef)` | Utility | 按身体部位分组返回植入物配方列表 |
| `ResolveReferences()` | UserTemplate | 从 DefDatabase 解析 recipeDefNames → resolvedRecipes |
| `PreOpen()` | Dialog | 窗口首帧前调 ForceCheckNow 确保数据就绪 |

### 十、文件映射

| 文件 | 用途 |
|------|------|
| `ColonistModificationMod.cs` | Mod 入口，`static Instance`，settings 宿主 |
| `ColonistModificationSettings.cs` | `List<UserTemplate>`、`EnsureDefaults()`、`ResolveAllReferences()`、`ExposeData` |
| `UserTemplate.cs` | 模板数据模型 + `ResolveReferences()` + `ExposeData` |
| `ColonistModificationManager.cs` | GameComponent：tick 循环、缓存刷新、动作处理、模板分配、序列化、日志 |
| `ColonistModificationUtility.cs` | 静态工具：`CheckSurgeryConditions`、`HasRequiredMedicine`、`HasRequiredMaterials`、`GetImplantRecipesByGroup` |
| `Dialog_ColonistModification.cs` | 5 tab UI 窗口：概览、未完成、已完成、日志、模板编辑 |
| `MainButtonWorker_ColonistModification.cs` | 底部栏按钮 → `OpenDialog()` |
| `ColonistModificationDialogUtility.cs` | `OpenDialog()` 入口 + 防重复打开 |

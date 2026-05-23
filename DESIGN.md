# 制式改造 Mod 设计文档

## 概述

游戏后期为每个殖民者实现标准化的制式改造，如统一基因植入、统一仿生体安装。解决原版逐个手动添加手术的繁琐操作，提供模板化批量管理。

## 核心需求

1. **模板系统**：定义一套标准改造方案（多个手术步骤按顺序执行）
2. **条件检测与通知**：物资、技能等条件满足时提示玩家
3. **玩家决策**：确认开始 / 延迟 / 不再提示
4. **失败自动重试**：手术失败后自动重新安排，无需手动操作
5. **多步骤自动推进**：上一步成功后自动创建下一步手术

## 架构

```
┌─────────────────────────────────────────────────────┐
│                    玩家交互层                         │
│  Dialog_ColonistModification (管理窗口)              │
│  Messages / Letter (通知)                           │
├─────────────────────────────────────────────────────┤
│                    核心逻辑层                         │
│  ColonistModificationManager (GameComponent)        │
│  ┌─────────────────────────────────────────────┐    │
│  │  每250 tick 遍历所有模板 × 所有殖民者       │    │
│  │  检查条件 → 更新状态 → 触发通知/手术       │    │
│  └─────────────────────────────────────────────┘    │
├─────────────────────────────────────────────────────┤
│                    手术执行层                         │
│  Bill_ColonistModification (自定义Bill)             │
│  ┌─────────────────────────────────────────────┐    │
│  │  重写 Notify_IterationCompleted             │    │
│  │  检测成败 → 重试 or 推进 or 跳过            │    │
│  └─────────────────────────────────────────────┘    │
│  ColonistModificationUtility (工具类)               │
├─────────────────────────────────────────────────────┤
│                    数据定义层                         │
│  ColonistModificationTemplateDef : Def              │
│  PawnModificationRecord (运行时状态)                 │
└─────────────────────────────────────────────────────┘
```

## 数据流

```
XML模板定义
     │
     ▼
ColonistModificationTemplateDef
  · recipeDefNames[]    → 手术步骤列表
  · autoRetryOnFailure  → 失败自动重试
  · maxRetriesPerStep   → 最大重试次数
  · pawnFilter          → 目标殖民者条件
  · requirePlayerConfirmation → 是否需要确认
     │
     ▼  ResolveReferences()
  · resolvedRecipes[]   → 解析后的RecipeDef引用
     │
     ▼  GameComponentTick (每250 tick)
Manager 遍历 (template × pawn)
     │
     ├── pawn不符合过滤 → 跳过
     ├── 已完成所有步骤 → status=Completed
     ├── 条件不满足     → status=Idle
     └── 条件满足
          ├── 需要确认 → status=PendingConfirmation → 发信件
          └── 不需确认 → 直接 StartSurgeryForPawn()
                              │
                              ▼
                    ColonistModificationUtility.CreateBillForStep()
                              │
                              ▼
                    Bill_ColonistModification 加入 pawn.billStack
                              │
                    pawn执行手术（原版JobDriver流程）
                              │
                              ▼
                    Bill.Notify_IterationCompleted()
                              │
                    ┌─────────┴──────────┐
                    ▼                    ▼
              手术成功              手术失败
              GetNextStepIndex()     自动重试？
              ├─还有下一步→新Bill    ├─是 & 次数<上限→retryBill
              └─全部完成→通知        └─否→跳过,推进下一步
```

## 状态机

```
每个 (殖民地, 模板) 对的状态：

   Idle ──条件满足──▶ PendingConfirmation ──玩家确认──▶ InProgress
     ▲                    │   │   │                        │
     │                    │   │   └──── 玩家忽略 ──▶ Dismissed
     │                    │   │
     │                    │   └──── 玩家延迟 ──▶ Delayed ──时间到──▶ Idle
     │                    │
     │                    └── 不需确认直接 ──▶ InProgress
     │
     ├── 条件不满足 ◀── 从 InProgress（Bill被手动删）
     │
     └── 全部完成 ◀── 从 InProgress
                  Completed
```

## 关键类说明

### ColonistModificationTemplateDef
- 继承 `Def`，通过XML加载
- `recipeDefNames` → `ResolveReferences()` → `resolvedRecipes`
- 包含所有模板级配置（财富阈值、年龄限制、药品等级等）

### ColonistModificationManager
- 继承 `GameComponent`，自动注册
- 核心数据：`Dictionary<int, List<PawnModificationRecord>> pawnRecords`
- `GameComponentTick()` 每250 tick触发 `CheckAllTemplates()`
- 存档通过 `ExposeData` 完整持久化

### Bill_ColonistModification
- 继承 `Bill_Medical`
- 重写 `Notify_IterationCompleted` 实现失败重试和步骤推进
- 在 base 调用前捕获所有状态引用（因为base会删除Bill）
- base 调用后检查结果并决定后续动作

### Dialog_ColonistModification
- 继承 `Window`，三标签页设计
- 标签1：模板概览（所有模板×殖民者的状态矩阵）
- 标签2：待处理列表（仅显示PendingConfirmation）
- 标签3：已完成记录
- 底部提供"一键确认全部"快捷操作

## 文件清单

```
workspace/ColonistModification/
├── About/About.xml
├── Source/ColonistModification/
│   ├── ColonistModificationTemplateDef.cs   # 模板定义
│   ├── ColonistModificationManager.cs       # 核心管理器 (GameComponent)
│   ├── Bill_ColonistModification.cs         # 自定义Bill (失败重试)
│   ├── Dialog_ColonistModification.cs       # 管理UI窗口
│   ├── ColonistModificationUtility.cs       # 工具类
│   └── ColonistModificationDialogUtility.cs # 打开窗口入口
├── Defs/ColonistModificationTemplateDef/
│   └── Templates.xml                       # 示例模板
└── 1.5/Languages/ChineseSimplified/Keyed/
    └── ColonistModification_Keys.xml        # 本地化
```

## 依赖

- Harmony (brrainz.harmony) — 当前版本未使用Harmony Patch，预留给未来可能需要的场景
- RimWorld 1.5
- 可选：Biotech DLC（异种基因模板需要）

## 扩展点

- 可通过XML添加更多模板（无需改代码）
- 模板可引用任何已加载的RecipeDef
- 未来可扩展：模板编辑器（游戏内新建/修改模板）
- 未来可扩展：Harmony Patch 拦截手术失败事件（替代当前的hediff检测方式）

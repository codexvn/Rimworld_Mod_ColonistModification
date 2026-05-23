# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

ColonistModification（制式改造）是一个 RimWorld mod，为游戏后期提供殖民者批量标准化改造功能——统一的基因植入或统一的仿生/植入物手术。

## Reference: Assembly-CSharp (Decompiled RimWorld Source)

The decompiled RimWorld C# source is at `F:\RiderProjects\Assembly-CSharp`. It contains ~9,200 source files across `Verse` engine layer, `RimWorld` game content layer, and third-party libraries. Target: .NET Framework 4.7.2, C# 8, AnyCPU, Class Library. See `F:\RiderProjects\Assembly-CSharp\CLAUDE.md` for full architecture notes.

Key RimWorld APIs used by this mod:
- **DefDatabase<T>** — all game definitions (ThingDef, RecipeDef, HediffDef, etc.)
- **GameComponent** — auto-registered persistent component, used for the manager tick loop
- **Bill_Medical** — base class for medical/surgery bills on pawns
- **BillStack** — per-pawn bill queue
- **IExposable** — Scribe-based save/load system
- **Window / WindowStack** — UI system
- **MainButtonWorker** — bottom bar button registration
- **Mod / ModSettings** — mod entry point and cross-save settings persistence

## Build

This project references RimWorld DLLs from a local Steam installation. Build via Rider or MSBuild targeting .NET Framework 4.7.2.

## Current Architecture

The mod implements both core features the user wants:

1. **Template-driven conditional prompting**: Templates define a set of surgeries, conditions (wealth, body type, pawn type), and whether to auto-execute or prompt the player for confirmation.
2. **Auto-retry on failure**: `Bill_ColonistModification` detects surgery failure and re-adds bills up to a configurable max retry count.

### Key files

| File | Purpose |
|------|---------|
| `ColonistModificationMod.cs` | Mod entry, settings host |
| `ColonistModificationSettings.cs` | Cross-save template storage |
| `UserTemplate.cs` | Template data model (recipes, conditions, retry config) |
| `ColonistModificationManager.cs` | `GameComponent` tick loop, state machine, pawn-template assignment |
| `Bill_ColonistModification.cs` | Custom `Bill_Medical` with success/failure hooks, auto-retry, step progression |
| `ColonistModificationUtility.cs` | Surgery validation, implant recipe discovery, surgeon/medicine checks |
| `Dialog_ColonistModification.cs` | Main management UI (4-tab window) |
| `MainButtonWorker_ColonistModification.cs` | Bottom bar button |
| `ColonistModificationDialogUtility.cs` | Dialog opener helper |

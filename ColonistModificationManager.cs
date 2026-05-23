using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ColonistModification
{
    public class PawnModificationRecord : IExposable
    {
        public string templateId;
        public ModificationStatus status = ModificationStatus.Idle;
        public int delayedUntilTick = 0;

        public string conditionFailReason;
        public Dictionary<string, string> recipeStatus = new Dictionary<string, string>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref templateId, "templateId");

            string statusStr = Scribe.mode == LoadSaveMode.Saving
                ? (status == ModificationStatus.Delayed || status == ModificationStatus.Dismissed ? status.ToString() : ModificationStatus.Idle.ToString())
                : null;
            Scribe_Values.Look(ref statusStr, "status", "Idle");
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (statusStr == "Delayed" || statusStr == "Dismissed")
                    status = statusStr == "Delayed" ? ModificationStatus.Delayed : ModificationStatus.Dismissed;
                else
                    status = ModificationStatus.Idle;
            }

            Scribe_Values.Look(ref delayedUntilTick, "delayedUntilTick", 0);
        }

    }

    public class SurgeryLogEntry : IExposable
    {
        public string pawnName;
        public string recipeLabel;
        public string templateName;
        public int tickCreated;
        public bool isCheckEntry;
        public bool checkPassed;
        public string checkResult;

        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnName, "pawnName");
            Scribe_Values.Look(ref recipeLabel, "recipeLabel");
            Scribe_Values.Look(ref templateName, "templateName");
            Scribe_Values.Look(ref tickCreated, "tickCreated", 0);
            Scribe_Values.Look(ref isCheckEntry, "isCheckEntry", false);
            Scribe_Values.Look(ref checkPassed, "checkPassed", false);
            Scribe_Values.Look(ref checkResult, "checkResult");
        }
    }

    public enum ModificationStatus
    {
        Idle,
        PendingConfirmation,
        Completed,
        Dismissed,
        Delayed
    }

    public class ColonistModificationManager : GameComponent
    {
        public static ColonistModificationManager Instance;

        private Dictionary<int, List<PawnModificationRecord>> pawnRecords = new Dictionary<int, List<PawnModificationRecord>>();
        private Dictionary<int, string> assignedTemplateIds = new Dictionary<int, string>();
        private HashSet<string> disabledTemplates = new HashSet<string>();
        private HashSet<int> globallyIgnoredPawns = new HashSet<int>();
        private List<SurgeryLogEntry> surgeryLog = new List<SurgeryLogEntry>();
        private const int MaxLogEntries = 500;

        private int lastCheckTick = 0;
        private const int CheckIntervalTicks = 250;
        private const int MaxConcurrentSurgeries = 1;

        private static readonly List<UserTemplate> EmptyTemplateList = new List<UserTemplate>();

        private List<UserTemplate> AllTemplates =>
            ColonistModificationMod.Instance?.settings?.templates ?? EmptyTemplateList;

        private HashSet<string> ValidTemplateIds =>
            new HashSet<string>(AllTemplates.Select(t => t.id));

        public ColonistModificationManager()
        {
            Instance = this;
        }

        public ColonistModificationManager(Game game)
        {
            Instance = this;
        }

        // ===== Template Assignment =====

        public void AssignTemplate(int pawnThingID, string templateId)
        {
            if (string.IsNullOrEmpty(templateId)) return;

            var tpl = GetTemplateById(templateId);
            var pawn = FindPawnByID(pawnThingID);
            if (tpl != null && pawn != null && !string.IsNullOrEmpty(tpl.targetBodyDefName)
                && pawn.RaceProps.body.defName != tpl.targetBodyDefName)
                return;

            assignedTemplateIds[pawnThingID] = templateId;
            if (pawnRecords.TryGetValue(pawnThingID, out var records))
                records.RemoveAll(r => r.templateId != templateId);
            if (tpl != null && pawn != null && tpl.StepCount > 0)
                GetOrCreateRecord(pawn, tpl);

            RefreshAllCaches();
        }

        public void UnassignTemplate(int pawnThingID)
        {
            assignedTemplateIds.Remove(pawnThingID);
            RefreshAllCaches();
        }

        public string GetAssignedTemplateId(int pawnThingID)
        {
            return assignedTemplateIds.TryGetValue(pawnThingID, out var id) ? id : null;
        }

        // ===== Tick =====

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            if (Find.CurrentMap == null) return;

            int currentTick = Find.TickManager.TicksGame;
            if (currentTick - lastCheckTick < CheckIntervalTicks) return;
            lastCheckTick = currentTick;

            CheckAllTemplates(currentTick);
        }

        /// <summary>
        /// tick 循环入口：刷新缓存 → 基于缓存处理动作（弹窗/加手术）。
        /// 每 tick 最多执行一个动作：确认窗一个，或加手术 MaxConcurrentSurgeries 台。
        /// </summary>
        private void CheckAllTemplates(int currentTick, bool actOnResults = true)
        {
            float colonyWealth = CalculateColonyWealth();
            if (colonyWealth < 0) return;

            // 统一刷新所有缓存
            RefreshAllCaches();

            if (!actOnResults) return;

            // 基于缓存处理动作
            var templates = AllTemplates.ToList();
            int activeSurgeries = 0;

            // 统计当前已有手术单（仅限分配了模版的殖民者）
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                foreach (Pawn pawn in map.mapPawns.FreeColonistsAndPrisoners)
                {
                    var assignedTpl = GetAssignedTemplateId(pawn.thingIDNumber) != null
                        ? templates.FirstOrDefault(t => t.id == GetAssignedTemplateId(pawn.thingIDNumber))
                        : null;
                    if (assignedTpl == null) continue;
                    var recipeDefs = new HashSet<RecipeDef>(assignedTpl.resolvedRecipes);
                    if (!string.IsNullOrEmpty(assignedTpl.xenogermTargetXenotypeDefName) && ModsConfig.BiotechActive)
                    {
                        var xenoRecipe = DefDatabase<RecipeDef>.GetNamedSilentFail("ImplantXenogerm");
                        if (xenoRecipe != null) recipeDefs.Add(xenoRecipe);
                    }
                    XenotypeDef targetXenoDef = null;
                    string targetXenoName = assignedTpl.xenogermTargetXenotypeDefName;
                    if (!string.IsNullOrEmpty(targetXenoName))
                        targetXenoDef = DefDatabase<XenotypeDef>.GetNamedSilentFail(targetXenoName);
                    for (int i = 0; i < pawn.BillStack.Count; i++)
                    {
                        if (pawn.BillStack[i] is Bill_Medical bm && recipeDefs.Contains(bm.recipe))
                        {
                            if (bm.recipe.defName == "ImplantXenogerm" && targetXenoDef != null)
                            {
                                if (bm.xenogerm != null && bm.xenogerm.xenotypeName == targetXenoDef.label)
                                    activeSurgeries++;
                            }
                            else
                            {
                                activeSurgeries++;
                            }
                        }
                    }
                }
            }

            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;

                foreach (Pawn pawn in map.mapPawns.FreeColonistsAndPrisoners.ToList())
                {
                    if (globallyIgnoredPawns.Contains(pawn.thingIDNumber)) continue;

                    var assignedId = GetAssignedTemplateId(pawn.thingIDNumber);
                    var template = assignedId != null ? templates.FirstOrDefault(t => t.id == assignedId) : null;
                    if (template == null) continue;
                    if (disabledTemplates.Contains(template.id)) continue;
                    if (template.StepCount == 0) continue;

                    if (!string.IsNullOrEmpty(template.targetBodyDefName)
                        && pawn.RaceProps.body.defName != template.targetBodyDefName)
                        continue;
                    if (template.minColonyWealth > 0 && colonyWealth < template.minColonyWealth) continue;
                    if (template.colonistsOnly && !pawn.IsFreeColonist) continue;
                    if (!template.includeSlaves && pawn.IsSlave) continue;

                    var record = GetOrCreateRecord(pawn, template);

                    var allItems = GetAllRecipeItems(pawn, template);
                    int total = allItems.Count;
                    int done = allItems.Count(item => IsRecipePartCompleted(pawn, item.recipe, item.part, template.xenogermTargetXenotypeDefName));
                    if (done >= total)
                    {
                        record.status = ModificationStatus.Completed;
                        continue;
                    }

                    if (record.status == ModificationStatus.Delayed)
                    {
                        if (currentTick >= record.delayedUntilTick)
                            record.status = ModificationStatus.Idle;
                        continue;
                    }

                    if (record.status == ModificationStatus.PendingConfirmation)
                        continue;

                    if (record.status == ModificationStatus.Dismissed)
                        continue;

                    var pendingRecipes = GetPendingRecipes(pawn, template, record);

                    foreach (var item in pendingRecipes)
                    {
                        if (HasModificationBillForRecipe(pawn, item.recipe, item.part)) continue;

                        record.recipeStatus.TryGetValue(item.Key, out string status);
                        bool can = status == null;

                        if (!can) continue;

                        if (template.requirePlayerConfirmation)
                        {
                            if (activeSurgeries >= MaxConcurrentSurgeries) return;
                            record.status = ModificationStatus.PendingConfirmation;
                            var capturedPawn = pawn;
                            var capturedTemplate = template;
                            var capturedItem = item;
                            var box = new Dialog_MessageBox(
                                $"殖民者 {pawn.LabelShort} 的改造方案「{template.name}」条件已满足。\n\n下一步手术: {capturedItem.Label}",
                                "开始改造", () =>
                                {
                                    record.status = ModificationStatus.Idle;
                                    CreateAndAddBill(capturedPawn, capturedTemplate, capturedItem);
                                    RefreshAllCaches();
                                },
                                "稍后提醒", () => DelayTemplateForPawn(capturedPawn, capturedTemplate),
                                "制式改造确认", false, null, null);
                            box.buttonCText = "忽略";
                            box.buttonCAction = () => DismissTemplateForPawn(capturedPawn, capturedTemplate);
                            Find.WindowStack.Add(box);
                            return;
                        }

                        if (activeSurgeries >= MaxConcurrentSurgeries) return;
                        CreateAndAddBill(pawn, template, item);
                        activeSurgeries++;
                    }

                }
            }
        }

        /// <summary>recipe+部位展开后的单条待处理项</summary>
    public class PendingRecipeItem
        {
            public RecipeDef recipe;
            public BodyPartRecord part;
            public string Label => part != null ? $"{recipe.label} ({part.LabelCap})" : recipe.label;
            public string Key => part != null ? $"{recipe.defName}|{part.LabelCap}" : recipe.defName;
        }

        /// <summary>将模板的 recipe 按殖民者可用部位展开，排除已完成项，返回未完成的 (recipe+部位) 列表</summary>
        private List<PendingRecipeItem> GetPendingRecipes(Pawn pawn, UserTemplate template, PawnModificationRecord record)
        {
            var result = new List<PendingRecipeItem>();
            var recipes = new List<RecipeDef>(template.resolvedRecipes);
            // 基因植入
            if (!string.IsNullOrEmpty(template.xenogermTargetXenotypeDefName) && ModsConfig.BiotechActive)
            {
                var xenoRecipe = DefDatabase<RecipeDef>.GetNamedSilentFail("ImplantXenogerm");
                if (xenoRecipe != null) recipes.Add(xenoRecipe);
            }
            foreach (var recipe in recipes)
            {
                if (recipe.targetsBodyPart)
                {
                    var parts = recipe.Worker.GetPartsToApplyOn(pawn, recipe);
                    if (parts != null)
                    {
                        foreach (var part in parts)
                        {
                            if (IsRecipePartCompleted(pawn, recipe, part, template.xenogermTargetXenotypeDefName)) continue;
                            result.Add(new PendingRecipeItem { recipe = recipe, part = part });
                        }
                    }
                }
                else
                {
                    if (IsRecipePartCompleted(pawn, recipe, null, template.xenogermTargetXenotypeDefName)) continue;
                    result.Add(new PendingRecipeItem { recipe = recipe, part = null });
                }
            }
            return result;
        }

        // ===== Records =====

        private PawnModificationRecord GetOrCreateRecord(Pawn pawn, UserTemplate template)
        {
            if (!pawnRecords.TryGetValue(pawn.thingIDNumber, out var records))
            {
                records = new List<PawnModificationRecord>();
                pawnRecords[pawn.thingIDNumber] = records;
            }

            var record = records.FirstOrDefault(r => r.templateId == template.id);
            if (record == null)
            {
                record = new PawnModificationRecord { templateId = template.id };
                records.Add(record);
            }

            return record;
        }

        public PawnModificationRecord GetRecord(Pawn pawn, UserTemplate template)
        {
            if (!pawnRecords.TryGetValue(pawn.thingIDNumber, out var records)) return null;
            return records.FirstOrDefault(r => r.templateId == template.id);
        }

        // ===== Surgery =====

        public void AddSurgeryForRecipe(Pawn pawn, UserTemplate template, RecipeDef recipe, BodyPartRecord part = null)
        {
            if (IsRecipePartCompleted(pawn, recipe, part, template.xenogermTargetXenotypeDefName)) return;
            CreateAndAddBill(pawn, template, new PendingRecipeItem { recipe = recipe, part = part });
            RefreshAllCaches();
        }

        /// <summary>创建原版 Bill_Medical 并加入 BillStack</summary>
        private void CreateAndAddBill(Pawn pawn, UserTemplate template, PendingRecipeItem item)
        {
            var bill = new Bill_Medical(item.recipe, null);
            pawn.BillStack.AddBill(bill);
            if (item.part != null) bill.Part = item.part;
            if (item.recipe.defName == "ImplantXenogerm" && ModsConfig.BiotechActive)
            {
                var xenogerm = FindXenogerm(pawn.Map, template.xenogermTargetXenotypeDefName);
                if (xenogerm != null) bill.xenogerm = xenogerm;
            }

            AddLog(new SurgeryLogEntry
            {
                pawnName = pawn.LabelShort,
                recipeLabel = item.Label,
                templateName = template.name,
                tickCreated = Find.TickManager.TicksGame,
                isCheckEntry = false,
                checkPassed = true,
                checkResult = "已添加手术"
            });

            Messages.Message(
                $"开始对殖民者 {pawn.LabelShort} 执行制式改造 '{template.name}' 中的 {item.Label}。",
                new LookTargets(pawn), MessageTypeDefOf.NeutralEvent, false);
        }

        // ===== Delayed / Dismissed =====

        public void DelayTemplateForPawn(Pawn pawn, UserTemplate template)
        {
            var record = GetOrCreateRecord(pawn, template);
            record.status = ModificationStatus.Delayed;
            record.delayedUntilTick = Find.TickManager.TicksGame + (template.delayDays * 60000);
            Messages.Message(
                $"已暂缓殖民者 {pawn.LabelShort} 的 '{template.name}' 改造。",
                new LookTargets(pawn), MessageTypeDefOf.NeutralEvent, false);
        }

        public void DismissTemplateForPawn(Pawn pawn, UserTemplate template)
        {
            var record = GetOrCreateRecord(pawn, template);
            record.status = ModificationStatus.Dismissed;
            Messages.Message(
                $"已忽略殖民者 {pawn.LabelShort} 的 '{template.name}' 改造。",
                new LookTargets(pawn), MessageTypeDefOf.NeutralEvent, false);
        }

        // ===== Queries =====

        public UserTemplate GetTemplateById(string id)
        {
            return AllTemplates.FirstOrDefault(t => t.id == id);
        }

        private void AddLog(SurgeryLogEntry entry)
        {
            surgeryLog.Add(entry);
            while (surgeryLog.Count > MaxLogEntries)
                surgeryLog.RemoveAt(0);
        }

        public List<SurgeryLogEntry> GetSurgeryLog()
        {
            return surgeryLog;
        }

        public void ClearSurgeryLog()
        {
            surgeryLog.Clear();
        }

        public List<PendingRecipeItem> GetPendingRecipeItems(Pawn pawn, UserTemplate template)
        {
            var record = GetOrCreateRecord(pawn, template);
            return GetPendingRecipes(pawn, template, record);
        }

        /// <summary>返回全部 recipe+部位组合（含已完成），用于已完成 tab 显示全量清单</summary>
        /// <summary>返回全部 recipe+部位组合（含已完成），从身体定义展开而非 GetPartsToApplyOn（后者排除已有植入物的部位）</summary>
        public List<PendingRecipeItem> GetAllRecipeItems(Pawn pawn, UserTemplate template)
        {
            var body = pawn.RaceProps.body;
            var result = new List<PendingRecipeItem>();
            var recipes = new List<RecipeDef>(template.resolvedRecipes);
            if (!string.IsNullOrEmpty(template.xenogermTargetXenotypeDefName) && ModsConfig.BiotechActive)
            {
                var xenoRecipe = DefDatabase<RecipeDef>.GetNamedSilentFail("ImplantXenogerm");
                if (xenoRecipe != null) recipes.Add(xenoRecipe);
            }
            foreach (var recipe in recipes)
            {
                if (recipe.targetsBodyPart)
                {
                    var parts = new List<BodyPartRecord>();
                    if (recipe.appliedOnFixedBodyParts != null)
                    {
                        foreach (var partDef in recipe.appliedOnFixedBodyParts)
                            parts.AddRange(body.GetPartsWithDef(partDef));
                    }
                    if (recipe.appliedOnFixedBodyPartGroups != null)
                    {
                        foreach (var group in recipe.appliedOnFixedBodyPartGroups)
                        {
                            foreach (var part in body.AllParts)
                            {
                                if (part.groups != null && part.groups.Contains(group) && !parts.Contains(part))
                                    parts.Add(part);
                            }
                        }
                    }
                    foreach (var part in parts)
                        result.Add(new PendingRecipeItem { recipe = recipe, part = part });
                }
                else
                {
                    result.Add(new PendingRecipeItem { recipe = recipe, part = null });
                }
            }
            return result;
        }

        private Xenogerm FindXenogerm(Map map, string targetXenotypeDefName)
        {
            if (map == null) return null;
            XenotypeDef targetXeno = null;
            if (!string.IsNullOrEmpty(targetXenotypeDefName))
                targetXeno = DefDatabase<XenotypeDef>.GetNamedSilentFail(targetXenotypeDefName);
            foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.Xenogerm))
            {
                Xenogerm xenogerm = thing as Xenogerm;
                if (xenogerm == null || xenogerm.IsForbidden(Faction.OfPlayer) || xenogerm.Position.Fogged(map))
                    continue;
                if (targetXeno != null && xenogerm.xenotypeName != targetXeno.label)
                    continue;
                return xenogerm;
            }
            return null;
        }

        public bool IsRecipePartCompleted(Pawn pawn, RecipeDef recipe, BodyPartRecord part, string xenogermTargetDefName = null)
        {
            // 基因植入：检查 colonist 是否已有目标异种
            if (recipe.defName == "ImplantXenogerm")
            {
                if (string.IsNullOrEmpty(xenogermTargetDefName)) return false;
                if (pawn.genes == null) return false;
                var targetXeno = DefDatabase<XenotypeDef>.GetNamedSilentFail(xenogermTargetDefName);
                if (targetXeno == null) return false;
                return pawn.genes.XenotypeLabel == targetXeno.LabelCap;
            }

            if (recipe.addsHediff == null) return false;
            if (part != null)
                return pawn.health.hediffSet.hediffs.Any(h =>
                    h.def == recipe.addsHediff && h.Part != null && h.Part.def == part.def && h.Part.Label == part.Label);
            return pawn.health.hediffSet.HasHediff(recipe.addsHediff);
        }

        public bool HasModificationBillForRecipe(Pawn pawn, RecipeDef recipe, BodyPartRecord part = null)
        {
            foreach (Bill bill in pawn.BillStack)
                if (bill is Bill_Medical medBill
                    && medBill.recipe == recipe
                    && (part == null || (medBill.Part != null && medBill.Part.def == part.def && medBill.Part.Label == part.Label)))
                    return true;
            return false;
        }

        // ===== Misc =====

        public void DisableTemplate(string id) => disabledTemplates.Add(id);
        public void EnableTemplate(string id) => disabledTemplates.Remove(id);
        /// <summary>
        /// 统一入口：遍历所有殖民者的未完成 recipe，检测条件并写入 recipeStatus 缓存。
        /// 缓存值约定：null=条件通过，其他字符串=失败原因。已有手术单通过 HasModificationBillForRecipe 实时判断。
        /// </summary>
        private void RefreshAllCaches()
        {
            var mod = ColonistModificationMod.Instance;
            var settings = mod?.settings;
            if (settings != null)
            {
                bool needResolve = settings.templates.Any(t => t.resolvedRecipes.Count != t.recipeDefNames.Count);
                if (needResolve)
                    settings.ResolveAllReferences();
            }

            var templates = settings?.templates;
            int templateCount = templates?.Count ?? -1;

            var templateList = templates != null ? templates.ToList() : new List<UserTemplate>();
            int pawnsWithAssignment = 0;
            int pawnsProcessed = 0;
            int recipesChecked = 0;

            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                foreach (Pawn pawn in map.mapPawns.FreeColonistsAndPrisoners.ToList())
                {
                    var assignedId = GetAssignedTemplateId(pawn.thingIDNumber);
                    if (assignedId == null) continue;
                    pawnsWithAssignment++;

                    var template = templateList.FirstOrDefault(t => t.id == assignedId);
                    if (template == null || template.StepCount == 0) continue;
                    pawnsProcessed++;

                    var record = GetOrCreateRecord(pawn, template);
                    var pendingRecipes = GetPendingRecipes(pawn, template, record);
                    record.conditionFailReason = "";

                    foreach (var item in pendingRecipes)
                    {
                        recipesChecked++;
                        if (HasModificationBillForRecipe(pawn, item.recipe, item.part))
                            continue;
                        var (can, reason) = ColonistModificationUtility.CheckSurgeryConditions(
                            pawn, item.recipe, pawn.Map, template.minMedicineCategory, template.xenogermTargetXenotypeDefName);
                        record.recipeStatus[item.Key] = can ? null : (reason ?? "条件不满足");
                        if (!can && !string.IsNullOrEmpty(reason))
                            record.conditionFailReason += $"· {item.Label}: {reason}\n";

                        AddLog(new SurgeryLogEntry
                        {
                            pawnName = pawn.LabelShort,
                            recipeLabel = item.Label,
                            templateName = template.name,
                            tickCreated = Find.TickManager.TicksGame,
                            isCheckEntry = true,
                            checkPassed = can,
                            checkResult = can ? "条件满足" : (reason ?? "条件不满足")
                        });
                    }
                }
            }

        }

        public void ForceCheckNow()
        {
            RefreshAllCaches();
            lastCheckTick = Find.TickManager.TicksGame;
        }

        public void IgnorePawn(int id) => globallyIgnoredPawns.Add(id);
        public void UnignorePawn(int id) => globallyIgnoredPawns.Remove(id);

        private float CalculateColonyWealth()
        {
            float wealth = 0f;
            foreach (Map map in Find.Maps)
                if (map.IsPlayerHome) wealth += map.wealthWatcher.WealthTotal;
            return wealth;
        }

        private Pawn FindPawnByID(int thingID)
        {
            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.AllPawns)
                {
                    if (pawn.thingIDNumber == thingID)
                        return pawn;
                }
            }
            foreach (Caravan c in Find.WorldObjects.Caravans)
            {
                foreach (Pawn pawn in c.PawnsListForReading)
                {
                    if (pawn.thingIDNumber == thingID)
                        return pawn;
                }
            }
            return null;
        }

        // ===== Serialization =====

        public override void ExposeData()
        {
            base.ExposeData();

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                var validIds = new HashSet<int>();
                foreach (Map map in Find.Maps)
                    foreach (Pawn pawn in map.mapPawns.AllPawns)
                        if (!pawn.Dead && !pawn.Destroyed)
                            validIds.Add(pawn.thingIDNumber);
                foreach (Caravan c in Find.WorldObjects.Caravans)
                    foreach (Pawn pawn in c.PawnsListForReading)
                        if (!pawn.Dead && !pawn.Destroyed)
                            validIds.Add(pawn.thingIDNumber);

                var invalid = pawnRecords.Keys.Where(id => !validIds.Contains(id)).ToList();
                foreach (var id in invalid) { pawnRecords.Remove(id); assignedTemplateIds.Remove(id); }
            }

            var pawnIDs = new List<int>();
            var recordsList = new List<List<PawnModificationRecord>>();
            if (Scribe.mode == LoadSaveMode.Saving)
                foreach (var kvp in pawnRecords) { pawnIDs.Add(kvp.Key); recordsList.Add(kvp.Value); }
            Scribe_Collections.Look(ref pawnIDs, "pawnIDs", LookMode.Value);
            Scribe_Collections.Look(ref recordsList, "recordsList", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars && pawnIDs != null && recordsList != null)
            {
                pawnRecords = new Dictionary<int, List<PawnModificationRecord>>();
                for (int i = 0; i < pawnIDs.Count && i < recordsList.Count; i++)
                    pawnRecords[pawnIDs[i]] = recordsList[i];
            }
            if (pawnRecords == null) pawnRecords = new Dictionary<int, List<PawnModificationRecord>>();

            var assignKeys = new List<int>();
            var assignValues = new List<string>();
            if (Scribe.mode == LoadSaveMode.Saving)
                foreach (var kvp in assignedTemplateIds) { assignKeys.Add(kvp.Key); assignValues.Add(kvp.Value); }
            Scribe_Collections.Look(ref assignKeys, "assignKeys", LookMode.Value);
            Scribe_Collections.Look(ref assignValues, "assignValues", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars && assignKeys != null && assignValues != null)
            {
                assignedTemplateIds = new Dictionary<int, string>();
                for (int i = 0; i < assignKeys.Count && i < assignValues.Count; i++)
                    assignedTemplateIds[assignKeys[i]] = assignValues[i];
            }
            if (assignedTemplateIds == null) assignedTemplateIds = new Dictionary<int, string>();
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                var validTplIds = ValidTemplateIds;
                var invalid = assignedTemplateIds.Where(kvp => !validTplIds.Contains(kvp.Value)).Select(kvp => kvp.Key).ToList();
                foreach (var pawnId in invalid)
                {
                    assignedTemplateIds.Remove(pawnId);
                    pawnRecords.Remove(pawnId);
                }
            }

            Scribe_Collections.Look(ref disabledTemplates, "disabledTemplates", LookMode.Value);
            if (disabledTemplates == null) disabledTemplates = new HashSet<string>();
            Scribe_Collections.Look(ref globallyIgnoredPawns, "globallyIgnoredPawns", LookMode.Value);
            if (globallyIgnoredPawns == null) globallyIgnoredPawns = new HashSet<int>();
        }
    }
}

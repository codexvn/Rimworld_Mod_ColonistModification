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
        public HashSet<string> completedRecipeDefNames = new HashSet<string>();
        public int delayedUntilTick = 0;
        public int failedStepIndex = -1;

        public string conditionFailReason;
        public Dictionary<string, string> recipeStatus = new Dictionary<string, string>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref templateId, "templateId");
            Scribe_Values.Look(ref status, "status");
            Scribe_Collections.Look(ref completedRecipeDefNames, "completedRecipeDefNames", LookMode.Value);
            if (completedRecipeDefNames == null) completedRecipeDefNames = new HashSet<string>();
            Scribe_Values.Look(ref delayedUntilTick, "delayedUntilTick", 0);
            Scribe_Values.Look(ref failedStepIndex, "failedStepIndex", -1);
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
            pawnRecords.Remove(pawnThingID);
            RefreshAllCaches();
        }

        public string GetAssignedTemplateId(int pawnThingID)
        {
            return assignedTemplateIds.TryGetValue(pawnThingID, out var id) ? id : null;
        }

        public UserTemplate GetAssignedTemplate(Pawn pawn)
        {
            var id = GetAssignedTemplateId(pawn.thingIDNumber);
            return id != null ? AllTemplates.FirstOrDefault(t => t.id == id) : null;
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

        private void CheckAllTemplates(int currentTick, bool actOnResults = true)
        {
            float colonyWealth = CalculateColonyWealth();
            if (colonyWealth < 0) return;

            // 统一刷新所有缓存
            RefreshAllCaches();

            if (!actOnResults) return;

            // 基于缓存处理动作
            var templates = AllTemplates.ToList();
            bool confirmationShownThisTick = false;

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

                    if (record.completedRecipeDefNames.Count >= template.StepCount)
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

                    foreach (var recipe in pendingRecipes)
                    {
                        if (HasModificationBillForRecipe(pawn, template, recipe))
                            continue;

                        record.recipeStatus.TryGetValue(recipe.defName, out string status);
                        bool can = status == null;

                        AddLog(new SurgeryLogEntry
                        {
                            pawnName = pawn.LabelShort,
                            recipeLabel = recipe.label,
                            templateName = template.name,
                            tickCreated = currentTick,
                            isCheckEntry = true,
                            checkPassed = can,
                            checkResult = can ? "条件满足" : (status ?? "条件不满足")
                        });

                        if (!can) continue;

                        if (template.requirePlayerConfirmation)
                        {
                            if (confirmationShownThisTick) return;
                            confirmationShownThisTick = true;
                            record.status = ModificationStatus.PendingConfirmation;
                            var capturedPawn = pawn;
                            var capturedTemplate = template;
                            var capturedRecipe = recipe;
                            var box = new Dialog_MessageBox(
                                $"殖民者 {pawn.LabelShort} 的改造方案「{template.name}」条件已满足。\n\n下一步手术: {capturedRecipe.label}",
                                "开始改造", () => ConfirmTemplateForPawn(capturedPawn, capturedTemplate),
                                "稍后提醒", () => DelayTemplateForPawn(capturedPawn, capturedTemplate),
                                "制式改造确认", false, null, null);
                            box.buttonCText = "忽略";
                            box.buttonCAction = () => DismissTemplateForPawn(capturedPawn, capturedTemplate);
                            Find.WindowStack.Add(box);
                            return;
                        }

                        DoStartSurgery(pawn, template, record, recipe);
                    }

                    if (confirmationShownThisTick) return;
                }
            }
        }

        private List<RecipeDef> GetPendingRecipes(Pawn pawn, UserTemplate template, PawnModificationRecord record)
        {
            var result = new List<RecipeDef>();
            foreach (var recipe in template.resolvedRecipes)
            {
                if (record.completedRecipeDefNames.Contains(recipe.defName)) continue;
                result.Add(recipe);
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

        public void StartSurgeryForPawn(Pawn pawn, UserTemplate template, PawnModificationRecord record = null)
        {
            if (record == null) record = GetOrCreateRecord(pawn, template);

            var pending = GetPendingRecipes(pawn, template, record);
            RecipeDef bestRecipe = null;
            foreach (var recipe in pending)
            {
                if (ColonistModificationUtility.CheckSurgeryConditions(pawn, recipe, pawn.Map, template.minMedicineCategory).can)
                {
                    bestRecipe = recipe;
                    break;
                }
            }

            if (bestRecipe == null)
            {
                record.status = ModificationStatus.PendingConfirmation;
                return;
            }

            int idx = template.resolvedRecipes.IndexOf(bestRecipe);
            var bill = ColonistModificationUtility.CreateBillForStep(bestRecipe, pawn, template, idx);
            pawn.BillStack.AddBill(bill);

            Messages.Message(
                $"开始对殖民者 {pawn.LabelShort} 执行制式改造 '{template.name}' 中的 {bestRecipe.label}。",
                new LookTargets(pawn), MessageTypeDefOf.NeutralEvent, false);
        }

        public void AddSurgeryForRecipe(Pawn pawn, UserTemplate template, RecipeDef recipe)
        {
            var record = GetOrCreateRecord(pawn, template);
            if (record.completedRecipeDefNames.Contains(recipe.defName)) return;
            DoStartSurgery(pawn, template, record, recipe);
            RefreshAllCaches();
        }

        public void ConfirmTemplateForPawn(Pawn pawn, UserTemplate template)
        {
            var record = GetOrCreateRecord(pawn, template);
            record.status = ModificationStatus.Idle;
            StartSurgeryForPawn(pawn, template, record);
            RefreshAllCaches();
        }

        private void DoStartSurgery(Pawn pawn, UserTemplate template, PawnModificationRecord record,
            RecipeDef recipe)
        {
            int idx = template.resolvedRecipes.IndexOf(recipe);
            var bill = ColonistModificationUtility.CreateBillForStep(recipe, pawn, template, idx);
            pawn.BillStack.AddBill(bill);

            AddLog(new SurgeryLogEntry
            {
                pawnName = pawn.LabelShort,
                recipeLabel = recipe.label,
                templateName = template.name,
                tickCreated = Find.TickManager.TicksGame,
                isCheckEntry = false,
                checkPassed = true,
                checkResult = "已添加手术"
            });

            Messages.Message(
                $"开始对殖民者 {pawn.LabelShort} 执行制式改造 '{template.name}' 中的 {recipe.label}。",
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

        // ===== Notifications =====

        public void NotifyStepCompleted(Pawn pawn, UserTemplate template, int stepIndex)
        {
            var record = GetOrCreateRecord(pawn, template);
            var recipe = template.GetStep(stepIndex);
            if (recipe != null)
                record.completedRecipeDefNames.Add(recipe.defName);

            if (record.completedRecipeDefNames.Count >= template.StepCount)
                record.status = ModificationStatus.Completed;
        }

        public void NotifyStepFailed(Pawn pawn, UserTemplate template, int stepIndex)
        {
            var record = GetOrCreateRecord(pawn, template);
            record.failedStepIndex = stepIndex;
            var recipe = template.GetStep(stepIndex);
            if (recipe != null)
                record.completedRecipeDefNames.Add(recipe.defName);

            if (record.completedRecipeDefNames.Count >= template.StepCount)
                record.status = ModificationStatus.Completed;
        }

        // ===== Queries =====

        public UserTemplate GetTemplateById(string id)
        {
            return AllTemplates.FirstOrDefault(t => t.id == id);
        }

        public List<(Pawn pawn, UserTemplate template, PawnModificationRecord record)> GetCompletedRecords()
        {
            var result = new List<(Pawn, UserTemplate, PawnModificationRecord)>();
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                foreach (Pawn pawn in map.mapPawns.FreeColonistsAndPrisoners)
                {
                    var templateId = GetAssignedTemplateId(pawn.thingIDNumber);
                    if (templateId == null) continue;
                    var template = GetTemplateById(templateId);
                    if (template == null) continue;
                    var record = GetRecord(pawn, template);
                    if (record != null && record.status == ModificationStatus.Completed)
                        result.Add((pawn, template, record));
                }
            }
            return result;
        }

        public List<(Pawn pawn, UserTemplate template)> GetPendingConfirmations()
        {
            var result = new List<(Pawn, UserTemplate)>();
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                foreach (Pawn pawn in map.mapPawns.FreeColonistsAndPrisoners)
                {
                    var templateId = GetAssignedTemplateId(pawn.thingIDNumber);
                    if (templateId == null) continue;
                    var template = GetTemplateById(templateId);
                    if (template == null) continue;
                    var record = GetRecord(pawn, template);
                    if (record != null &&
                        (record.status == ModificationStatus.Idle
                         || record.status == ModificationStatus.PendingConfirmation
                         || record.status == ModificationStatus.Delayed))
                        result.Add((pawn, template));
                }
            }
            return result;
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

        public List<PawnModificationRecord> GetAllRecordsForPawn(Pawn pawn)
        {
            if (!pawnRecords.TryGetValue(pawn.thingIDNumber, out var records))
                return new List<PawnModificationRecord>();
            return new List<PawnModificationRecord>(records);
        }

        private bool HasModificationBill(Pawn pawn, UserTemplate template)
        {
            foreach (Bill bill in pawn.BillStack)
                if (bill is Bill_ColonistModification modBill && modBill.templateId == template.id)
                    return true;
            return false;
        }

        public bool HasModificationBillForRecipe(Pawn pawn, UserTemplate template, RecipeDef recipe)
        {
            foreach (Bill bill in pawn.BillStack)
                if (bill is Bill_ColonistModification modBill
                    && modBill.templateId == template.id
                    && modBill.recipe == recipe)
                    return true;
            return false;
        }

        // ===== Misc =====

        public void DisableTemplate(string id) => disabledTemplates.Add(id);
        public void EnableTemplate(string id) => disabledTemplates.Remove(id);
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

                    foreach (var recipe in pendingRecipes)
                    {
                        recipesChecked++;
                        if (HasModificationBillForRecipe(pawn, template, recipe))
                        {
                            record.recipeStatus[recipe.defName] = null;
                            continue;
                        }
                        var (can, reason) = ColonistModificationUtility.CheckSurgeryConditions(
                            pawn, recipe, pawn.Map, template.minMedicineCategory);
                        record.recipeStatus[recipe.defName] = can ? null : (reason ?? "条件不满足");
                        if (!can && !string.IsNullOrEmpty(reason))
                            record.conditionFailReason += $"· {recipe.label}: {reason}\n";
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

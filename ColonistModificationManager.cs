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

        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnName, "pawnName");
            Scribe_Values.Look(ref recipeLabel, "recipeLabel");
            Scribe_Values.Look(ref templateName, "templateName");
            Scribe_Values.Look(ref tickCreated, "tickCreated", 0);
        }
    }

    public enum ModificationStatus
    {
        Idle,
        PendingConfirmation,
        InProgress,
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

        private int lastCheckTick = 0;
        private const int CheckIntervalTicks = 250;

        private static readonly List<UserTemplate> EmptyTemplateList = new List<UserTemplate>();

        private List<UserTemplate> AllTemplates =>
            ColonistModificationMod.Instance?.settings?.templates ?? EmptyTemplateList;

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
            if (!string.IsNullOrEmpty(templateId))
            {
                var tpl = GetTemplateById(templateId);
                var pawn = FindPawnByID(pawnThingID);
                if (tpl != null && pawn != null && !string.IsNullOrEmpty(tpl.targetBodyDefName)
                    && pawn.RaceProps.body.defName != tpl.targetBodyDefName)
                    return;
            }
            assignedTemplateIds[pawnThingID] = templateId;
            if (pawnRecords.TryGetValue(pawnThingID, out var records))
            {
                records.RemoveAll(r => r.templateId != templateId);
            }
        }

        public void UnassignTemplate(int pawnThingID)
        {
            assignedTemplateIds.Remove(pawnThingID);
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

        private void CheckAllTemplates(int currentTick)
        {
            float colonyWealth = CalculateColonyWealth();
            if (colonyWealth < 0) return;

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

                    if (!string.IsNullOrEmpty(template.targetBodyDefName))
                    {
                        if (pawn.RaceProps.body.defName != template.targetBodyDefName)
                            continue;
                    }
                    if (template.minColonyWealth > 0 && colonyWealth < template.minColonyWealth) continue;
                    if (template.StepCount == 0) continue;

                    if (template.colonistsOnly && !pawn.IsFreeColonist) continue;
                    if (!template.includeSlaves && pawn.IsSlave) continue;

                    var record = GetOrCreateRecord(pawn, template);

                    if (record.completedRecipeDefNames.Count >= template.StepCount)
                    {
                        record.status = ModificationStatus.Completed;
                        continue;
                    }

                    switch (record.status)
                    {
                        case ModificationStatus.Idle:
                        {
                            var pendingRecipes = GetPendingRecipes(pawn, template, record);
                            record.conditionFailReason = "";
                            RecipeDef firstReady = null;

                            foreach (var recipe in pendingRecipes)
                            {
                                var (can, reason) = ColonistModificationUtility.CheckSurgeryConditions(
                                    pawn, recipe, pawn.Map, template.minMedicineCategory);
                                if (can)
                                {
                                    firstReady = recipe;
                                    break;
                                }
                                if (!string.IsNullOrEmpty(reason))
                                    record.conditionFailReason += $"· {recipe.label}: {reason}\n";
                            }

                            if (firstReady != null)
                            {
                                if (template.requirePlayerConfirmation)
                                {
                                    if (confirmationShownThisTick) break;
                                    confirmationShownThisTick = true;
                                    record.status = ModificationStatus.PendingConfirmation;
                                    var capturedPawn = pawn;
                                    var capturedTemplate = template;
                                    var capturedRecipe = firstReady;
                                    var box = new Dialog_MessageBox(
                                        $"殖民者 {pawn.LabelShort} 的改造方案「{template.name}」条件已满足。\n\n下一步手术: {capturedRecipe.label}",
                                        "开始改造", () =>
                                        {
                                            ConfirmTemplateForPawn(capturedPawn, capturedTemplate);
                                        },
                                        "稍后提醒", () =>
                                        {
                                            DelayTemplateForPawn(capturedPawn, capturedTemplate);
                                        },
                                        "制式改造确认", false, null, null);
                                    box.buttonCText = "忽略";
                                    box.buttonCAction = () =>
                                    {
                                        DismissTemplateForPawn(capturedPawn, capturedTemplate);
                                    };
                                    Find.WindowStack.Add(box);
                                }
                                else
                                {
                                    DoStartSurgery(pawn, template, record, firstReady);
                                }
                            }
                            break;
                        }

                        case ModificationStatus.PendingConfirmation:
                            break;

                        case ModificationStatus.Delayed:
                            if (currentTick >= record.delayedUntilTick)
                                record.status = ModificationStatus.Idle;
                            break;

                        case ModificationStatus.InProgress:
                            if (!HasModificationBill(pawn, template))
                                record.status = ModificationStatus.Idle;
                            break;
                    }
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
            record.status = ModificationStatus.InProgress;

            Messages.Message(
                $"开始对殖民者 {pawn.LabelShort} 执行制式改造 '{template.name}' 中的 {bestRecipe.label}。",
                new LookTargets(pawn), MessageTypeDefOf.NeutralEvent, false);
        }

        public void AddSurgeryForRecipe(Pawn pawn, UserTemplate template, RecipeDef recipe)
        {
            var record = GetOrCreateRecord(pawn, template);
            if (record.completedRecipeDefNames.Contains(recipe.defName)) return;
            if (HasModificationBill(pawn, template)) return;
            DoStartSurgery(pawn, template, record, recipe);
        }

        public void ConfirmTemplateForPawn(Pawn pawn, UserTemplate template)
        {
            var record = GetOrCreateRecord(pawn, template);
            record.status = ModificationStatus.Idle;
            StartSurgeryForPawn(pawn, template, record);
        }

        private void DoStartSurgery(Pawn pawn, UserTemplate template, PawnModificationRecord record,
            RecipeDef recipe)
        {
            int idx = template.resolvedRecipes.IndexOf(recipe);
            var bill = ColonistModificationUtility.CreateBillForStep(recipe, pawn, template, idx);
            pawn.BillStack.AddBill(bill);
            record.status = ModificationStatus.InProgress;

            surgeryLog.Add(new SurgeryLogEntry
            {
                pawnName = pawn.LabelShort,
                recipeLabel = recipe.label,
                templateName = template.name,
                tickCreated = Find.TickManager.TicksGame
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
                    if (record != null &&
                        (record.status == ModificationStatus.InProgress
                         || record.status == ModificationStatus.Completed))
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

        public List<SurgeryLogEntry> GetSurgeryLog()
        {
            return surgeryLog;
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

        // ===== Misc =====

        public void DisableTemplate(string id) => disabledTemplates.Add(id);
        public void EnableTemplate(string id) => disabledTemplates.Remove(id);
        public void ForceCheckNow()
        {
            lastCheckTick = 0;
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

            Scribe_Collections.Look(ref disabledTemplates, "disabledTemplates", LookMode.Value);
            Scribe_Collections.Look(ref globallyIgnoredPawns, "globallyIgnoredPawns", LookMode.Value);
            Scribe_Collections.Look(ref surgeryLog, "surgeryLog", LookMode.Deep);
            if (surgeryLog == null) surgeryLog = new List<SurgeryLogEntry>();
        }
    }
}

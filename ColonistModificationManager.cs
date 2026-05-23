using System;
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
        public int lastCompletedStepIndex = -1;
        public int delayedUntilTick = 0;
        public int failedStepIndex = -1;
        public int currentRetryCount = 0;

        public void ExposeData()
        {
            Scribe_Values.Look(ref templateId, "templateId");
            Scribe_Values.Look(ref status, "status");
            Scribe_Values.Look(ref lastCompletedStepIndex, "lastCompletedStepIndex", -1);
            Scribe_Values.Look(ref delayedUntilTick, "delayedUntilTick", 0);
            Scribe_Values.Look(ref failedStepIndex, "failedStepIndex", -1);
            Scribe_Values.Look(ref currentRetryCount, "currentRetryCount", 0);
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

        private Dictionary<int, List<PawnModificationRecord>> pawnRecords =
            new Dictionary<int, List<PawnModificationRecord>>();

        private HashSet<string> disabledTemplates = new HashSet<string>();
        private HashSet<int> globallyIgnoredPawns = new HashSet<int>();

        private int lastCheckTick = 0;
        private const int CheckIntervalTicks = 250;
        private const int LetterCooldownTicks = 60000;
        private int lastLetterTick = -60000;

        /// <summary>从 ModSettings 获取所有模板</summary>
        private List<UserTemplate> AllTemplates =>
            ColonistModificationMod.Instance?.settings?.templates ?? new List<UserTemplate>();

        public ColonistModificationManager() : base()
        {
            Instance = this;
        }

        public ColonistModificationManager(Game game) : base()
        {
            Instance = this;
        }

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

            var templates = AllTemplates;
            bool anyPendingConfirmation = false;
            List<string> readyTemplateNames = new List<string>();

            foreach (var template in templates)
            {
                if (disabledTemplates.Contains(template.id)) continue;
                if (template.minColonyWealth > 0 && colonyWealth < template.minColonyWealth) continue;
                if (template.StepCount == 0) continue;

                foreach (Map map in Find.Maps)
                {
                    if (!map.IsPlayerHome) continue;
                    foreach (Pawn pawn in map.mapPawns.FreeColonistsAndPrisoners)
                    {
                        if (globallyIgnoredPawns.Contains(pawn.thingIDNumber)) continue;
                        if (!ColonistModificationUtility.PawnMatchesTemplate(pawn, template)) continue;

                        if (ColonistModificationUtility.HasCompletedTemplate(pawn, template))
                        {
                            UpdateRecordStatus(pawn, template, ModificationStatus.Completed);
                            continue;
                        }

                        var record = GetOrCreateRecord(pawn, template);
                        switch (record.status)
                        {
                            case ModificationStatus.Idle:
                            case ModificationStatus.PendingConfirmation:
                                int nextStepIdx = ColonistModificationUtility.GetNextStepIndex(pawn, template);
                                if (nextStepIdx >= 0)
                                {
                                    var nextRecipe = template.GetStep(nextStepIdx);
                                    if (nextRecipe != null && ColonistModificationUtility.CanPerformSurgery(pawn, nextRecipe, pawn.Map))
                                    {
                                        if (template.requirePlayerConfirmation)
                                        {
                                            if (record.status != ModificationStatus.PendingConfirmation)
                                            {
                                                record.status = ModificationStatus.PendingConfirmation;
                                                anyPendingConfirmation = true;
                                                readyTemplateNames.Add(template.name);
                                            }
                                        }
                                        else
                                        {
                                            StartSurgeryForPawn(pawn, template, record);
                                        }
                                    }
                                }
                                break;

                            case ModificationStatus.Delayed:
                                if (currentTick >= record.delayedUntilTick)
                                    record.status = ModificationStatus.Idle;
                                break;

                            case ModificationStatus.InProgress:
                                if (!HasModificationBill(pawn, template))
                                {
                                    int resumeStep = ColonistModificationUtility.GetNextStepIndex(pawn, template);
                                    if (resumeStep >= 0)
                                    {
                                        var resumeRecipe = template.GetStep(resumeStep);
                                        if (resumeRecipe != null && ColonistModificationUtility.CanPerformSurgery(pawn, resumeRecipe, pawn.Map))
                                            StartSurgeryForPawn(pawn, template, record);
                                        else
                                            record.status = ModificationStatus.PendingConfirmation;
                                    }
                                    else
                                    {
                                        record.status = ModificationStatus.Completed;
                                    }
                                }
                                break;
                        }
                    }
                }
            }

            if (anyPendingConfirmation && currentTick - lastLetterTick >= LetterCooldownTicks)
            {
                SendPendingConfirmationLetter(readyTemplateNames);
                lastLetterTick = currentTick;
            }
        }

        private PawnModificationRecord GetOrCreateRecord(Pawn pawn, UserTemplate template)
        {
            if (!pawnRecords.ContainsKey(pawn.thingIDNumber))
                pawnRecords[pawn.thingIDNumber] = new List<PawnModificationRecord>();

            var records = pawnRecords[pawn.thingIDNumber];
            var record = records.FirstOrDefault(r => r.templateId == template.id);
            if (record == null)
            {
                record = new PawnModificationRecord { templateId = template.id, status = ModificationStatus.Idle };
                records.Add(record);
            }

            if (record.status != ModificationStatus.Completed && record.status != ModificationStatus.Dismissed)
            {
                if (ColonistModificationUtility.HasCompletedTemplate(pawn, template))
                {
                    record.status = ModificationStatus.Completed;
                    record.lastCompletedStepIndex = template.StepCount - 1;
                }
            }
            return record;
        }

        private void UpdateRecordStatus(Pawn pawn, UserTemplate template, ModificationStatus newStatus)
        {
            var record = GetOrCreateRecord(pawn, template);
            if (record.status != newStatus) record.status = newStatus;
        }

        public void StartSurgeryForPawn(Pawn pawn, UserTemplate template, PawnModificationRecord record = null)
        {
            if (record == null) record = GetOrCreateRecord(pawn, template);
            int nextStep = ColonistModificationUtility.GetNextStepIndex(pawn, template);
            if (nextStep < 0)
            {
                record.status = ModificationStatus.Completed;
                record.lastCompletedStepIndex = template.StepCount - 1;
                return;
            }

            var recipe = template.GetStep(nextStep);
            if (recipe == null)
            {
                Log.Error($"ColonistModification: 模板 '{template.name}' 的步骤{nextStep}无效。");
                return;
            }

            var bill = ColonistModificationUtility.CreateBillForStep(recipe, pawn, template, nextStep);
            pawn.BillStack.AddBill(bill);
            record.status = ModificationStatus.InProgress;
            record.lastCompletedStepIndex = Math.Max(record.lastCompletedStepIndex, nextStep - 1);

            Messages.Message(
                $"开始对殖民者 {pawn.LabelShort} 执行制式改造 '{template.name}'，共 {template.StepCount} 个步骤。",
                new LookTargets(pawn), MessageTypeDefOf.NeutralEvent, false);
        }

        public void ConfirmTemplateForPawn(Pawn pawn, UserTemplate template)
        {
            var record = GetOrCreateRecord(pawn, template);
            record.status = ModificationStatus.Idle;
            StartSurgeryForPawn(pawn, template, record);
        }

        public void DelayTemplateForPawn(Pawn pawn, UserTemplate template)
        {
            var record = GetOrCreateRecord(pawn, template);
            record.status = ModificationStatus.Delayed;
            record.delayedUntilTick = Find.TickManager.TicksGame + (template.delayDays * 60000);

            Messages.Message(
                $"已暂缓殖民者 {pawn.LabelShort} 的 '{template.name}' 改造，将在 {template.delayDays} 天后重新提示。",
                new LookTargets(pawn), MessageTypeDefOf.NeutralEvent, false);
        }

        public void DismissTemplateForPawn(Pawn pawn, UserTemplate template)
        {
            var record = GetOrCreateRecord(pawn, template);
            record.status = ModificationStatus.Dismissed;

            Messages.Message(
                $"已忽略殖民者 {pawn.LabelShort} 的 '{template.name}' 改造，不再提示。",
                new LookTargets(pawn), MessageTypeDefOf.NeutralEvent, false);
        }

        public void DisableTemplate(string templateId) => disabledTemplates.Add(templateId);
        public void EnableTemplate(string templateId) => disabledTemplates.Remove(templateId);
        public void IgnorePawn(int pawnThingID) => globallyIgnoredPawns.Add(pawnThingID);
        public void UnignorePawn(int pawnThingID) => globallyIgnoredPawns.Remove(pawnThingID);

        public PawnModificationRecord GetRecord(Pawn pawn, UserTemplate template)
        {
            if (!pawnRecords.ContainsKey(pawn.thingIDNumber)) return null;
            return pawnRecords[pawn.thingIDNumber].FirstOrDefault(r => r.templateId == template.id);
        }

        public List<PawnModificationRecord> GetAllRecordsForPawn(Pawn pawn)
        {
            if (!pawnRecords.ContainsKey(pawn.thingIDNumber)) return new List<PawnModificationRecord>();
            return new List<PawnModificationRecord>(pawnRecords[pawn.thingIDNumber]);
        }

        public void NotifyStepCompleted(Pawn pawn, UserTemplate template, int stepIndex)
        {
            var record = GetOrCreateRecord(pawn, template);
            record.lastCompletedStepIndex = Math.Max(record.lastCompletedStepIndex, stepIndex);
            record.currentRetryCount = 0;

            if (ColonistModificationUtility.HasCompletedTemplate(pawn, template))
            {
                record.status = ModificationStatus.Completed;
                record.lastCompletedStepIndex = template.StepCount - 1;
            }
        }

        public void NotifyStepFailed(Pawn pawn, UserTemplate template, int stepIndex)
        {
            var record = GetOrCreateRecord(pawn, template);
            record.failedStepIndex = stepIndex;
            record.lastCompletedStepIndex = Math.Max(record.lastCompletedStepIndex, stepIndex);

            int nextStep = ColonistModificationUtility.GetNextStepIndex(pawn, template);
            if (nextStep < 0) record.status = ModificationStatus.Completed;
        }

        private bool HasModificationBill(Pawn pawn, UserTemplate template)
        {
            foreach (Bill bill in pawn.BillStack)
            {
                if (bill is Bill_ColonistModification modBill && modBill.template?.id == template.id)
                    return true;
            }
            return false;
        }

        private void SendPendingConfirmationLetter(List<string> templateNames)
        {
            if (templateNames.Count == 0) return;
            string templateList = string.Join("、", templateNames.Distinct().Take(3));
            Find.LetterStack.ReceiveLetter("殖民者制式改造已就绪",
                $"有改造模板的条件已满足，可以开始手术：\n\n{templateList}\n\n打开「殖民者改造管理」窗口查看详情并确认或延迟手术。",
                LetterDefOf.NeutralEvent);
        }

        private float CalculateColonyWealth()
        {
            float wealth = 0f;
            foreach (Map map in Find.Maps)
                if (map.IsPlayerHome) wealth += map.wealthWatcher.WealthTotal;
            return wealth;
        }

        public List<(Pawn pawn, UserTemplate template)> GetPendingConfirmations()
        {
            var result = new List<(Pawn, UserTemplate)>();
            foreach (var kvp in pawnRecords)
            {
                var pawn = FindPawnByID(kvp.Key);
                if (pawn == null) continue;

                foreach (var record in kvp.Value)
                {
                    if (record.status == ModificationStatus.PendingConfirmation)
                    {
                        var template = AllTemplates.FirstOrDefault(t => t.id == record.templateId);
                        if (template != null) result.Add((pawn, template));
                    }
                }
            }
            return result;
        }

        public UserTemplate GetTemplateById(string id)
        {
            return AllTemplates.FirstOrDefault(t => t.id == id);
        }

        private Pawn FindPawnByID(int thingID)
        {
            foreach (Map map in Find.Maps)
                foreach (Pawn pawn in map.mapPawns.AllPawns)
                    if (pawn.thingIDNumber == thingID) return pawn;

            foreach (Caravan caravan in Find.WorldObjects.Caravans)
                foreach (Pawn pawn in caravan.PawnsListForReading)
                    if (pawn.thingIDNumber == thingID) return pawn;

            return null;
        }

        private void CleanupInvalidRecords()
        {
            var invalidIDs = new List<int>();
            foreach (int pawnID in pawnRecords.Keys)
            {
                var pawn = FindPawnByID(pawnID);
                if (pawn == null || pawn.Dead || pawn.Destroyed) invalidIDs.Add(pawnID);
            }
            foreach (int id in invalidIDs) pawnRecords.Remove(id);
        }

        public override void ExposeData()
        {
            base.ExposeData();

            if (Scribe.mode == LoadSaveMode.Saving) CleanupInvalidRecords();

            List<int> pawnIDs = new List<int>();
            List<List<PawnModificationRecord>> recordsList = new List<List<PawnModificationRecord>>();

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                foreach (var kvp in pawnRecords)
                {
                    pawnIDs.Add(kvp.Key);
                    recordsList.Add(kvp.Value);
                }
            }

            Scribe_Collections.Look(ref pawnIDs, "pawnIDs", LookMode.Value);
            Scribe_Collections.Look(ref recordsList, "recordsList", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.LoadingVars && pawnIDs != null && recordsList != null)
            {
                pawnRecords = new Dictionary<int, List<PawnModificationRecord>>();
                for (int i = 0; i < pawnIDs.Count && i < recordsList.Count; i++)
                    pawnRecords[pawnIDs[i]] = recordsList[i];
            }

            Scribe_Collections.Look(ref disabledTemplates, "disabledTemplates", LookMode.Value);
            Scribe_Collections.Look(ref globallyIgnoredPawns, "globallyIgnoredPawns", LookMode.Value);
        }
    }
}

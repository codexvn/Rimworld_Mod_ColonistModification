using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ColonistModification
{
    /// <summary>
    /// 单个殖民者对于某个模板的改造状态记录
    /// </summary>
    public class PawnModificationRecord : IExposable
    {
        /// <summary>模板的defName</summary>
        public string templateDefName;

        /// <summary>当前改造状态</summary>
        public ModificationStatus status = ModificationStatus.Idle;

        /// <summary>最后完成的步骤索引（-1表示尚未开始）</summary>
        public int lastCompletedStepIndex = -1;

        /// <summary>延迟到哪个tick再提示（玩家选择"稍后"时设置）</summary>
        public int delayedUntilTick = 0;

        /// <summary>失败超过最大重试次数的步骤索引（-1表示无）</summary>
        public int failedStepIndex = -1;

        /// <summary>当前步骤已重试次数</summary>
        public int currentRetryCount = 0;

        public void ExposeData()
        {
            Scribe_Values.Look(ref templateDefName, "templateDefName");
            Scribe_Values.Look(ref status, "status");
            Scribe_Values.Look(ref lastCompletedStepIndex, "lastCompletedStepIndex", -1);
            Scribe_Values.Look(ref delayedUntilTick, "delayedUntilTick", 0);
            Scribe_Values.Look(ref failedStepIndex, "failedStepIndex", -1);
            Scribe_Values.Look(ref currentRetryCount, "currentRetryCount", 0);
        }
    }

    /// <summary>
    /// 改造状态枚举
    /// </summary>
    public enum ModificationStatus
    {
        /// <summary>空闲，尚未开始评估</summary>
        Idle,

        /// <summary>等待玩家确认是否开始</summary>
        PendingConfirmation,

        /// <summary>正在进行手术中</summary>
        InProgress,

        /// <summary>所有步骤已完成</summary>
        Completed,

        /// <summary>玩家选择了忽略（不再提示）</summary>
        Dismissed,

        /// <summary>玩家选择了延迟（到达指定时间后再提示）</summary>
        Delayed
    }

    /// <summary>
    /// 殖民者制式改造核心管理器
    ///
    /// 作为GameComponent自动注册到游戏中，负责：
    /// 1. 周期性检查所有模板和殖民者的匹配条件
    /// 2. 管理每个殖民者对每个模板的改造状态
    /// 3. 当条件满足时通知玩家（信件或消息）
    /// 4. 处理玩家的确认/延迟/忽略决定
    /// 5. 触发手术Bill的创建（通过ColonistModificationUtility）
    /// </summary>
    public class ColonistModificationManager : GameComponent
    {
        /// <summary>全局单例引用</summary>
        public static ColonistModificationManager Instance;

        /// <summary>
        /// 核心数据：pawnThingID -> 该pawn的所有模板记录列表
        /// </summary>
        private Dictionary<int, List<PawnModificationRecord>> pawnRecords =
            new Dictionary<int, List<PawnModificationRecord>>();

        /// <summary>
        /// 全局启用的模板defName集合（默认所有模板都启用）
        /// </summary>
        private HashSet<string> disabledTemplates = new HashSet<string>();

        /// <summary>
        /// 全局忽略的pawn thingID集合（玩家手动排除的殖民者）
        /// </summary>
        private HashSet<int> globallyIgnoredPawns = new HashSet<int>();

        /// <summary>
        /// 上次条件检查的tick计数，用于周期性检查
        /// </summary>
        private int lastCheckTick = 0;

        /// <summary>
        /// 条件检查间隔（tick数），约250 tick检查一次（正常速度下约4秒）
        /// </summary>
        private const int CheckIntervalTicks = 250;

        /// <summary>
        /// 向玩家发送信件的间隔（tick数），避免频繁骚扰
        /// </summary>
        private const int LetterCooldownTicks = 60000; // 约1游戏天

        /// <summary>
        /// 上次发送信件的时间
        /// </summary>
        private int lastLetterTick = -60000;

        public ColonistModificationManager() : base()
        {
            Instance = this;
        }

        public ColonistModificationManager(Game game) : base()
        {
            Instance = this;
        }

        /// <summary>
        /// 每个游戏tick调用一次，进行周期性条件检查
        /// </summary>
        public override void GameComponentTick()
        {
            base.GameComponentTick();

            // 只在主地图存在时检查
            if (Find.CurrentMap == null)
                return;

            // 限制检查频率
            int currentTick = Find.TickManager.TicksGame;
            if (currentTick - lastCheckTick < CheckIntervalTicks)
                return;
            lastCheckTick = currentTick;

            // 执行条件检查
            CheckAllTemplates(currentTick);
        }

        /// <summary>
        /// 遍历所有模板和殖民者，检查条件并触发相应操作
        /// </summary>
        private void CheckAllTemplates(int currentTick)
        {
            // 检查殖民地财富条件
            float colonyWealth = CalculateColonyWealth();
            if (colonyWealth < 0)
                return; // 无法计算财富，跳过

            IEnumerable<ColonistModificationTemplateDef> allTemplates =
                DefDatabase<ColonistModificationTemplateDef>.AllDefs;

            bool anyPendingConfirmation = false;
            List<string> readyTemplateNames = new List<string>();

            foreach (ColonistModificationTemplateDef template in allTemplates)
            {
                // 跳过被禁用的模板
                if (disabledTemplates.Contains(template.defName))
                    continue;

                // 检查殖民地财富阈值
                if (template.minColonyWealth > 0 && colonyWealth < template.minColonyWealth)
                    continue;

                // 跳过没有有效步骤的模板
                if (template.StepCount == 0)
                    continue;

                // 遍历所有可用的殖民者地图
                foreach (Map map in Find.Maps)
                {
                    if (!map.IsPlayerHome)
                        continue;

                    foreach (Pawn pawn in map.mapPawns.FreeColonistsAndPrisoners)
                    {
                        // 跳过全局忽略的pawn
                        if (globallyIgnoredPawns.Contains(pawn.thingIDNumber))
                            continue;

                        // 检查pawn是否匹配模板过滤器
                        if (!ColonistModificationUtility.PawnMatchesTemplate(pawn, template))
                            continue;

                        // 检查是否已完成所有步骤
                        if (ColonistModificationUtility.HasCompletedTemplate(pawn, template))
                        {
                            UpdateRecordStatus(pawn, template, ModificationStatus.Completed);
                            continue;
                        }

                        // 获取或创建记录
                        PawnModificationRecord record = GetOrCreateRecord(pawn, template);

                        // 根据当前状态处理
                        switch (record.status)
                        {
                            case ModificationStatus.Idle:
                            case ModificationStatus.PendingConfirmation:
                                // 检查手术条件是否满足
                                int nextStepIdx = ColonistModificationUtility.GetNextStepIndex(pawn, template);
                                if (nextStepIdx >= 0)
                                {
                                    RecipeDef nextRecipe = template.GetStep(nextStepIdx);
                                    if (nextRecipe != null && ColonistModificationUtility.CanPerformSurgery(pawn, nextRecipe, pawn.Map))
                                    {
                                        if (template.requirePlayerConfirmation)
                                        {
                                            if (record.status != ModificationStatus.PendingConfirmation)
                                            {
                                                record.status = ModificationStatus.PendingConfirmation;
                                                anyPendingConfirmation = true;
                                                readyTemplateNames.Add(template.label);
                                            }
                                        }
                                        else
                                        {
                                            // 不需要确认，直接开始
                                            StartSurgeryForPawn(pawn, template, record);
                                        }
                                    }
                                }
                                break;

                            case ModificationStatus.Delayed:
                                // 检查延迟是否到期
                                if (currentTick >= record.delayedUntilTick)
                                {
                                    record.status = ModificationStatus.Idle; // 重置为Idle，下次检查会重新评估
                                }
                                break;

                            case ModificationStatus.InProgress:
                                // 检查是否有bill在队列中（可能被手动删除）
                                if (!HasModificationBill(pawn, template))
                                {
                                    // Bill被手动删除，恢复
                                    int resumeStep = ColonistModificationUtility.GetNextStepIndex(pawn, template);
                                    if (resumeStep >= 0)
                                    {
                                        RecipeDef resumeRecipe = template.GetStep(resumeStep);
                                        if (resumeRecipe != null && ColonistModificationUtility.CanPerformSurgery(pawn, resumeRecipe, pawn.Map))
                                        {
                                            StartSurgeryForPawn(pawn, template, record);
                                        }
                                        else
                                        {
                                            record.status = ModificationStatus.PendingConfirmation;
                                        }
                                    }
                                    else
                                    {
                                        record.status = ModificationStatus.Completed;
                                    }
                                }
                                break;

                            case ModificationStatus.Completed:
                            case ModificationStatus.Dismissed:
                                // 无需处理
                                break;
                        }
                    }
                }
            }

            // 发送通知信件（限制频率）
            if (anyPendingConfirmation && currentTick - lastLetterTick >= LetterCooldownTicks)
            {
                SendPendingConfirmationLetter(readyTemplateNames);
                lastLetterTick = currentTick;
            }
        }

        /// <summary>
        /// 获取或创建pawn对某个模板的记录
        /// </summary>
        private PawnModificationRecord GetOrCreateRecord(Pawn pawn, ColonistModificationTemplateDef template)
        {
            if (!pawnRecords.ContainsKey(pawn.thingIDNumber))
            {
                pawnRecords[pawn.thingIDNumber] = new List<PawnModificationRecord>();
            }

            List<PawnModificationRecord> records = pawnRecords[pawn.thingIDNumber];
            PawnModificationRecord record = records.FirstOrDefault(r => r.templateDefName == template.defName);
            if (record == null)
            {
                record = new PawnModificationRecord
                {
                    templateDefName = template.defName,
                    status = ModificationStatus.Idle
                };
                records.Add(record);
            }

            // 自动检测已完成状态
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

        /// <summary>
        /// 更新记录的状态
        /// </summary>
        private void UpdateRecordStatus(Pawn pawn, ColonistModificationTemplateDef template, ModificationStatus newStatus)
        {
            PawnModificationRecord record = GetOrCreateRecord(pawn, template);
            if (record.status != newStatus)
            {
                record.status = newStatus;
            }
        }

        /// <summary>
        /// 为指定殖民者开始模板改造手术
        /// </summary>
        public void StartSurgeryForPawn(Pawn pawn, ColonistModificationTemplateDef template, PawnModificationRecord record = null)
        {
            if (record == null)
            {
                record = GetOrCreateRecord(pawn, template);
            }

            int nextStep = ColonistModificationUtility.GetNextStepIndex(pawn, template);
            if (nextStep < 0)
            {
                record.status = ModificationStatus.Completed;
                record.lastCompletedStepIndex = template.StepCount - 1;
                return;
            }

            RecipeDef recipe = template.GetStep(nextStep);
            if (recipe == null)
            {
                Log.Error($"ColonistModification: 模板 '{template.defName}' 的步骤{nextStep}无效。");
                return;
            }

            // 创建第一步手术Bill
            Bill_ColonistModification bill = ColonistModificationUtility.CreateBillForStep(
                recipe, pawn, template, nextStep);
            pawn.BillStack.AddBill(bill);

            record.status = ModificationStatus.InProgress;
            record.lastCompletedStepIndex = Math.Max(record.lastCompletedStepIndex, nextStep - 1);

            Messages.Message(
                $"开始对殖民者 {pawn.LabelShort} 执行制式改造 '{template.label}'，共 {template.StepCount} 个步骤。",
                new LookTargets(pawn), MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>
        /// 玩家确认开始模板改造
        /// </summary>
        public void ConfirmTemplateForPawn(Pawn pawn, ColonistModificationTemplateDef template)
        {
            PawnModificationRecord record = GetOrCreateRecord(pawn, template);
            record.status = ModificationStatus.Idle;
            StartSurgeryForPawn(pawn, template, record);
        }

        /// <summary>
        /// 玩家延迟模板改造
        /// </summary>
        public void DelayTemplateForPawn(Pawn pawn, ColonistModificationTemplateDef template)
        {
            PawnModificationRecord record = GetOrCreateRecord(pawn, template);
            record.status = ModificationStatus.Delayed;
            record.delayedUntilTick = Find.TickManager.TicksGame + (template.delayDays * 60000);

            Messages.Message(
                $"已暂缓殖民者 {pawn.LabelShort} 的 '{template.label}' 改造，将在 {template.delayDays} 天后重新提示。",
                new LookTargets(pawn), MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>
        /// 玩家忽略模板改造（不再提示）
        /// </summary>
        public void DismissTemplateForPawn(Pawn pawn, ColonistModificationTemplateDef template)
        {
            PawnModificationRecord record = GetOrCreateRecord(pawn, template);
            record.status = ModificationStatus.Dismissed;

            Messages.Message(
                $"已忽略殖民者 {pawn.LabelShort} 的 '{template.label}' 改造，不再提示。",
                new LookTargets(pawn), MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>
        /// 全局禁用某个模板
        /// </summary>
        public void DisableTemplate(string templateDefName)
        {
            disabledTemplates.Add(templateDefName);
        }

        /// <summary>
        /// 全局启用某个模板
        /// </summary>
        public void EnableTemplate(string templateDefName)
        {
            disabledTemplates.Remove(templateDefName);
        }

        /// <summary>
        /// 全局忽略某个殖民者
        /// </summary>
        public void IgnorePawn(int pawnThingID)
        {
            globallyIgnoredPawns.Add(pawnThingID);
        }

        /// <summary>
        /// 取消全局忽略某个殖民者
        /// </summary>
        public void UnignorePawn(int pawnThingID)
        {
            globallyIgnoredPawns.Remove(pawnThingID);
        }

        /// <summary>
        /// 获取殖民者对某个模板的改造状态记录
        /// </summary>
        public PawnModificationRecord GetRecord(Pawn pawn, ColonistModificationTemplateDef template)
        {
            if (!pawnRecords.ContainsKey(pawn.thingIDNumber))
                return null;

            return pawnRecords[pawn.thingIDNumber]
                .FirstOrDefault(r => r.templateDefName == template.defName);
        }

        /// <summary>
        /// 获取殖民者的所有改造记录
        /// </summary>
        public List<PawnModificationRecord> GetAllRecordsForPawn(Pawn pawn)
        {
            if (!pawnRecords.ContainsKey(pawn.thingIDNumber))
                return new List<PawnModificationRecord>();

            return new List<PawnModificationRecord>(pawnRecords[pawn.thingIDNumber]);
        }

        /// <summary>
        /// 通知步骤完成
        /// </summary>
        public void NotifyStepCompleted(Pawn pawn, ColonistModificationTemplateDef template, int stepIndex)
        {
            PawnModificationRecord record = GetOrCreateRecord(pawn, template);
            record.lastCompletedStepIndex = Math.Max(record.lastCompletedStepIndex, stepIndex);
            record.currentRetryCount = 0;

            // 检查是否全部完成
            if (ColonistModificationUtility.HasCompletedTemplate(pawn, template))
            {
                record.status = ModificationStatus.Completed;
                record.lastCompletedStepIndex = template.StepCount - 1;
            }
        }

        /// <summary>
        /// 通知步骤失败超过最大次数
        /// </summary>
        public void NotifyStepFailed(Pawn pawn, ColonistModificationTemplateDef template, int stepIndex)
        {
            PawnModificationRecord record = GetOrCreateRecord(pawn, template);
            record.failedStepIndex = stepIndex;
            record.lastCompletedStepIndex = Math.Max(record.lastCompletedStepIndex, stepIndex);

            // 检查是否还有后续步骤能执行
            int nextStep = ColonistModificationUtility.GetNextStepIndex(pawn, template);
            if (nextStep < 0)
            {
                // 没有更多步骤了
                record.status = ModificationStatus.Completed;
            }
        }

        /// <summary>
        /// 检查pawn的billStack中是否有Bill_ColonistModification
        /// </summary>
        private bool HasModificationBill(Pawn pawn, ColonistModificationTemplateDef template)
        {
            foreach (Bill bill in pawn.BillStack)
            {
                if (bill is Bill_ColonistModification modBill && modBill.template == template)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 发送等待确认的信件通知
        /// </summary>
        private void SendPendingConfirmationLetter(List<string> templateNames)
        {
            if (templateNames.Count == 0)
                return;

            string templateList = string.Join("、", templateNames.Distinct().Take(3));
            string title = "殖民者制式改造已就绪";
            string body = $"有改造模板的条件已满足，可以开始手术：\n\n{templateList}\n\n" +
                          $"打开\"殖民者改造管理\"窗口查看详情并确认或延迟手术。";

            Find.LetterStack.ReceiveLetter(title, body, LetterDefOf.NeutralEvent);
        }

        /// <summary>
        /// 计算殖民地总财富
        /// </summary>
        private float CalculateColonyWealth()
        {
            float wealth = 0f;
            foreach (Map map in Find.Maps)
            {
                if (map.IsPlayerHome)
                {
                    wealth += map.wealthWatcher.WealthTotal;
                }
            }
            return wealth;
        }

        /// <summary>
        /// 获取所有注册的改造模板
        /// </summary>
        public IEnumerable<ColonistModificationTemplateDef> GetAllTemplates()
        {
            if (DefDatabase<ColonistModificationTemplateDef>.DefCount == 0)
                return Enumerable.Empty<ColonistModificationTemplateDef>();

            return DefDatabase<ColonistModificationTemplateDef>.AllDefs;
        }

        /// <summary>
        /// 获取处于等待确认状态的所有(pawn, template)对
        /// </summary>
        public List<(Pawn pawn, ColonistModificationTemplateDef template)> GetPendingConfirmations()
        {
            List<(Pawn, ColonistModificationTemplateDef)> result = new List<(Pawn, ColonistModificationTemplateDef)>();

            foreach (var kvp in pawnRecords)
            {
                int pawnID = kvp.Key;
                Pawn pawn = FindPawnByID(pawnID);
                if (pawn == null)
                    continue;

                foreach (PawnModificationRecord record in kvp.Value)
                {
                    if (record.status == ModificationStatus.PendingConfirmation)
                    {
                        ColonistModificationTemplateDef template =
                            DefDatabase<ColonistModificationTemplateDef>.GetNamedSilentFail(record.templateDefName);
                        if (template != null)
                        {
                            result.Add((pawn, template));
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// 通过thingID查找pawn
        /// </summary>
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
            // 也搜索世界地图上的caravan
            List<Caravan> caravans = Find.WorldObjects.Caravans;
            foreach (Caravan caravan in caravans)
            {
                foreach (Pawn pawn in caravan.PawnsListForReading)
                {
                    if (pawn.thingIDNumber == thingID)
                        return pawn;
                }
            }
            return null;
        }

        /// <summary>
        /// 清理无效记录（pawn已死亡、消失等）
        /// </summary>
        private void CleanupInvalidRecords()
        {
            List<int> invalidIDs = new List<int>();
            foreach (int pawnID in pawnRecords.Keys)
            {
                Pawn pawn = FindPawnByID(pawnID);
                if (pawn == null || pawn.Dead || pawn.Destroyed)
                {
                    invalidIDs.Add(pawnID);
                }
            }
            foreach (int id in invalidIDs)
            {
                pawnRecords.Remove(id);
            }
        }

        /// <summary>
        /// 存档序列化
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();

            // 序列化pawnRecords
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                CleanupInvalidRecords();
            }

            // 将Dictionary序列化为可保存的格式
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
                {
                    pawnRecords[pawnIDs[i]] = recordsList[i];
                }
            }

            Scribe_Collections.Look(ref disabledTemplates, "disabledTemplates", LookMode.Value);
            Scribe_Collections.Look(ref globallyIgnoredPawns, "globallyIgnoredPawns", LookMode.Value);
        }
    }
}

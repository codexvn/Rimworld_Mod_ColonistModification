using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ColonistModification
{
    /// <summary>
    /// 殖民者制式改造专用手术Bill
    ///
    /// 相比普通Bill_Medical，增加了以下功能：
    /// 1. 关联改造模板，追踪当前处于模板的哪个步骤
    /// 2. 手术失败后自动重新添加Bill（autoRetryOnFailure）
    /// 3. 手术成功后自动推进到模板的下一步骤
    /// 4. 超过最大重试次数后通知玩家并跳过该步骤
    /// </summary>
    public class Bill_ColonistModification : Bill_Medical
    {
        /// <summary>所属模板的唯一ID</summary>
        public string templateId;

        /// <summary>运行时解析的模板引用（不序列化）</summary>
        public UserTemplate template;

        /// <summary>
        /// 当前步骤在模板中的索引
        /// </summary>
        public int currentStepIndex;

        /// <summary>
        /// 当前步骤已重试次数
        /// </summary>
        public int retryCount;

        public Bill_ColonistModification() : base()
        {
        }

        public Bill_ColonistModification(RecipeDef recipe) : base(recipe, null)
        {
        }

        /// <summary>
        /// 重写手术完成通知，实现失败重试和步骤推进
        ///
        /// 流程：
        /// 1. 记录手术前状态
        /// 2. 执行基础手术逻辑（base.Notify_IterationCompleted）
        /// 3. 检查手术是否成功（通过比对hediff变化）
        /// 4. 失败且有剩余重试次数 → 自动重新添加Bill
        /// 5. 失败且无剩余重试次数 → 通知玩家，跳过此步骤
        /// 6. 成功 → 推进到下一步骤或标记完成
        /// </summary>
        public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
        {
            // ---- 在base调用前捕获状态（base调用后会删除bill，无法再访问billStack） ----
            Pawn patient = this.GiverPawn;
            RecipeDef currentRecipe = this.recipe;
            BodyPartRecord operatedPart = this.Part;
            BillStack stack = this.billStack;
            UserTemplate currentTemplate = this.template ?? ColonistModificationManager.Instance?.GetTemplateById(this.templateId);
            string currentTemplateId = this.templateId;
            int stepIndex = this.currentStepIndex;
            int currentRetryCount = this.retryCount;
            HediffDef targetHediff = currentRecipe?.addsHediff;
            Xenogerm boundXenogerm = this.xenogerm;

            // 记录手术前目标hediff数量
            int hediffCountBefore = targetHediff != null
                ? patient.health.hediffSet.hediffs.Count(h => h.def == targetHediff)
                : 0;

            // ---- 执行基础手术逻辑（手术 + 删除Bill） ----
            base.Notify_IterationCompleted(billDoer, ingredients);
            // 注意：此后this.billStack为null，this已被删除

            // ---- 检查手术结果 ----
            bool surgerySucceeded;
            if (targetHediff != null)
            {
                // 比较手术前后hediff数量（避免重复类型hediff误判）
                int hediffCountAfter = patient.health.hediffSet.hediffs.Count(h => h.def == targetHediff);
                surgerySucceeded = hediffCountAfter > hediffCountBefore;
            }
            else if (currentRecipe.defName == "ImplantXenogerm")
            {
                // 异种胚植入：检查异种胚是否已被消耗（从地图上消失或销毁）
                surgerySucceeded = boundXenogerm == null || boundXenogerm.Destroyed || !boundXenogerm.Spawned;
            }
            else
            {
                // 其他无目标hediff的手术（如移除手术），检查pawn是否存活
                surgerySucceeded = !patient.Dead;
            }

            // ---- 根据结果处理后续流程 ----
            if (surgerySucceeded)
            {
                OnSurgerySucceeded(patient, currentTemplate, currentTemplateId, stepIndex, stack, operatedPart);
            }
            else
            {
                OnSurgeryFailed(patient, currentTemplate, currentTemplateId, stepIndex, currentRetryCount, currentRecipe, operatedPart, stack, boundXenogerm);
            }
        }

        private void OnSurgerySucceeded(Pawn patient, UserTemplate template, string templateId, int stepIndex, BillStack stack, BodyPartRecord operatedPart)
        {
            if (template == null)
                return;

            ColonistModificationManager.Instance?.NotifyStepCompleted(patient, template, stepIndex, operatedPart?.LabelCap);

            int nextStep = ColonistModificationUtility.GetNextStepIndex(patient, template);

            if (nextStep < 0)
            {
                Messages.Message(
                    $"殖民者 {patient.LabelShort} 已完成制式改造模板 '{template.name}' 的所有步骤！",
                    new LookTargets(patient), MessageTypeDefOf.PositiveEvent, false);
            }
            else
            {
                RecipeDef nextRecipe = template.GetStep(nextStep);
                if (nextRecipe != null)
                {
                    Bill_ColonistModification nextBill = ColonistModificationUtility.CreateBillForStep(
                        nextRecipe, patient, template, nextStep);
                    stack.AddBill(nextBill);

                    Messages.Message(
                        $"殖民者 {patient.LabelShort} 的 '{template.name}' 模板：已完成步骤 {stepIndex + 1}/{template.StepCount}，准备执行下一步。",
                        new LookTargets(patient), MessageTypeDefOf.NeutralEvent, false);
                }
            }
        }

        /// <summary>
        /// 手术失败后的处理：重试或跳过
        /// </summary>
        private void OnSurgeryFailed(Pawn patient, UserTemplate template, string templateId, int stepIndex,
            int currentRetryCount, RecipeDef recipe, BodyPartRecord part, BillStack stack, Xenogerm boundXenogerm)
        {
            if (template == null)
            {
                // 非模板关联的bill，忽略
                return;
            }

            int maxRetries = template.maxRetriesPerStep;
            bool autoRetry = template.autoRetryOnFailure;

            if (autoRetry && currentRetryCount < maxRetries)
            {
                // 自动重试：重新添加手术Bill
                Bill_ColonistModification retryBill = new Bill_ColonistModification(recipe);
                retryBill.templateId = templateId;
                retryBill.template = template;
                retryBill.currentStepIndex = stepIndex;
                retryBill.retryCount = currentRetryCount + 1;
                if (boundXenogerm != null)
                {
                    retryBill.xenogerm = boundXenogerm;
                }
                if (this.uniqueRequiredIngredients != null)
                {
                    retryBill.uniqueRequiredIngredients = new List<Thing>(this.uniqueRequiredIngredients);
                }
                stack.AddBill(retryBill);
                if (part != null)
                {
                    retryBill.Part = part;
                }

                // 发送提示消息
                Messages.Message(
                    $"殖民者 {patient.LabelShort} 的 {recipe.label} 手术失败！已自动重新安排手术。（重试 {currentRetryCount + 1}/{maxRetries}）",
                    new LookTargets(patient), MessageTypeDefOf.CautionInput, false);
            }
            else
            {
                // 超过最大重试次数或未启用自动重试 → 跳过此步骤
                string reason = autoRetry
                    ? $"手术经过 {maxRetries} 次重试仍然失败，跳过此步骤。"
                    : "手术失败，跳过此步骤。";

                Messages.Message(
                    $"殖民者 {patient.LabelShort} 的 {recipe.label} 手术失败！{reason}",
                    new LookTargets(patient), MessageTypeDefOf.NegativeEvent, false);

                // 通知Manager该步骤失败，并标记为已完成（跳过）以免GetNextStepIndex返回同一步骤
                ColonistModificationManager.Instance?.NotifyStepFailed(patient, template, stepIndex, part?.LabelCap);

                // 尝试推进到下一步
                int nextStep = ColonistModificationUtility.GetNextStepIndex(patient, template);
                if (nextStep >= 0)
                {
                    RecipeDef nextRecipe = template.GetStep(nextStep);
                    if (nextRecipe != null)
                    {
                        Bill_ColonistModification nextBill = ColonistModificationUtility.CreateBillForStep(
                            nextRecipe, patient, template, nextStep);
                        stack.AddBill(nextBill);
                    }
                }
            }
        }

        /// <summary>
        /// 克隆Bill时保留模板相关字段
        /// </summary>
        public override Bill Clone()
        {
            Bill_ColonistModification clone = (Bill_ColonistModification)base.Clone();
            clone.templateId = this.templateId;
            clone.template = this.template;
            clone.currentStepIndex = this.currentStepIndex;
            clone.retryCount = this.retryCount;
            return clone;
        }

        /// <summary>
        /// 序列化：保存模板相关数据
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref templateId, "templateId");
            Scribe_Values.Look(ref currentStepIndex, "currentStepIndex", 0);
            Scribe_Values.Look(ref retryCount, "retryCount", 0);
            // 加载后重新解析模板引用
            if (Scribe.mode == LoadSaveMode.LoadingVars && templateId != null)
                template = ColonistModificationManager.Instance?.GetTemplateById(templateId);
        }
    }
}

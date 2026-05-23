using Verse;

namespace ColonistModification
{
    /// <summary>
    /// 模版运行时覆盖设置，允许玩家在游戏中动态调整模版参数。
    /// 所有字段为 nullable：null 表示使用 XML 中定义的默认值。
    /// 通过 ExposeData 与存档一起持久化。
    /// </summary>
    public class TemplateRuntimeSettings : IExposable
    {
        /// <summary>null=使用XML默认值</summary>
        public bool? autoRetryOnFailure;

        /// <summary>null=使用XML默认值</summary>
        public int? maxRetriesPerStep;

        /// <summary>null=使用XML默认值</summary>
        public float? minColonyWealth;

        /// <summary>null=使用XML默认值</summary>
        public bool? requirePlayerConfirmation;

        /// <summary>null=使用XML默认值</summary>
        public int? delayDays;

        /// <summary>null=使用XML默认值</summary>
        public RimWorld.MedicineCategory? minMedicineCategory;

        // 以下 GetXxx 方法：返回玩家的覆盖值（如果设置了），否则回退到 XML 默认值

        public bool GetAutoRetryOnFailure(RimWorld.ColonistModificationTemplateDef t) => autoRetryOnFailure ?? t.autoRetryOnFailure;
        public int GetMaxRetriesPerStep(RimWorld.ColonistModificationTemplateDef t) => maxRetriesPerStep ?? t.maxRetriesPerStep;
        public float GetMinColonyWealth(RimWorld.ColonistModificationTemplateDef t) => minColonyWealth ?? t.minColonyWealth;
        public bool GetRequirePlayerConfirmation(RimWorld.ColonistModificationTemplateDef t) => requirePlayerConfirmation ?? t.requirePlayerConfirmation;
        public int GetDelayDays(RimWorld.ColonistModificationTemplateDef t) => delayDays ?? t.delayDays;
        public RimWorld.MedicineCategory GetMinMedicineCategory(RimWorld.ColonistModificationTemplateDef t) => minMedicineCategory ?? t.minMedicineCategory;

        public void ExposeData()
        {
            Scribe_Values.Look(ref autoRetryOnFailure, "autoRetryOnFailure");
            Scribe_Values.Look(ref maxRetriesPerStep, "maxRetriesPerStep");
            Scribe_Values.Look(ref minColonyWealth, "minColonyWealth");
            Scribe_Values.Look(ref requirePlayerConfirmation, "requirePlayerConfirmation");
            Scribe_Values.Look(ref delayDays, "delayDays");
            Scribe_Values.Look(ref minMedicineCategory, "minMedicineCategory");
        }
    }
}

using System;
using System.Collections.Generic;
using Verse;

namespace ColonistModification
{
    /// <summary>
    /// 玩家自定义的改造模板，保存在 ModSettings 中跨存档持久化。
    /// 替换原有的 XML-based ColonistModificationTemplateDef。
    /// </summary>
    public class UserTemplate : IExposable
    {
        /// <summary>唯一标识（GUID）</summary>
        public string id;

        /// <summary>显示名称</summary>
        public string name = "新模板";

        /// <summary>选中的手术 RecipeDef defNames</summary>
        public List<string> recipeDefNames = new List<string>();

        /// <summary>已解析的 RecipeDef 缓存（不序列化）</summary>
        public List<RecipeDef> resolvedRecipes = new List<RecipeDef>();

        // === 可配置参数 ===

        public bool autoRetryOnFailure = true;
        public int maxRetriesPerStep = 3;
        public float minColonyWealth = 0f;
        /// <summary>基因植入目标异种defName，null=不启用基因植入</summary>
        public string xenogermTargetXenotypeDefName;

        public bool colonistsOnly = true;
        public bool includeSlaves = false;
        public bool requirePlayerConfirmation = true;
        public int delayDays = 3;
        public MedicineCategory minMedicineCategory = MedicineCategory.Industrial;

        public int StepCount => resolvedRecipes.Count;

        public RecipeDef GetStep(int index)
        {
            if (index < 0 || index >= resolvedRecipes.Count) return null;
            return resolvedRecipes[index];
        }

        /// <summary>
        /// 将 recipeDefNames 解析为 RecipeDef 引用，过滤无效名称。
        /// </summary>
        public void ResolveReferences()
        {
            resolvedRecipes.Clear();
            foreach (string defName in recipeDefNames)
            {
                var recipe = DefDatabase<RecipeDef>.GetNamedSilentFail(defName);
                if (recipe != null)
                    resolvedRecipes.Add(recipe);
                else
                    Log.Warning($"ColonistModification: 模板 '{name}' 中的手术 '{defName}' 未找到，已跳过。");
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref name, "name", "新模板");
            Scribe_Collections.Look(ref recipeDefNames, "recipeDefNames", LookMode.Value);
            Scribe_Values.Look(ref autoRetryOnFailure, "autoRetryOnFailure", true);
            Scribe_Values.Look(ref maxRetriesPerStep, "maxRetriesPerStep", 3);
            Scribe_Values.Look(ref minColonyWealth, "minColonyWealth", 0f);
            Scribe_Values.Look(ref xenogermTargetXenotypeDefName, "xenogermTargetXenotypeDefName");
            Scribe_Values.Look(ref colonistsOnly, "colonistsOnly", true);
            Scribe_Values.Look(ref includeSlaves, "includeSlaves", false);
            Scribe_Values.Look(ref requirePlayerConfirmation, "requirePlayerConfirmation", true);
            Scribe_Values.Look(ref delayDays, "delayDays", 3);
            Scribe_Values.Look(ref minMedicineCategory, "minMedicineCategory", MedicineCategory.Industrial);
        }
    }

    /// <summary>
    /// 药品等级枚举
    /// </summary>
    public enum MedicineCategory
    {
        None,
        Herbal,
        Industrial,
        Glitter
    }
}

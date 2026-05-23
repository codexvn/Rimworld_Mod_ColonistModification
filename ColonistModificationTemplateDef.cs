using System.Collections.Generic;
using Verse;

namespace ColonistModification
{
    /// <summary>
    /// 殖民者制式改造模板定义
    /// 每个模板定义一套标准化的手术改造流程，可以批量应用于符合条件的殖民者
    /// 通过XML定义，在游戏中由 ColonistModificationManager 管理和执行
    /// </summary>
    public class ColonistModificationTemplateDef : Def
    {
        /// <summary>
        /// 手术步骤列表，按顺序执行。每个步骤对应一个RecipeDef的defName
        /// </summary>
        public List<string> recipeDefNames = new List<string>();

        /// <summary>
        /// 手术失败后是否自动重试
        /// </summary>
        public bool autoRetryOnFailure = true;

        /// <summary>
        /// 每个步骤的最大重试次数（超过后跳过该步骤并通知玩家）
        /// </summary>
        public int maxRetriesPerStep = 3;

        /// <summary>
        /// 最低殖民地财富阈值，低于此值不触发
        /// </summary>
        public float minColonyWealth = 0f;

        /// <summary>
        /// 是否只应用于殖民者（非奴隶、非囚犯）
        /// </summary>
        public bool colonistsOnly = true;

        /// <summary>
        /// 是否也应用于奴隶
        /// </summary>
        public bool includeSlaves = false;

        /// <summary>
        /// 最小生物学年龄
        /// </summary>
        public float minBiologicalAge = 0f;

        /// <summary>
        /// 目标异种类型defName列表（为空则不限制）
        /// </summary>
        public List<string> targetXenotypeDefNames = new List<string>();

        /// <summary>
        /// 需要使用的最低药品等级（Industrial/Herbal/None）
        /// </summary>
        public MedicineCategory minMedicineCategory = MedicineCategory.Industrial;

        /// <summary>
        /// 是否需要在开始每个殖民者改造前征求玩家确认
        /// </summary>
        public bool requirePlayerConfirmation = true;

        /// <summary>
        /// 延迟天数（玩家选择"稍后"后的等待天数）
        /// </summary>
        public int delayDays = 3;

        /// <summary>
        /// 模板优先级（数值越高越优先处理）
        /// </summary>
        public int priority = 0;

        /// <summary>
        /// 已解析的手术Recipe列表（由ResolveReferences填充）
        /// </summary>
        public List<RecipeDef> resolvedRecipes = new List<RecipeDef>();

        /// <summary>
        /// 验证并解析recipeDefNames为实际的RecipeDef引用
        /// </summary>
        public override void ResolveReferences()
        {
            base.ResolveReferences();
            resolvedRecipes.Clear();
            foreach (string defName in recipeDefNames)
            {
                RecipeDef recipe = DefDatabase<RecipeDef>.GetNamedSilentFail(defName);
                if (recipe != null)
                {
                    resolvedRecipes.Add(recipe);
                }
                else
                {
                    Log.Warning($"ColonistModification: 模板 '{defName}' 中的手术 '{defName}' 未找到，已跳过。");
                }
            }
            if (resolvedRecipes.Count == 0)
            {
                Log.Error($"ColonistModification: 模板 '{defName}' 中没有有效的手术步骤！");
            }
        }

        /// <summary>
        /// 获取改造步骤总数
        /// </summary>
        public int StepCount => resolvedRecipes.Count;

        /// <summary>
        /// 获取指定索引的手术步骤
        /// </summary>
        public RecipeDef GetStep(int index)
        {
            if (index < 0 || index >= resolvedRecipes.Count)
                return null;
            return resolvedRecipes[index];
        }
    }

    /// <summary>
    /// 药品等级枚举
    /// </summary>
    public enum MedicineCategory
    {
        None,       // 不使用药品
        Herbal,     // 草药
        Industrial, // 工业药品
        Glitter     // 闪耀世界药品
    }
}

using System.Collections.Generic;
using Verse;

namespace ColonistModification
{
    /// <summary>
    /// ModSettings：保存所有用户模板，跨存档持久化。
    /// 通过 ColonistModificationMod 加载和保存。
    /// </summary>
    public class ColonistModificationSettings : ModSettings
    {
        public List<UserTemplate> templates = new List<UserTemplate>();

        /// <summary>
        /// 首次加载时创建默认模板。
        /// </summary>
        public void EnsureDefaults()
        {
            if (templates.Count > 0) return;

            templates.Add(new UserTemplate
            {
                id = System.Guid.NewGuid().ToString(),
                name = "标准仿生改造",
                recipeDefNames = new List<string> { "InstallBionicArm", "InstallBionicLeg", "InstallBionicEye" },
                minColonyWealth = 5000f,
                autoRetryOnFailure = true,
                maxRetriesPerStep = 3,
            });

            templates.Add(new UserTemplate
            {
                id = System.Guid.NewGuid().ToString(),
                name = "高级仿生改造",
                recipeDefNames = new List<string> { "InstallBionicSpine", "InstallBionicHeart", "InstallBionicStomach", "InstallBionicArm", "InstallBionicLeg" },
                minColonyWealth = 50000f,
                autoRetryOnFailure = true,
                maxRetriesPerStep = 5,
            });

            templates.Add(new UserTemplate
            {
                id = System.Guid.NewGuid().ToString(),
                name = "统一异种基因植入",
                recipeDefNames = new List<string> { "ImplantXenogerm" },
                minColonyWealth = 10000f,
                autoRetryOnFailure = true,
                maxRetriesPerStep = 3,
                includeSlaves = true,
            });
        }

        /// <summary>
        /// 加载或重建后调用，解析所有模板的 RecipeDef 引用。
        /// </summary>
        public void ResolveAllReferences()
        {
            foreach (var t in templates)
                t.ResolveReferences();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref templates, "templates", LookMode.Deep);
        }
    }
}

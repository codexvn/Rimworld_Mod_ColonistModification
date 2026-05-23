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
            if (templates == null) templates = new List<UserTemplate>();
        }
    }
}

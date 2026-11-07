using UnityEngine;
using Verse;

namespace ColonistModification
{
    public class ColonistModificationMod : Mod
    {
        public static ColonistModificationMod Instance;

        public ColonistModificationSettings settings;

        public ColonistModificationMod(ModContentPack content) : base(content)
        {
            Instance = this;
            settings = GetSettings<ColonistModificationSettings>();
            settings.EnsureDefaults();
            // 模板的 RecipeDef 解析不在此处做：本构造函数运行于 CreateModClasses 阶段，
            // 早于 XML Def 加载，任何 defName 都查不到。见 TemplateReferenceBootstrap。
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.Label($"当前共有 {settings.templates.Count} 个改造模板。");
            listing.Label("请在游戏中通过「制式改造」窗口编辑模板。");
            listing.Gap();
            listing.CheckboxLabeled("启用详细日志（显示在日志标签页）", ref settings.enableDetailedLogging);

            listing.End();
        }

        public override string SettingsCategory()
        {
            return "制式改造";
        }
    }

    /// <summary>
    /// DefDatabase 就绪后解析各模板的 RecipeDef 引用。
    /// StaticConstructorOnStartup 在全部 Def 注册并完成交叉引用解析后运行，
    /// 是读取 DefDatabase 的最早安全时机；ColonistModificationManager 的缓存刷新另有兜底重解析。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class TemplateReferenceBootstrap
    {
        static TemplateReferenceBootstrap()
        {
            ColonistModificationMod.Instance?.settings.ResolveAllReferences();
        }
    }
}

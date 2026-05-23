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
            settings.ResolveAllReferences();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.Label($"当前共有 {settings.templates.Count} 个改造模板。");
            listing.Label("请在游戏中通过「制式改造」窗口编辑模板。");
            listing.Gap();
            listing.CheckboxLabeled("启用详细日志（显示在日志tab）", ref settings.enableDetailedLogging);

            listing.End();
        }

        public override string SettingsCategory()
        {
            return "制式改造";
        }
    }
}

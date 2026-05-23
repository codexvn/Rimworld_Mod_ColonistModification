using System.Linq;
using UnityEngine;
using Verse;

namespace ColonistModification
{
    /// <summary>
    /// Mod 入口：连接 ModSettings，提供跨存档模板配置。
    /// </summary>
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

            listing.End();
        }

        public override string SettingsCategory()
        {
            return "制式改造";
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
        }
    }
}

using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ColonistModification
{
    /// <summary>
    /// 殖民者制式改造的UI工具类
    /// 提供打开管理窗口等方法
    /// </summary>
    public static class ColonistModificationDialogUtility
    {
        /// <summary>
        /// 打开殖民者制式改造管理窗口
        /// 如果窗口已打开则不会重复打开
        /// </summary>
        public static void OpenDialog()
        {
            if (Find.WindowStack.WindowOfType<Dialog_ColonistModification>() != null)
                return;

            if (ColonistModificationManager.Instance == null)
            {
                Messages.Message("改造管理器未初始化，请先加载或开始游戏。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            Find.WindowStack.Add(new Dialog_ColonistModification());
        }
    }

    /// <summary>
    /// 通过Harmony为殖民者右键菜单添加入口，并在游戏启动时初始化
    /// </summary>
    [StaticConstructorOnStartup]
    public static class ColonistModificationMenuEntry
    {
        static ColonistModificationMenuEntry()
        {
            Harmony harmony = new Harmony("codexvn.ColonistModification.main");
            harmony.PatchAll();
            Log.Message("ColonistModification: 制式改造mod已加载，右键点击殖民者可打开管理窗口。");
        }
    }

    /// <summary>
    /// 为殖民者右键菜单添加"制式改造管理"选项
    /// </summary>
    [HarmonyPatch(typeof(Pawn), "GetFloatMenuOptions")]
    public static class PawnFloatMenuPatch
    {
        public static System.Collections.Generic.IEnumerable<FloatMenuOption> Postfix(
            System.Collections.Generic.IEnumerable<FloatMenuOption> values, Pawn __instance)
        {
            foreach (FloatMenuOption option in values)
                yield return option;

            if (__instance != null && __instance.IsColonistPlayerControlled)
            {
                yield return new FloatMenuOption(
                    "制式改造管理",
                    () => ColonistModificationDialogUtility.OpenDialog(),
                    MenuOptionPriority.Low);
            }
        }
    }
}

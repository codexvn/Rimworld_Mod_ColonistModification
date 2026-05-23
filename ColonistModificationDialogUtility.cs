using RimWorld;
using Verse;

namespace ColonistModification
{
    /// <summary>
    /// 殖民者制式改造的UI工具类
    /// 提供打开管理窗口等方法
    /// </summary>
    public static class ColonistModificationDialogUtility
    {
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
}

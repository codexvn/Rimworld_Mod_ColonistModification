using System.Collections.Generic;
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
        /// <summary>
        /// 打开殖民者制式改造管理窗口
        /// 如果窗口已打开则不会重复打开
        /// </summary>
        public static void OpenDialog()
        {
            // 检查是否已经打开
            if (Find.WindowStack.WindowOfType<Dialog_ColonistModification>() != null)
                return;

            // 检查Manager是否可用
            if (ColonistModificationManager.Instance == null)
            {
                Messages.Message("改造管理器未初始化，请先加载或开始游戏。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            Find.WindowStack.Add(new Dialog_ColonistModification());
        }

        /// <summary>
        /// 为特定殖民者打开改造管理窗口
        /// （暂不支持直接跳转到特定殖民者，打开通用窗口）
        /// </summary>
        public static void OpenDialogForPawn(Pawn pawn)
        {
            OpenDialog();
        }
    }

    /// <summary>
    /// 在主菜单或右键菜单中添加打开改造管理窗口的入口
    /// 通过[StaticConstructorOnStartup]自动注册
    /// </summary>
    [StaticConstructorOnStartup]
    public static class ColonistModificationMenuEntry
    {
        static ColonistModificationMenuEntry()
        {
            // 此处可以添加菜单注册逻辑
            // 具体的UI入口点（如底部按钮）可通过XML patch添加到对应的菜单def中
            Log.Message("ColonistModification: 殖民者制式改造mod已加载。通过开发者菜单或Mod设置打开管理窗口。");
        }
    }
}

using RimWorld;
using Verse;

namespace ColonistModification
{
    /// <summary>
    /// 底部栏按钮行为：点击时打开殖民者制式改造管理窗口。
    /// 通过 Defs/MainButtonDef/MainButtons.xml 注册到游戏底部栏。
    /// </summary>
    public class MainButtonWorker_ColonistModification : MainButtonWorker
    {
        public override void Activate()
        {
            ColonistModificationDialogUtility.OpenDialog();
        }
    }
}

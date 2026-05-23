using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ColonistModification
{
    /// <summary>
    /// 制式改造确认弹窗：在右侧信件栏显示，点击后可选择开始/稍后/忽略。
    /// </summary>
    public class ChoiceLetter_ColonistModification : ChoiceLetter
    {
        public int pawnThingID;
        public string templateId;
        public string templateLabel;
        public string nextStepLabel;

        public override bool CanDismissWithRightClick => true;

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                // 确认开始
                yield return new DiaOption("开始改造")
                {
                    action = () =>
                    {
                        var manager = ColonistModificationManager.Instance;
                        var pawn = FindPawn();
                        var tpl = GetTemplate();
                        if (manager != null && pawn != null && tpl != null)
                        {
                            manager.ConfirmTemplateForPawn(pawn, tpl);
                            Messages.Message($"已开始 {pawn.LabelShort} 的 {tpl.name} 改造。",
                                new LookTargets(pawn), MessageTypeDefOf.NeutralEvent, false);
                        }
                    },
                    resolveTree = true
                };

                // 延迟
                yield return new DiaOption("稍后提醒")
                {
                    action = () =>
                    {
                        var manager = ColonistModificationManager.Instance;
                        var pawn = FindPawn();
                        var tpl = GetTemplate();
                        if (manager != null && pawn != null && tpl != null)
                        {
                            manager.DelayTemplateForPawn(pawn, tpl);
                        }
                    },
                    resolveTree = true
                };

                // 忽略
                yield return new DiaOption("忽略")
                {
                    action = () =>
                    {
                        var manager = ColonistModificationManager.Instance;
                        var pawn = FindPawn();
                        var tpl = GetTemplate();
                        if (manager != null && pawn != null && tpl != null)
                        {
                            manager.DismissTemplateForPawn(pawn, tpl);
                        }
                    },
                    resolveTree = true
                };

                // 打开管理窗口
                yield return new DiaOption("打开管理窗口")
                {
                    action = () =>
                    {
                        ColonistModificationDialogUtility.OpenDialog();
                    },
                    resolveTree = true
                };
            }
        }

        public override void OpenLetter()
        {
            DiaNode diaNode = new DiaNode(Text);
            diaNode.options.AddRange(Choices);
            Find.WindowStack.Add(new Dialog_NodeTreeWithFactionInfo(diaNode, null, false, false, title));
        }

        private Pawn FindPawn()
        {
            foreach (Map map in Find.Maps)
                foreach (Pawn p in map.mapPawns.AllPawns)
                    if (p.thingIDNumber == pawnThingID) return p;
            return null;
        }

        private UserTemplate GetTemplate()
        {
            return ColonistModificationManager.Instance?.GetTemplateById(templateId);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref pawnThingID, "pawnThingID");
            Scribe_Values.Look(ref templateId, "templateId");
            Scribe_Values.Look(ref templateLabel, "templateLabel");
            Scribe_Values.Look(ref nextStepLabel, "nextStepLabel");
        }
    }
}

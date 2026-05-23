using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ColonistModification
{
    /// <summary>
    /// 殖民者制式改造管理窗口
    ///
    /// 提供以下功能：
    /// - 查看所有已定义的改造模板
    /// - 查看每个模板在各殖民者身上的状态
    /// - 确认、延迟或忽略待处理的改造
    /// - 启用/禁用模板
    /// - 全局排除殖民者
    /// </summary>
    public class Dialog_ColonistModification : Window
    {
        /// <summary>当前选中的标签页索引</summary>
        private int selectedTab = 0;

        /// <summary>标签页名称列表</summary>
        private readonly string[] tabNames = { "模板概览", "待处理列表", "已完成记录" };

        /// <summary>滚动位置</summary>
        private Vector2 scrollPosition = Vector2.zero;

        /// <summary>高度缓存</summary>
        private float cachedHeight = 0f;

        /// <summary>Manager引用</summary>
        private ColonistModificationManager Manager => ColonistModificationManager.Instance;

        public Dialog_ColonistModification()
        {
            this.forcePause = true;
            this.draggable = true;
            this.resizeable = true;
            this.doCloseX = true;
            this.closeOnCancel = true;
            this.closeOnAccept = false;
            this.absorbInputAroundWindow = true;
            this.soundAppear = SoundDefOf.CommsWindow_Open;
            this.soundClose = SoundDefOf.CommsWindow_Close;
        }

        public override Vector2 InitialSize => new Vector2(900f, 650f);

        public override void DoWindowContents(Rect inRect)
        {
            // 标题
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 35f), "殖民者制式改造管理");
            Text.Font = GameFont.Small;

            // 标签页按钮
            float tabWidth = inRect.width / 3f;
            Rect tabRect = new Rect(0f, 40f, inRect.width, 30f);
            for (int i = 0; i < tabNames.Length; i++)
            {
                Rect buttonRect = new Rect(tabRect.x + i * tabWidth, tabRect.y, tabWidth - 4f, 28f);
                bool isSelected = (i == selectedTab);
                if (isSelected)
                {
                    GUI.color = Color.cyan;
                }
                if (Widgets.ButtonText(buttonRect, tabNames[i], true, false, true))
                {
                    selectedTab = i;
                    scrollPosition = Vector2.zero;
                }
                GUI.color = Color.white;
            }

            // 内容区域
            Rect contentRect = new Rect(0f, 75f, inRect.width - 16f, inRect.height - 110f);
            Rect viewRect;

            switch (selectedTab)
            {
                case 0:
                    viewRect = new Rect(0f, 0f, contentRect.width - 20f, Mathf.Max(contentRect.height, cachedHeight));
                    Widgets.BeginScrollView(contentRect, ref scrollPosition, viewRect, true);
                    DrawTemplateOverview(viewRect);
                    Widgets.EndScrollView();
                    break;

                case 1:
                    viewRect = new Rect(0f, 0f, contentRect.width - 20f, Mathf.Max(contentRect.height, cachedHeight));
                    Widgets.BeginScrollView(contentRect, ref scrollPosition, viewRect, true);
                    DrawPendingList(viewRect);
                    Widgets.EndScrollView();
                    break;

                case 2:
                    viewRect = new Rect(0f, 0f, contentRect.width - 20f, Mathf.Max(contentRect.height, cachedHeight));
                    Widgets.BeginScrollView(contentRect, ref scrollPosition, viewRect, true);
                    DrawCompletedList(viewRect);
                    Widgets.EndScrollView();
                    break;
            }

            // 底部按钮
            Rect bottomRect = new Rect(0f, inRect.height - 30f, inRect.width, 30f);
            if (Widgets.ButtonText(new Rect(bottomRect.x, bottomRect.y, 120f, 28f), "刷新状态"))
            {
                scrollPosition = Vector2.zero;
            }
            if (Widgets.ButtonText(new Rect(bottomRect.x + 130f, bottomRect.y, 160f, 28f), "一键确认全部待处理"))
            {
                ConfirmAllPending();
            }
            if (Widgets.ButtonText(new Rect(bottomRect.x + 300f, bottomRect.y, 100f, 28f), "关闭"))
            {
                this.Close(true);
            }
        }

        /// <summary>
        /// 标签页1：模板概览 - 显示所有模板及其在各殖民者身上的状态
        /// </summary>
        private void DrawTemplateOverview(Rect rect)
        {
            if (Manager == null)
            {
                Widgets.Label(new Rect(0f, 0f, rect.width, 30f), "改造管理器未初始化，请先加载或开始游戏。");
                return;
            }

            IEnumerable<ColonistModificationTemplateDef> templates = Manager.GetAllTemplates();
            if (!templates.Any())
            {
                Widgets.Label(new Rect(0f, 0f, rect.width, 30f), "没有定义任何改造模板。请通过XML添加模板定义。");
                return;
            }

            float y = 0f;
            float width = rect.width;

            foreach (ColonistModificationTemplateDef template in templates)
            {
                if (template.StepCount == 0)
                    continue;

                // 模板标题卡片
                Rect cardRect = new Rect(0f, y, width, 28f);
                Widgets.DrawBoxSolid(cardRect, new Color(0.2f, 0.2f, 0.25f, 0.5f));

                // 模板名称和状态
                string templateLabel = $"{template.label} ({template.StepCount} 个步骤, 优先级:{template.priority})";
                Widgets.Label(new Rect(4f, y + 4f, width * 0.4f, 24f), templateLabel);

                // 步骤预览
                string stepsPreview = string.Join(" → ", template.resolvedRecipes.Select(r => r.label));
                if (stepsPreview.Length > 60)
                    stepsPreview = stepsPreview.Substring(0, 57) + "...";
                Widgets.Label(new Rect(width * 0.42f, y + 4f, width * 0.45f, 24f), stepsPreview);

                y += 32f;

                // 显示匹配的殖民者列表
                List<Pawn> matchingPawns = GetMatchingPawns(template);
                if (matchingPawns.Count == 0)
                {
                    Widgets.Label(new Rect(20f, y, width - 20f, 22f), "没有匹配的殖民者。");
                    y += 24f;
                }
                else
                {
                    foreach (Pawn pawn in matchingPawns)
                    {
                        PawnModificationRecord record = Manager.GetRecord(pawn, template);
                        string statusLabel = GetStatusLabel(record);
                        Color statusColor = GetStatusColor(record);

                        // 殖民者行
                        Rect pawnRow = new Rect(20f, y, width - 20f, 24f);

                        // 选中框（高亮待确认的）
                        if (record != null && record.status == ModificationStatus.PendingConfirmation)
                        {
                            Widgets.DrawBoxSolid(pawnRow, new Color(1f, 0.92f, 0.016f, 0.15f));
                        }

                        // 殖民者名称
                        Widgets.Label(new Rect(24f, y + 2f, 150f, 20f), pawn.LabelShort);

                        // 状态
                        GUI.color = statusColor;
                        Widgets.Label(new Rect(180f, y + 2f, 150f, 20f), statusLabel);
                        GUI.color = Color.white;

                        // 进度
                        if (record != null)
                        {
                            string progressLabel = $"进度: {record.lastCompletedStepIndex + 1}/{template.StepCount}";
                            Widgets.Label(new Rect(330f, y + 2f, 120f, 20f), progressLabel);
                        }

                        // 操作按钮
                        float buttonX = width - 320f;
                        DrawActionButtons(buttonX, y + 1f, pawn, template, record);

                        y += 26f;
                    }
                }

                y += 8f; // 模板间距
            }

            cachedHeight = y + 40f;
        }

        /// <summary>
        /// 标签页2：待处理列表 - 只显示等待确认的项目
        /// </summary>
        private void DrawPendingList(Rect rect)
        {
            if (Manager == null)
            {
                Widgets.Label(new Rect(0f, 0f, rect.width, 30f), "改造管理器未初始化。");
                return;
            }

            var pendingList = Manager.GetPendingConfirmations();
            if (!pendingList.Any())
            {
                Widgets.Label(new Rect(0f, 0f, rect.width, 30f), "当前没有等待确认的改造项目。");
                cachedHeight = 30f;
                return;
            }

            float y = 0f;
            foreach (var (pawn, template) in pendingList)
            {
                PawnModificationRecord record = Manager.GetRecord(pawn, template);

                Rect cardRect = new Rect(0f, y, rect.width, 28f);
                Widgets.DrawBoxSolid(cardRect, new Color(0.25f, 0.25f, 0.1f, 0.4f));

                string label = $"殖民者: {pawn.LabelShort}  |  模板: {template.label}";
                Widgets.Label(new Rect(4f, y + 4f, rect.width * 0.5f, 24f), label);

                int nextStep = record != null ? record.lastCompletedStepIndex + 1 : 0;
                if (nextStep < template.StepCount)
                {
                    RecipeDef nextRecipe = template.GetStep(nextStep);
                    if (nextRecipe != null)
                    {
                        Widgets.Label(new Rect(rect.width * 0.52f, y + 4f, rect.width * 0.48f, 24f),
                            $"下一步: {nextRecipe.label}");
                    }
                }

                y += 32f;

                // 操作按钮
                DrawActionButtons(20f, y, pawn, template, record);
                y += 28f;
            }

            cachedHeight = y + 40f;
        }

        /// <summary>
        /// 标签页3：已完成记录
        /// </summary>
        private void DrawCompletedList(Rect rect)
        {
            if (Manager == null)
            {
                Widgets.Label(new Rect(0f, 0f, rect.width, 30f), "改造管理器未初始化。");
                return;
            }

            float y = 0f;
            bool foundAny = false;

            foreach (ColonistModificationTemplateDef template in Manager.GetAllTemplates())
            {
                foreach (Pawn pawn in GetMatchingPawns(template))
                {
                    PawnModificationRecord record = Manager.GetRecord(pawn, template);
                    if (record != null && record.status == ModificationStatus.Completed)
                    {
                        foundAny = true;
                        Rect rowRect = new Rect(0f, y, rect.width, 24f);
                        Widgets.Label(rowRect, $"殖民者: {pawn.LabelShort}  |  模板: {template.label}  |  状态: ✓ 已完成");
                        y += 26f;
                    }
                }
            }

            if (!foundAny)
            {
                Widgets.Label(new Rect(0f, 0f, rect.width, 30f), "暂无已完成的改造记录。");
            }

            cachedHeight = y + 40f;
        }

        /// <summary>
        /// 绘制操作按钮（确认、延迟、忽略）
        /// </summary>
        private void DrawActionButtons(float x, float y, Pawn pawn, ColonistModificationTemplateDef template, PawnModificationRecord record)
        {
            float buttonWidth = 90f;
            float gap = 6f;

            if (record == null)
                return;

            switch (record.status)
            {
                case ModificationStatus.PendingConfirmation:
                    // 确认按钮
                    if (Widgets.ButtonText(new Rect(x, y, buttonWidth, 22f), "✓ 确认"))
                    {
                        Manager.ConfirmTemplateForPawn(pawn, template);
                    }
                    x += buttonWidth + gap;

                    // 延迟按钮
                    if (Widgets.ButtonText(new Rect(x, y, buttonWidth, 22f), "⏱ 稍后"))
                    {
                        Manager.DelayTemplateForPawn(pawn, template);
                    }
                    x += buttonWidth + gap;

                    // 忽略按钮
                    if (Widgets.ButtonText(new Rect(x, y, buttonWidth, 22f), "✗ 忽略"))
                    {
                        Manager.DismissTemplateForPawn(pawn, template);
                    }
                    break;

                case ModificationStatus.InProgress:
                    GUI.color = Color.green;
                    Widgets.Label(new Rect(x, y, 200f, 22f), "正在进行中...");
                    GUI.color = Color.white;
                    break;

                case ModificationStatus.Delayed:
                    int remainingTicks = record.delayedUntilTick - Find.TickManager.TicksGame;
                    float remainingDays = remainingTicks / 60000f;
                    Widgets.Label(new Rect(x, y, 250f, 22f), $"已延迟，约 {remainingDays:F1} 天后重新提示");

                    // 可手动提前
                    if (Widgets.ButtonText(new Rect(x + 260f, y, buttonWidth, 22f), "立即开始"))
                    {
                        Manager.ConfirmTemplateForPawn(pawn, template);
                    }
                    break;

                case ModificationStatus.Dismissed:
                    GUI.color = Color.gray;
                    Widgets.Label(new Rect(x, y, 200f, 22f), "已忽略");
                    GUI.color = Color.white;

                    // 可手动重新激活
                    if (Widgets.ButtonText(new Rect(x + 210f, y, buttonWidth, 22f), "重新激活"))
                    {
                        Manager.ConfirmTemplateForPawn(pawn, template);
                    }
                    break;

                case ModificationStatus.Completed:
                    GUI.color = Color.green;
                    Widgets.Label(new Rect(x, y, 200f, 22f), "✓ 已全部完成");
                    GUI.color = Color.white;
                    break;

                case ModificationStatus.Idle:
                    // 条件不满足，手动触发
                    if (Widgets.ButtonText(new Rect(x, y, buttonWidth + 20f, 22f), "强制开始"))
                    {
                        Manager.ConfirmTemplateForPawn(pawn, template);
                    }
                    break;
            }
        }

        /// <summary>
        /// 一键确认所有待处理的改造
        /// </summary>
        private void ConfirmAllPending()
        {
            if (Manager == null)
                return;

            var pendingList = Manager.GetPendingConfirmations();
            foreach (var (pawn, template) in pendingList)
            {
                Manager.ConfirmTemplateForPawn(pawn, template);
            }

            Messages.Message($"已确认 {pendingList.Count} 项制式改造，手术将依次开始。", MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>
        /// 获取匹配模板的所有殖民者
        /// </summary>
        private List<Pawn> GetMatchingPawns(ColonistModificationTemplateDef template)
        {
            List<Pawn> result = new List<Pawn>();
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome)
                    continue;

                foreach (Pawn pawn in map.mapPawns.FreeColonistsAndPrisoners)
                {
                    if (ColonistModificationUtility.PawnMatchesTemplate(pawn, template))
                    {
                        result.Add(pawn);
                    }
                }
            }
            // 按名称排序
            result.Sort((a, b) => a.LabelShort.CompareTo(b.LabelShort));
            return result;
        }

        /// <summary>
        /// 获取状态描述文本
        /// </summary>
        private string GetStatusLabel(PawnModificationRecord record)
        {
            if (record == null)
                return "未评估";

            switch (record.status)
            {
                case ModificationStatus.Idle: return "条件不满足";
                case ModificationStatus.PendingConfirmation: return "⚡ 等待确认";
                case ModificationStatus.InProgress: return "▶ 进行中";
                case ModificationStatus.Completed: return "✓ 已完成";
                case ModificationStatus.Dismissed: return "✗ 已忽略";
                case ModificationStatus.Delayed: return "⏱ 已延迟";
                default: return "未知";
            }
        }

        /// <summary>
        /// 获取状态对应的颜色
        /// </summary>
        private Color GetStatusColor(PawnModificationRecord record)
        {
            if (record == null)
                return Color.gray;

            switch (record.status)
            {
                case ModificationStatus.PendingConfirmation: return new Color(1f, 0.84f, 0f); // 金色
                case ModificationStatus.InProgress: return Color.green;
                case ModificationStatus.Completed: return new Color(0.3f, 0.8f, 0.3f); // 深绿
                case ModificationStatus.Dismissed: return Color.gray;
                case ModificationStatus.Delayed: return new Color(0.5f, 0.5f, 1f); // 浅蓝
                case ModificationStatus.Idle: return Color.white;
                default: return Color.white;
            }
        }
    }
}

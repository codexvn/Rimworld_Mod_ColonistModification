using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ColonistModification
{
    public class Dialog_ColonistModification : Window
    {
        private int selectedTab = 0;
        private readonly string[] tabNames = { "模板概览", "待处理列表", "已完成记录", "模板设置" };
        private Vector2 scrollPosition = Vector2.zero;
        private float cachedHeight = 0f;
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
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 35f), "殖民者制式改造管理");
            Text.Font = GameFont.Small;

            float tabWidth = inRect.width / 4f;
            Rect tabRect = new Rect(0f, 40f, inRect.width, 30f);
            for (int i = 0; i < tabNames.Length; i++)
            {
                Rect buttonRect = new Rect(tabRect.x + i * tabWidth, tabRect.y, tabWidth - 4f, 28f);
                if (i == selectedTab) GUI.color = Color.cyan;
                if (Widgets.ButtonText(buttonRect, tabNames[i], true, false, true))
                {
                    selectedTab = i;
                    scrollPosition = Vector2.zero;
                }
                GUI.color = Color.white;
            }

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
                case 3:
                    viewRect = new Rect(0f, 0f, contentRect.width - 20f, Mathf.Max(contentRect.height, cachedHeight));
                    Widgets.BeginScrollView(contentRect, ref scrollPosition, viewRect, true);
                    DrawTemplateSettings(viewRect);
                    Widgets.EndScrollView();
                    break;
            }

            Rect bottomRect = new Rect(0f, inRect.height - 30f, inRect.width, 30f);
            if (Widgets.ButtonText(new Rect(bottomRect.x, bottomRect.y, 120f, 28f), "刷新状态"))
                scrollPosition = Vector2.zero;
            if (Widgets.ButtonText(new Rect(bottomRect.x + 130f, bottomRect.y, 160f, 28f), "一键确认全部待处理"))
                ConfirmAllPending();
            if (Widgets.ButtonText(new Rect(bottomRect.x + 300f, bottomRect.y, 100f, 28f), "关闭"))
                this.Close(true);
        }

        /// <summary>
        /// 动态高度标签：用 Text.CalcHeight 计算文本实际所需高度，避免长文本被截断。
        /// 返回实际使用的高度，调用方据此推进 y 坐标。
        /// </summary>
        private static float LabelWithHeight(Rect rect, string text)
        {
            float height = Text.CalcHeight(text, rect.width);
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, height), text);
            return height;
        }

        // ==================== Tab 0: 模板概览 ====================

        private void DrawTemplateOverview(Rect rect)
        {
            if (Manager == null)
            {
                LabelWithHeight(new Rect(0f, 0f, rect.width, 30f), "改造管理器未初始化，请先加载或开始游戏。");
                return;
            }

            var templates = Manager.GetAllTemplates().ToList();
            if (!templates.Any())
            {
                LabelWithHeight(new Rect(0f, 0f, rect.width, 30f), "没有定义任何改造模板。请通过XML添加模板定义。");
                return;
            }

            float y = 0f;
            float width = rect.width;

            foreach (var template in templates)
            {
                if (template.StepCount == 0) continue;

                // Template header
                string headerText = $"{template.label} ({template.StepCount} 个步骤, 优先级:{template.priority})";
                string stepsPreview = string.Join(" → ", template.resolvedRecipes.Select(r => r.label));
                if (stepsPreview.Length > 60) stepsPreview = stepsPreview.Substring(0, 57) + "...";

                float headerH = Text.CalcHeight(headerText, width * 0.4f);
                float stepsH = Text.CalcHeight(stepsPreview, width * 0.45f);
                float cardH = Mathf.Max(headerH, stepsH) + 10f;

                Rect cardRect = new Rect(0f, y, width, cardH);
                Widgets.DrawBoxSolid(cardRect, new Color(0.2f, 0.2f, 0.25f, 0.5f));
                Widgets.Label(new Rect(4f, y + 4f, width * 0.4f, headerH), headerText);
                Widgets.Label(new Rect(width * 0.42f, y + 4f, width * 0.45f, stepsH), stepsPreview);
                y += cardH + 2f;

                // Pawns
                var matchingPawns = GetMatchingPawns(template);
                if (matchingPawns.Count == 0)
                {
                    float h = LabelWithHeight(new Rect(20f, y, width - 20f, 22f), "没有匹配的殖民者。");
                    y += h + 2f;
                }
                else
                {
                    foreach (var pawn in matchingPawns)
                    {
                        var record = Manager.GetRecord(pawn, template);
                        string statusLabel = GetStatusLabel(record);
                        Color statusColor = GetStatusColor(record);
                        string progressLabel = record != null
                            ? $"进度: {record.lastCompletedStepIndex + 1}/{template.StepCount}"
                            : "";

                        float nameH = Text.CalcHeight(pawn.LabelShort, 150f);
                        float statH = Text.CalcHeight(statusLabel, 150f);
                        float progH = Text.CalcHeight(progressLabel, 120f);
                        float rowH = Mathf.Max(nameH, statH, progH, 22f) + 4f;

                        Rect pawnRow = new Rect(20f, y, width - 20f, rowH);
                        if (record != null && record.status == ModificationStatus.PendingConfirmation)
                            Widgets.DrawBoxSolid(pawnRow, new Color(1f, 0.92f, 0.016f, 0.15f));

                        Widgets.Label(new Rect(24f, y + 2f, 150f, rowH), pawn.LabelShort);

                        GUI.color = statusColor;
                        Widgets.Label(new Rect(180f, y + 2f, 150f, rowH), statusLabel);
                        GUI.color = Color.white;

                        if (record != null)
                            Widgets.Label(new Rect(330f, y + 2f, 120f, rowH), progressLabel);

                        float buttonX = width - 320f;
                        DrawActionButtons(buttonX, y + 1f, pawn, template, record);

                        y += rowH + 2f;
                    }
                }
                y += 8f;
            }
            cachedHeight = y + 40f;
        }

        // ==================== Tab 1: 待处理列表 ====================

        private void DrawPendingList(Rect rect)
        {
            if (Manager == null)
            {
                LabelWithHeight(new Rect(0f, 0f, rect.width, 30f), "改造管理器未初始化。");
                return;
            }

            var pendingList = Manager.GetPendingConfirmations();
            if (!pendingList.Any())
            {
                LabelWithHeight(new Rect(0f, 0f, rect.width, 30f), "当前没有等待确认的改造项目。");
                cachedHeight = 30f;
                return;
            }

            float y = 0f;
            foreach (var (pawn, template) in pendingList)
            {
                var record = Manager.GetRecord(pawn, template);

                string label = $"殖民者: {pawn.LabelShort}  |  模板: {template.label}";
                float labelH = Text.CalcHeight(label, rect.width * 0.5f);
                int nextStep = record != null ? record.lastCompletedStepIndex + 1 : 0;
                string nextText = "";
                if (nextStep < template.StepCount)
                {
                    var nextRecipe = template.GetStep(nextStep);
                    if (nextRecipe != null) nextText = $"下一步: {nextRecipe.label}";
                }
                float nextH = string.IsNullOrEmpty(nextText) ? 0f : Text.CalcHeight(nextText, rect.width * 0.48f);
                float cardH = Mathf.Max(labelH, nextH) + 10f;

                Rect cardRect = new Rect(0f, y, rect.width, cardH);
                Widgets.DrawBoxSolid(cardRect, new Color(0.25f, 0.25f, 0.1f, 0.4f));
                Widgets.Label(new Rect(4f, y + 4f, rect.width * 0.5f, labelH), label);
                if (!string.IsNullOrEmpty(nextText))
                    Widgets.Label(new Rect(rect.width * 0.52f, y + 4f, rect.width * 0.48f, nextH), nextText);

                y += cardH + 2f;
                DrawActionButtons(20f, y, pawn, template, record);
                y += 28f;
            }
            cachedHeight = y + 40f;
        }

        // ==================== Tab 2: 已完成记录 ====================

        private void DrawCompletedList(Rect rect)
        {
            if (Manager == null)
            {
                LabelWithHeight(new Rect(0f, 0f, rect.width, 30f), "改造管理器未初始化。");
                return;
            }

            float y = 0f;
            bool foundAny = false;

            foreach (var template in Manager.GetAllTemplates())
            {
                foreach (var pawn in GetMatchingPawns(template))
                {
                    var record = Manager.GetRecord(pawn, template);
                    if (record != null && record.status == ModificationStatus.Completed)
                    {
                        foundAny = true;
                        string text = $"殖民者: {pawn.LabelShort}  |  模板: {template.label}  |  状态: ✓ 已完成";
                        float h = LabelWithHeight(new Rect(0f, y, rect.width, 24f), text);
                        y += h + 2f;
                    }
                }
            }

            if (!foundAny)
                LabelWithHeight(new Rect(0f, 0f, rect.width, 30f), "暂无已完成的改造记录。");

            cachedHeight = y + 40f;
        }

        // ==================== Tab 3: 模板设置 ====================

        private void DrawTemplateSettings(Rect rect)
        {
            if (Manager == null)
            {
                LabelWithHeight(new Rect(0f, 0f, rect.width, 30f), "改造管理器未初始化。");
                return;
            }

            var templates = Manager.GetAllTemplates().ToList();
            if (!templates.Any())
            {
                LabelWithHeight(new Rect(0f, 0f, rect.width, 30f), "没有定义任何改造模板。");
                cachedHeight = 30f;
                return;
            }

            float y = 0f;
            float width = rect.width;

            foreach (var template in templates)
            {
                if (template.StepCount == 0) continue;

                // Header
                string headerText = $"【{template.label}】({template.StepCount} 个步骤)";
                Text.Font = GameFont.Medium;
                float headerH = Text.CalcHeight(headerText, width);
                Widgets.Label(new Rect(0f, y, width, headerH), headerText);
                y += headerH + 4f;
                Text.Font = GameFont.Small;

                // Description
                if (!string.IsNullOrEmpty(template.description))
                {
                    float descH = Text.CalcHeight(template.description, width - 10f);
                    GUI.color = Color.grey;
                    Widgets.Label(new Rect(10f, y, width - 10f, descH), template.description);
                    GUI.color = Color.white;
                    y += descH + 4f;
                }

                // Settings form
                var settings = Manager.GetOrCreateRuntimeSettings(template.defName);
                float listingWidth = width - 20f;
                Listing_Standard listing = new Listing_Standard();
                listing.Begin(new Rect(10f, y, listingWidth, 999f));

                // autoRetryOnFailure
                bool curAutoRetry = settings.GetAutoRetryOnFailure(template);
                listing.CheckboxLabeled(
                    $"失败自动重试 (XML默认: {(template.autoRetryOnFailure ? "是" : "否")})",
                    ref curAutoRetry,
                    "手术失败后是否自动重新安排手术");
                settings.autoRetryOnFailure = (curAutoRetry == template.autoRetryOnFailure) ? (bool?)null : curAutoRetry;

                // maxRetriesPerStep
                int curMaxRetries = settings.GetMaxRetriesPerStep(template);
                string maxRetBuf = curMaxRetries.ToString();
                Rect maxRetRect = listing.GetRect(30f);
                Widgets.Label(maxRetRect.LeftHalf(), $"每步最大重试次数 (XML默认: {template.maxRetriesPerStep})");
                Widgets.TextFieldNumeric(new Rect(maxRetRect.x + maxRetRect.width / 2f, maxRetRect.y, 80f, 28f),
                    ref curMaxRetries, ref maxRetBuf, 0f, 99f);
                settings.maxRetriesPerStep = (curMaxRetries == template.maxRetriesPerStep) ? (int?)null : curMaxRetries;
                listing.Gap(4f);

                // minColonyWealth
                float curWealth = settings.GetMinColonyWealth(template);
                string wealthBuf = curWealth.ToString("F0");
                Rect wealthRect = listing.GetRect(30f);
                Widgets.Label(wealthRect.LeftHalf(), $"最低殖民地财富 (XML默认: {template.minColonyWealth:F0})");
                Widgets.TextFieldNumeric(new Rect(wealthRect.x + wealthRect.width / 2f, wealthRect.y, 80f, 28f),
                    ref curWealth, ref wealthBuf, 0f, 9999999f);
                settings.minColonyWealth = Mathf.Approximately(curWealth, template.minColonyWealth) ? (float?)null : curWealth;
                listing.Gap(4f);

                // requirePlayerConfirmation
                bool curConfirm = settings.GetRequirePlayerConfirmation(template);
                listing.CheckboxLabeled(
                    $"需要玩家确认 (XML默认: {(template.requirePlayerConfirmation ? "是" : "否")})",
                    ref curConfirm,
                    "开始改造前是否征求玩家确认");
                settings.requirePlayerConfirmation = (curConfirm == template.requirePlayerConfirmation) ? (bool?)null : curConfirm;

                // delayDays
                int curDelay = settings.GetDelayDays(template);
                string delayBuf = curDelay.ToString();
                Rect delayRect = listing.GetRect(30f);
                Widgets.Label(delayRect.LeftHalf(), $"延迟天数 (XML默认: {template.delayDays})");
                Widgets.TextFieldNumeric(new Rect(delayRect.x + delayRect.width / 2f, delayRect.y, 80f, 28f),
                    ref curDelay, ref delayBuf, 0f, 365f);
                settings.delayDays = (curDelay == template.delayDays) ? (int?)null : curDelay;
                listing.Gap(4f);

                // minMedicineCategory - cycle button
                string[] medNames = { "无要求", "草药", "工业药品", "闪耀世界药品" };
                RimWorld.MedicineCategory[] medVals = { RimWorld.MedicineCategory.None, RimWorld.MedicineCategory.Herbal,
                    RimWorld.MedicineCategory.Industrial, RimWorld.MedicineCategory.Glitter };
                RimWorld.MedicineCategory curCat = settings.GetMinMedicineCategory(template);
                int catIdx = Array.IndexOf(medVals, curCat);
                if (catIdx < 0) catIdx = 0;

                Rect medRect = listing.GetRect(30f);
                int xmlCatIdx = Array.IndexOf(medVals, template.minMedicineCategory);
                Widgets.Label(medRect.LeftHalf(),
                    $"最低药品等级 (XML默认: {(xmlCatIdx >= 0 ? medNames[xmlCatIdx] : "?")})");
                if (Widgets.ButtonText(new Rect(medRect.x + medRect.width / 2f, medRect.y, 100f, 28f), medNames[catIdx]))
                {
                    catIdx = (catIdx + 1) % medVals.Length;
                    settings.minMedicineCategory = (medVals[catIdx] == template.minMedicineCategory)
                        ? (RimWorld.MedicineCategory?)null : medVals[catIdx];
                }
                listing.Gap(4f);

                listing.End();
                y += listing.CurHeight + 10f;

                // Reset button
                if (Manager.HasRuntimeOverrides(template.defName))
                {
                    if (Widgets.ButtonText(new Rect(width - 160f, y, 150f, 28f), "恢复XML默认值"))
                        Manager.ResetRuntimeSettings(template.defName);
                    y += 32f;
                }

                // Separator
                GUI.color = Color.grey;
                Widgets.DrawLineHorizontal(0f, y, width);
                GUI.color = Color.white;
                y += 10f;
            }

            cachedHeight = y + 40f;
        }

        // ==================== Action Buttons ====================

        private void DrawActionButtons(float x, float y, Pawn pawn, ColonistModificationTemplateDef template, PawnModificationRecord record)
        {
            float buttonWidth = 90f;
            float gap = 6f;

            if (record == null) return;

            switch (record.status)
            {
                case ModificationStatus.PendingConfirmation:
                    if (Widgets.ButtonText(new Rect(x, y, buttonWidth, 22f), "✓ 确认"))
                        Manager.ConfirmTemplateForPawn(pawn, template);
                    x += buttonWidth + gap;
                    if (Widgets.ButtonText(new Rect(x, y, buttonWidth, 22f), "⏱ 稍后"))
                        Manager.DelayTemplateForPawn(pawn, template);
                    x += buttonWidth + gap;
                    if (Widgets.ButtonText(new Rect(x, y, buttonWidth, 22f), "✗ 忽略"))
                        Manager.DismissTemplateForPawn(pawn, template);
                    break;

                case ModificationStatus.InProgress:
                    GUI.color = Color.green;
                    LabelWithHeight(new Rect(x, y, 200f, 22f), "正在进行中...");
                    GUI.color = Color.white;
                    break;

                case ModificationStatus.Delayed:
                {
                    int remainingTicks = record.delayedUntilTick - Find.TickManager.TicksGame;
                    float remainingDays = remainingTicks / 60000f;
                    LabelWithHeight(new Rect(x, y, 250f, 22f), $"已延迟，约 {remainingDays:F1} 天后重新提示");
                    if (Widgets.ButtonText(new Rect(x + 260f, y, buttonWidth, 22f), "立即开始"))
                        Manager.ConfirmTemplateForPawn(pawn, template);
                    break;
                }

                case ModificationStatus.Dismissed:
                    GUI.color = Color.gray;
                    LabelWithHeight(new Rect(x, y, 200f, 22f), "已忽略");
                    GUI.color = Color.white;
                    if (Widgets.ButtonText(new Rect(x + 210f, y, buttonWidth, 22f), "重新激活"))
                        Manager.ConfirmTemplateForPawn(pawn, template);
                    break;

                case ModificationStatus.Completed:
                    GUI.color = new Color(0.3f, 0.8f, 0.3f);
                    LabelWithHeight(new Rect(x, y, 200f, 22f), "✓ 已全部完成");
                    GUI.color = Color.white;
                    break;

                case ModificationStatus.Idle:
                    if (Widgets.ButtonText(new Rect(x, y, buttonWidth + 20f, 22f), "强制开始"))
                        Manager.ConfirmTemplateForPawn(pawn, template);
                    break;
            }
        }

        // ==================== Helpers ====================

        private void ConfirmAllPending()
        {
            if (Manager == null) return;
            var pendingList = Manager.GetPendingConfirmations();
            foreach (var (pawn, template) in pendingList)
                Manager.ConfirmTemplateForPawn(pawn, template);
            Messages.Message($"已确认 {pendingList.Count} 项制式改造，手术将依次开始。", MessageTypeDefOf.NeutralEvent, false);
        }

        private List<Pawn> GetMatchingPawns(ColonistModificationTemplateDef template)
        {
            var result = new List<Pawn>();
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                foreach (Pawn pawn in map.mapPawns.FreeColonistsAndPrisoners)
                    if (ColonistModificationUtility.PawnMatchesTemplate(pawn, template))
                        result.Add(pawn);
            }
            result.Sort((a, b) => a.LabelShort.CompareTo(b.LabelShort));
            return result;
        }

        private string GetStatusLabel(PawnModificationRecord record)
        {
            if (record == null) return "未评估";
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

        private Color GetStatusColor(PawnModificationRecord record)
        {
            if (record == null) return Color.gray;
            switch (record.status)
            {
                case ModificationStatus.PendingConfirmation: return new Color(1f, 0.84f, 0f);
                case ModificationStatus.InProgress: return Color.green;
                case ModificationStatus.Completed: return new Color(0.3f, 0.8f, 0.3f);
                case ModificationStatus.Dismissed: return Color.gray;
                case ModificationStatus.Delayed: return new Color(0.5f, 0.5f, 1f);
                default: return Color.white;
            }
        }
    }
}

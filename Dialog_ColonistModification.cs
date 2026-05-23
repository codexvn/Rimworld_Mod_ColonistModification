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
        private readonly string[] tabNames = { "模板概览", "待处理列表", "已完成记录", "模板编辑" };
        private Vector2 scrollPosition = Vector2.zero;
        private float cachedHeight = 0f;

        // 模板编辑器状态
        private string editingTemplateId;
        private string newTemplateName = "";
        private Vector2 editorScrollPos = Vector2.zero;

        private ColonistModificationManager Manager => ColonistModificationManager.Instance;
        private List<UserTemplate> AllTemplates =>
            ColonistModificationMod.Instance?.settings?.templates ?? new List<UserTemplate>();

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
                    DrawTemplateEditor(viewRect);
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

            var templates = AllTemplates;
            if (templates.Count == 0)
            {
                LabelWithHeight(new Rect(0f, 0f, rect.width, 30f), "没有定义任何改造模板。请在「模板编辑」中创建。");
                return;
            }

            float y = 0f;
            float width = rect.width;

            foreach (var template in templates)
            {
                if (template.StepCount == 0) continue;

                string headerText = $"{template.name} ({template.StepCount} 个步骤)";
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
                string label = $"殖民者: {pawn.LabelShort}  |  模板: {template.name}";
                float labelH = Text.CalcHeight(label, rect.width * 0.5f);

                int nextStep = record != null ? record.lastCompletedStepIndex + 1 : 0;
                string nextText = "";
                if (nextStep < template.StepCount)
                {
                    var nr = template.GetStep(nextStep);
                    if (nr != null) nextText = $"下一步: {nr.label}";
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
            foreach (var template in AllTemplates)
            {
                foreach (var pawn in GetMatchingPawns(template))
                {
                    var record = Manager.GetRecord(pawn, template);
                    if (record != null && record.status == ModificationStatus.Completed)
                    {
                        foundAny = true;
                        string text = $"殖民者: {pawn.LabelShort}  |  模板: {template.name}  |  状态: ✓ 已完成";
                        float h = LabelWithHeight(new Rect(0f, y, rect.width, 24f), text);
                        y += h + 2f;
                    }
                }
            }

            if (!foundAny)
                LabelWithHeight(new Rect(0f, 0f, rect.width, 30f), "暂无已完成的改造记录。");
            cachedHeight = y + 40f;
        }

        // ==================== Tab 3: 模板编辑 ====================

        private void DrawTemplateEditor(Rect rect)
        {
            var settings = ColonistModificationMod.Instance?.settings;
            if (settings == null)
            {
                LabelWithHeight(new Rect(0f, 0f, rect.width, 30f), "Mod配置未初始化。");
                return;
            }

            var templates = settings.templates;
            float leftWidth = 220f;
            float y = 0f;

            // === 左侧：模板列表 ===
            Widgets.Label(new Rect(0f, y, leftWidth, 24f), "模板列表:");
            y += 26f;

            foreach (var t in templates)
            {
                bool selected = t.id == editingTemplateId;
                if (selected) GUI.color = Color.cyan;
                if (Widgets.ButtonText(new Rect(0f, y, leftWidth - 50f, 24f), t.name, true, false, true))
                {
                    editingTemplateId = t.id;
                    newTemplateName = t.name;
                }
                GUI.color = Color.white;

                if (Widgets.ButtonText(new Rect(leftWidth - 46f, y, 46f, 24f), "✕"))
                {
                    templates.Remove(t);
                    if (editingTemplateId == t.id) editingTemplateId = null;
                    ColonistModificationMod.Instance.WriteSettings();
                    break;
                }
                y += 26f;
            }

            y += 4f;
            if (Widgets.ButtonText(new Rect(0f, y, leftWidth, 26f), "+ 新建模板"))
            {
                var newTpl = new UserTemplate
                {
                    id = Guid.NewGuid().ToString(),
                    name = "新模板"
                };
                templates.Add(newTpl);
                editingTemplateId = newTpl.id;
                newTemplateName = newTpl.name;
                ColonistModificationMod.Instance.WriteSettings();
            }

            // === 右侧：编辑区域 ===
            float rightX = leftWidth + 10f;
            float rightWidth = rect.width - rightX;

            var editingTemplate = templates.FirstOrDefault(t => t.id == editingTemplateId);
            if (editingTemplate == null)
            {
                LabelWithHeight(new Rect(rightX, 0f, rightWidth, 30f), "← 选择或新建一个模板开始编辑");
                cachedHeight = y + 40f;
                return;
            }

            Rect editorRect = new Rect(rightX, 0f, rightWidth, rect.height + 200f);
            Widgets.BeginScrollView(new Rect(rightX, 0f, rightWidth, rect.height), ref editorScrollPos,
                new Rect(0f, 0f, rightWidth - 20f, 900f), true);
            float ey = 0f;
            float ew = rightWidth - 20f;

            // 模板名称
            Widgets.Label(new Rect(0f, ey, 60f, 28f), "名称:");
            newTemplateName = Widgets.TextField(new Rect(65f, ey, ew - 70f, 28f), newTemplateName);
            if (Widgets.ButtonText(new Rect(ew - 65f, ey, 60f, 28f), "保存"))
            {
                editingTemplate.name = newTemplateName;
                ColonistModificationMod.Instance.WriteSettings();
            }
            ey += 34f;

            // === 手术选择（分类复选框）===
            Widgets.Label(new Rect(0f, ey, ew, 26f), "【手术步骤 - 植入物】");
            ey += 28f;

            var implantRecipes = ColonistModificationUtility.GetImplantRecipes();
            var xenogermRecipes = ColonistModificationUtility.GetXenogermRecipes();

            foreach (var recipe in implantRecipes)
            {
                bool has = editingTemplate.recipeDefNames.Contains(recipe.defName);
                bool newVal = has;
                Rect checkRect = new Rect(5f, ey, ew - 10f, 22f);
                Widgets.CheckboxLabeled(checkRect, recipe.label, ref newVal);
                if (newVal != has)
                {
                    if (newVal)
                        editingTemplate.recipeDefNames.Add(recipe.defName);
                    else
                        editingTemplate.recipeDefNames.Remove(recipe.defName);
                    editingTemplate.ResolveReferences();
                    ColonistModificationMod.Instance.WriteSettings();
                }
                ey += 22f;
            }

            // 胚芽
            if (xenogermRecipes.Count > 0)
            {
                ey += 4f;
                Widgets.Label(new Rect(0f, ey, ew, 26f), "【胚芽改造】");
                ey += 28f;

                foreach (var recipe in xenogermRecipes)
                {
                    bool has = editingTemplate.recipeDefNames.Contains(recipe.defName);
                    bool newVal = has;
                    Rect checkRect = new Rect(5f, ey, ew - 10f, 22f);
                    Widgets.CheckboxLabeled(checkRect, recipe.label, ref newVal);
                    if (newVal != has)
                    {
                        if (newVal)
                            editingTemplate.recipeDefNames.Add(recipe.defName);
                        else
                            editingTemplate.recipeDefNames.Remove(recipe.defName);
                        editingTemplate.ResolveReferences();
                        ColonistModificationMod.Instance.WriteSettings();
                    }
                    ey += 22f;
                }
            }

            ey += 8f;

            // === 设置区域 ===
            Widgets.Label(new Rect(0f, ey, ew, 26f), "【参数设置】");
            ey += 28f;

            // 失败自动重试
            bool curAutoRetry = editingTemplate.autoRetryOnFailure;
            Widgets.CheckboxLabeled(new Rect(5f, ey, ew - 10f, 22f), "失败自动重试", ref curAutoRetry);
            if (curAutoRetry != editingTemplate.autoRetryOnFailure)
            {
                editingTemplate.autoRetryOnFailure = curAutoRetry;
                ColonistModificationMod.Instance.WriteSettings();
            }
            ey += 24f;

            // 最大重试次数
            Widgets.Label(new Rect(5f, ey, 120f, 28f), "最大重试次数:");
            int curRetries = editingTemplate.maxRetriesPerStep;
            string retBuf = curRetries.ToString();
            Widgets.TextFieldNumeric(new Rect(130f, ey + 2f, 50f, 24f), ref curRetries, ref retBuf, 0f, 99f);
            if (curRetries != editingTemplate.maxRetriesPerStep)
            {
                editingTemplate.maxRetriesPerStep = curRetries;
                ColonistModificationMod.Instance.WriteSettings();
            }
            ey += 28f;

            // 最低财富
            Widgets.Label(new Rect(5f, ey, 120f, 28f), "最低殖民地财富:");
            float curWealth = editingTemplate.minColonyWealth;
            string wealthBuf = curWealth.ToString("F0");
            Widgets.TextFieldNumeric(new Rect(130f, ey + 2f, 80f, 24f), ref curWealth, ref wealthBuf, 0f, 9999999f);
            if (!Mathf.Approximately(curWealth, editingTemplate.minColonyWealth))
            {
                editingTemplate.minColonyWealth = curWealth;
                ColonistModificationMod.Instance.WriteSettings();
            }
            ey += 28f;

            // 需要玩家确认
            bool curConfirm = editingTemplate.requirePlayerConfirmation;
            Widgets.CheckboxLabeled(new Rect(5f, ey, ew - 10f, 22f), "需要玩家确认", ref curConfirm);
            if (curConfirm != editingTemplate.requirePlayerConfirmation)
            {
                editingTemplate.requirePlayerConfirmation = curConfirm;
                ColonistModificationMod.Instance.WriteSettings();
            }
            ey += 24f;

            // 延迟天数
            Widgets.Label(new Rect(5f, ey, 120f, 28f), "延迟天数:");
            int curDelay = editingTemplate.delayDays;
            string delayBuf = curDelay.ToString();
            Widgets.TextFieldNumeric(new Rect(130f, ey + 2f, 50f, 24f), ref curDelay, ref delayBuf, 0f, 365f);
            if (curDelay != editingTemplate.delayDays)
            {
                editingTemplate.delayDays = curDelay;
                ColonistModificationMod.Instance.WriteSettings();
            }
            ey += 28f;

            // 仅殖民者 / 包含奴隶
            bool curColonists = editingTemplate.colonistsOnly;
            Widgets.CheckboxLabeled(new Rect(5f, ey, ew - 10f, 22f), "仅殖民者", ref curColonists);
            if (curColonists != editingTemplate.colonistsOnly)
            {
                editingTemplate.colonistsOnly = curColonists;
                ColonistModificationMod.Instance.WriteSettings();
            }
            ey += 24f;

            bool curSlaves = editingTemplate.includeSlaves;
            Widgets.CheckboxLabeled(new Rect(5f, ey, ew - 10f, 22f), "包含奴隶", ref curSlaves);
            if (curSlaves != editingTemplate.includeSlaves)
            {
                editingTemplate.includeSlaves = curSlaves;
                ColonistModificationMod.Instance.WriteSettings();
            }
            ey += 28f;

            // 药品等级（带图标下拉）
            Widgets.Label(new Rect(5f, ey, 120f, 28f), "最低药品等级:");
            DrawMedicineDropdown(new Rect(130f, ey + 2f, 140f, 24f), editingTemplate);
            ey += 30f;

            Widgets.EndScrollView();
            cachedHeight = y + 40f;
        }

        /// <summary>
        /// 药品下拉框，按钮显示当前药品图标+名称，点击弹出 FloatMenu。
        /// </summary>
        private void DrawMedicineDropdown(Rect buttonRect, UserTemplate template)
        {
            var medDefs = new List<ThingDef> { null, ThingDefOf.MedicineHerbal, ThingDefOf.MedicineIndustrial, ThingDefOf.MedicineUltratech };
            var medNames = new string[] { "无要求", "草药", "工业药品", "闪耀世界药品" };

            int curIdx = (int)template.minMedicineCategory;
            if (curIdx < 0 || curIdx >= medDefs.Count) curIdx = 2; // default Industrial

            ThingDef curDef = medDefs[curIdx];

            // 按钮：图标+名称
            Rect iconRect = new Rect(buttonRect.x, buttonRect.y, 24f, 24f);
            if (curDef != null)
                Widgets.ThingIcon(iconRect, curDef);
            else
                GUI.DrawTexture(iconRect, BaseContent.GreyTex);

            Rect labelRect = new Rect(buttonRect.x + 26f, buttonRect.y, buttonRect.width - 26f, 24f);
            if (Widgets.ButtonText(labelRect, medNames[curIdx]))
            {
                var options = new List<FloatMenuOption>();
                for (int i = 0; i < medDefs.Count; i++)
                {
                    int idx = i;
                    ThingDef def = medDefs[i];
                    options.Add(new FloatMenuOption(medNames[i], () =>
                    {
                        template.minMedicineCategory = (MedicineCategory)idx;
                        ColonistModificationMod.Instance.WriteSettings();
                    }, def));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        // ==================== Action Buttons ====================

        private void DrawActionButtons(float x, float y, Pawn pawn, UserTemplate template, PawnModificationRecord record)
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

        private List<Pawn> GetMatchingPawns(UserTemplate template)
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

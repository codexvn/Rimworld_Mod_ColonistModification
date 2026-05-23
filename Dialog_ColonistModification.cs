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

        private string editingTemplateId;
        private string newTemplateName = "";
        private int editorSubTab = 0;

        private ColonistModificationManager Manager => ColonistModificationManager.Instance;
        private List<UserTemplate> AllTemplates
        {
            get
            {
                var s = ColonistModificationMod.Instance?.settings;
                return s?.templates ?? new List<UserTemplate>();
            }
        }

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

        public override Vector2 InitialSize => new Vector2(950f, 700f);

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

            Rect contentRect = new Rect(0f, 75f, inRect.width - 16f, inRect.height - 115f);
            Rect viewRect = new Rect(0f, 0f, contentRect.width - 20f, Mathf.Max(contentRect.height, cachedHeight));
            Widgets.BeginScrollView(contentRect, ref scrollPosition, viewRect, true);

            switch (selectedTab)
            {
                case 0: DrawTemplateOverview(viewRect); break;
                case 1: DrawPendingList(viewRect); break;
                case 2: DrawCompletedList(viewRect); break;
                case 3: DrawTemplateEditor(viewRect); break;
            }

            Widgets.EndScrollView();

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

        // ==================== Tab 0: 模板概览（按人分配模板） ====================

        private void DrawTemplateOverview(Rect rect)
        {
            if (Manager == null)
            {
                LabelWithHeight(new Rect(0f, 0f, rect.width, 30f), "改造管理器未初始化。");
                return;
            }

            var templates = AllTemplates;
            if (templates.Count == 0)
            {
                LabelWithHeight(new Rect(0f, 0f, rect.width, 30f), "没有模板，请在「模板编辑」中创建。");
                cachedHeight = 30f;
                return;
            }

            float y = 0f;
            float width = rect.width;

            // Column headers
            Widgets.Label(new Rect(0f, y, 200f, 24f), "殖民者");
            Widgets.Label(new Rect(210f, y, 200f, 24f), "分配模板");
            Widgets.Label(new Rect(420f, y, 300f, 24f), "状态");
            y += 26f;

            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;

                foreach (Pawn pawn in map.mapPawns.FreeColonistsAndPrisoners)
                {
                    string assignedId = Manager.GetAssignedTemplateId(pawn.thingIDNumber);
                    UserTemplate assigned = assignedId != null ? AllTemplates.FirstOrDefault(t => t.id == assignedId) : null;
                    var record = assigned != null ? Manager.GetRecord(pawn, assigned) : null;

                    // Row background
                    if (record != null && record.status == ModificationStatus.PendingConfirmation)
                        Widgets.DrawBoxSolid(new Rect(0f, y, width, 24f), new Color(1f, 0.92f, 0.016f, 0.1f));

                    // Name
                    Widgets.Label(new Rect(0f, y + 2f, 200f, 20f), pawn.LabelShort);

                    // Template dropdown
                    string selName = assigned?.name ?? "无";
                    if (Widgets.ButtonText(new Rect(210f, y, 200f, 22f), selName))
                    {
                        var opts = new List<FloatMenuOption>();
                        opts.Add(new FloatMenuOption("无 (不改造)", () =>
                        {
                            Manager.UnassignTemplate(pawn.thingIDNumber);
                        }));
                        foreach (var t in AllTemplates)
                        {
                            var captured = t;
                            opts.Add(new FloatMenuOption(captured.name, () =>
                            {
                                Manager.AssignTemplate(pawn.thingIDNumber, captured.id);
                                captured.ResolveReferences();
                            }));
                        }
                        Find.WindowStack.Add(new FloatMenu(opts));
                    }

                    // Status & reasons
                    if (record != null && assigned != null)
                    {
                        string statusText = GetStatusLabel(record);
                        int completed = record.completedRecipeDefNames?.Count ?? 0;
                        string fullText = $"{statusText} ({completed}/{assigned.StepCount})";

                        if (record.status == ModificationStatus.Idle && !string.IsNullOrEmpty(record.conditionFailReason))
                            fullText += $"\n{record.conditionFailReason.TrimEnd()}";

                        GUI.color = GetStatusColor(record);
                        float h = LabelWithHeight(new Rect(420f, y + 2f, 300f, 20f), fullText);
                        GUI.color = Color.white;
                    }

                    y += Mathf.Max(24f, Text.CalcHeight(GetStatusLabel(record), 300f)) + 4f;
                }
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

            var pending = Manager.GetPendingConfirmations();
            if (!pending.Any())
            {
                LabelWithHeight(new Rect(0f, 0f, rect.width, 30f), "当前没有等待确认的改造项目。");
                cachedHeight = 30f;
                return;
            }

            float y = 0f;
            float w = rect.width;
            foreach (var (pawn, template) in pending)
            {
                var record = Manager.GetRecord(pawn, template);
                string label = $"殖民者: {pawn.LabelShort}  |  模板: {template.name}";
                float lh = Text.CalcHeight(label, w * 0.6f);

                Rect cardRect = new Rect(0f, y, w, lh + 8f);
                Widgets.DrawBoxSolid(cardRect, new Color(0.25f, 0.25f, 0.1f, 0.4f));
                Widgets.Label(new Rect(4f, y + 4f, w * 0.6f, lh), label);
                y += cardRect.height + 2f;
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
            bool found = false;
            foreach (var template in AllTemplates)
            {
                foreach (Map map in Find.Maps)
                {
                    if (!map.IsPlayerHome) continue;
                    foreach (Pawn pawn in map.mapPawns.FreeColonistsAndPrisoners)
                    {
                        var record = Manager.GetRecord(pawn, template);
                        if (record != null && record.status == ModificationStatus.Completed)
                        {
                            found = true;
                            string t = $"殖民者: {pawn.LabelShort}  |  模板: {template.name}  |  ✓ 已完成";
                            float h = LabelWithHeight(new Rect(0f, y, rect.width, 24f), t);
                            y += h + 2f;
                        }
                    }
                }
            }

            if (!found)
                LabelWithHeight(new Rect(0f, 0f, rect.width, 30f), "暂无已完成的改造记录。");
            cachedHeight = y + 40f;
        }

        // ==================== Tab 3: 模板编辑（无嵌套滚动） ====================

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
                var nt = new UserTemplate { id = Guid.NewGuid().ToString(), name = "新模板" };
                templates.Add(nt);
                editingTemplateId = nt.id;
                newTemplateName = nt.name;
                ColonistModificationMod.Instance.WriteSettings();
            }

            float leftEndY = y;

            // === 右侧：编辑区 ===
            float rightX = leftWidth + 10f;
            float rw = rect.width - rightX;
            float ry = 0f;

            var editTemplate = templates.FirstOrDefault(t => t.id == editingTemplateId);
            if (editTemplate == null)
            {
                LabelWithHeight(new Rect(rightX, ry, rw, 30f), "← 选择或新建一个模板开始编辑");
                cachedHeight = Mathf.Max(leftEndY, ry + 30f) + 40f;
                return;
            }

            // 模板名称
            Widgets.Label(new Rect(rightX, ry, 60f, 28f), "名称:");
            newTemplateName = Widgets.TextField(new Rect(rightX + 65f, ry, rw - 130f, 28f), newTemplateName);
            if (Widgets.ButtonText(new Rect(rightX + rw - 60f, ry, 60f, 28f), "保存"))
            {
                editTemplate.name = newTemplateName;
                ColonistModificationMod.Instance.WriteSettings();
            }
            ry += 34f;

            // 种族筛选：只列出可手术的人类like种族（有身体部位才可手术）
            // 只列出手术菜单中有"添加清单"按钮的种族（recipeUsers中包含该种族的才算可手术）
            var bodyDefs = DefDatabase<ThingDef>.AllDefs
                .Where(t => t.race != null && t.race.Humanlike && t.race.body != null
                    && t.AllRecipes.Any(r => r.IsSurgery))
                .Select(t => t.race.body).Distinct().ToList();
            BodyDef selBody = bodyDefs.FirstOrDefault(b => b.defName == (editTemplate.targetBodyDefName ?? "Human")) ?? BodyDefOf.Human;
            Widgets.Label(new Rect(rightX, ry, 80f, 26f), "种族筛选:");
            if (Widgets.ButtonText(new Rect(rightX + 85f, ry, 150f, 26f), selBody.LabelCap))
            {
                var opts = new List<FloatMenuOption>();
                foreach (var b in bodyDefs)
                {
                    var c = b;
                    opts.Add(new FloatMenuOption(c.LabelCap, () =>
                    {
                        editTemplate.targetBodyDefName = c.defName == "Human" ? null : c.defName;
                        ColonistModificationMod.Instance.WriteSettings();
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            ry += 30f;

            // === 子标签：植入物 / 基因植入 ===
            string[] subTabs = { "植入物", "基因植入" };
            float subTabW = 90f;
            for (int si = 0; si < subTabs.Length; si++)
            {
                Rect sr = new Rect(rightX + si * (subTabW + 4f), ry, subTabW, 24f);
                if (si == editorSubTab) GUI.color = Color.cyan;
                if (Widgets.ButtonText(sr, subTabs[si], true, false, true)) editorSubTab = si;
                GUI.color = Color.white;
            }
            ry += 30f;

            if (editorSubTab == 0) // 植入物
            {
                var grouped = ColonistModificationUtility.GetImplantRecipesByGroup(selBody);
                foreach (var kvp in grouped)
                {
                    Widgets.Label(new Rect(rightX, ry, rw, 24f), $"◼ {kvp.Key}");
                    ry += 24f;

                    foreach (var recipe in kvp.Value)
                    {
                        bool has = editTemplate.recipeDefNames.Contains(recipe.defName);
                        bool newVal = has;
                        Widgets.CheckboxLabeled(new Rect(rightX + 15f, ry, rw - 15f, 22f), recipe.label, ref newVal);
                        if (newVal != has)
                        {
                            if (newVal) editTemplate.recipeDefNames.Add(recipe.defName);
                            else editTemplate.recipeDefNames.Remove(recipe.defName);
                            editTemplate.ResolveReferences();
                            ColonistModificationMod.Instance.WriteSettings();
                        }
                        ry += 22f;
                    }
                    ry += 4f;
                }
            }
            else // 基因植入
            {
                var xenotypeDefs = DefDatabase<XenotypeDef>.AllDefs.ToList();
                bool hasGene = !string.IsNullOrEmpty(editTemplate.xenogermTargetXenotypeDefName);
                Widgets.CheckboxLabeled(new Rect(rightX, ry, rw, 22f), "启用基因植入", ref hasGene);
                if (hasGene != !string.IsNullOrEmpty(editTemplate.xenogermTargetXenotypeDefName))
                {
                    editTemplate.xenogermTargetXenotypeDefName = hasGene ? xenotypeDefs.FirstOrDefault()?.defName : null;
                    ColonistModificationMod.Instance.WriteSettings();
                }
                ry += 26f;

                if (hasGene && xenotypeDefs.Count > 0)
                {
                    Widgets.Label(new Rect(rightX + 15f, ry, 80f, 26f), "目标异种:");
                    var sel = xenotypeDefs.FirstOrDefault(x => x.defName == editTemplate.xenogermTargetXenotypeDefName) ?? xenotypeDefs[0];
                    if (Widgets.ButtonText(new Rect(rightX + 95f, ry, 150f, 26f), sel.label))
                    {
                        var opts = new List<FloatMenuOption>();
                        foreach (var xd in xenotypeDefs)
                        {
                            var c = xd;
                            opts.Add(new FloatMenuOption(c.label, () =>
                            {
                                editTemplate.xenogermTargetXenotypeDefName = c.defName;
                                ColonistModificationMod.Instance.WriteSettings();
                            }));
                        }
                        Find.WindowStack.Add(new FloatMenu(opts));
                    }
                    ry += 30f;

                    GUI.color = Color.grey;
                    Widgets.Label(new Rect(rightX + 15f, ry, rw - 15f, 20f), "手术时将查找地图上匹配的异种胚，需提前在基因装配器制作。");
                    GUI.color = Color.white;
                    ry += 22f;
                }
            }

            ry += 8f;

            // === 设置区域 ===
            Widgets.Label(new Rect(rightX, ry, rw, 26f), "【参数设置】");
            ry += 28f;

            // 失败自动重试
            bool ar = editTemplate.autoRetryOnFailure;
            Widgets.CheckboxLabeled(new Rect(rightX + 5f, ry, rw - 5f, 22f), "失败自动重试", ref ar);
            if (ar != editTemplate.autoRetryOnFailure) { editTemplate.autoRetryOnFailure = ar; ColonistModificationMod.Instance.WriteSettings(); }
            ry += 24f;

            // 最大重试
            Widgets.Label(new Rect(rightX + 5f, ry, 120f, 28f), "最大重试次数:");
            int cr = editTemplate.maxRetriesPerStep;
            string crb = cr.ToString();
            Widgets.TextFieldNumeric(new Rect(rightX + 130f, ry + 2f, 50f, 24f), ref cr, ref crb, 0f, 99f);
            if (cr != editTemplate.maxRetriesPerStep) { editTemplate.maxRetriesPerStep = cr; ColonistModificationMod.Instance.WriteSettings(); }
            ry += 28f;

            // 最低财富
            Widgets.Label(new Rect(rightX + 5f, ry, 120f, 28f), "最低殖民地财富:");
            float cw = editTemplate.minColonyWealth;
            string cwb = cw.ToString("F0");
            Widgets.TextFieldNumeric(new Rect(rightX + 130f, ry + 2f, 80f, 24f), ref cw, ref cwb, 0f, 9999999f);
            if (!Mathf.Approximately(cw, editTemplate.minColonyWealth)) { editTemplate.minColonyWealth = cw; ColonistModificationMod.Instance.WriteSettings(); }
            ry += 28f;

            // 需要确认
            bool cf = editTemplate.requirePlayerConfirmation;
            Widgets.CheckboxLabeled(new Rect(rightX + 5f, ry, rw - 5f, 22f), "需要玩家确认", ref cf);
            if (cf != editTemplate.requirePlayerConfirmation) { editTemplate.requirePlayerConfirmation = cf; ColonistModificationMod.Instance.WriteSettings(); }
            ry += 24f;

            // 延迟天数
            Widgets.Label(new Rect(rightX + 5f, ry, 120f, 28f), "延迟天数:");
            int cd = editTemplate.delayDays;
            string cdb = cd.ToString();
            Widgets.TextFieldNumeric(new Rect(rightX + 130f, ry + 2f, 50f, 24f), ref cd, ref cdb, 0f, 365f);
            if (cd != editTemplate.delayDays) { editTemplate.delayDays = cd; ColonistModificationMod.Instance.WriteSettings(); }
            ry += 28f;

            // 仅殖民者/奴隶
            bool cc = editTemplate.colonistsOnly;
            Widgets.CheckboxLabeled(new Rect(rightX + 5f, ry, rw - 5f, 22f), "仅殖民者", ref cc);
            if (cc != editTemplate.colonistsOnly) { editTemplate.colonistsOnly = cc; ColonistModificationMod.Instance.WriteSettings(); }
            ry += 24f;

            bool cs = editTemplate.includeSlaves;
            Widgets.CheckboxLabeled(new Rect(rightX + 5f, ry, rw - 5f, 22f), "包含奴隶", ref cs);
            if (cs != editTemplate.includeSlaves) { editTemplate.includeSlaves = cs; ColonistModificationMod.Instance.WriteSettings(); }
            ry += 28f;

            // 药品等级
            DrawMedicineDropdown(new Rect(rightX + 5f, ry, 140f, 24f), editTemplate);
            ry += 30f;

            cachedHeight = ry + 80f;
        }

        private void DrawMedicineDropdown(Rect buttonRect, UserTemplate template)
        {
            var medDefs = new List<ThingDef> { null, ThingDefOf.MedicineHerbal, ThingDefOf.MedicineIndustrial, ThingDefOf.MedicineUltratech };
            var medNames = new string[] { "无要求", "草药", "工业药品", "闪耀世界药品" };
            int curIdx = (int)template.minMedicineCategory;
            if (curIdx < 0 || curIdx >= medDefs.Count) curIdx = 2;
            ThingDef curDef = medDefs[curIdx];
            Rect iconRect = new Rect(buttonRect.x, buttonRect.y, 24f, 24f);
            if (curDef != null) Widgets.ThingIcon(iconRect, curDef);
            else GUI.DrawTexture(iconRect, BaseContent.GreyTex);
            if (Widgets.ButtonText(new Rect(buttonRect.x + 26f, buttonRect.y, buttonRect.width - 26f, 24f), medNames[curIdx]))
            {
                var opts = new List<FloatMenuOption>();
                for (int i = 0; i < medDefs.Count; i++)
                {
                    int idx = i;
                    ThingDef d = medDefs[i];
                    opts.Add(new FloatMenuOption(medNames[i], () =>
                    {
                        template.minMedicineCategory = (MedicineCategory)idx;
                        ColonistModificationMod.Instance.WriteSettings();
                    }, d));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }
        }

        // ==================== Action Buttons ====================

        private void DrawActionButtons(float x, float y, Pawn pawn, UserTemplate template, PawnModificationRecord record)
        {
            float bw = 90f; float gap = 6f;
            if (record == null) return;
            switch (record.status)
            {
                case ModificationStatus.PendingConfirmation:
                    if (Widgets.ButtonText(new Rect(x, y, bw, 22f), "✓ 确认")) Manager.ConfirmTemplateForPawn(pawn, template);
                    x += bw + gap;
                    if (Widgets.ButtonText(new Rect(x, y, bw, 22f), "⏱ 稍后")) Manager.DelayTemplateForPawn(pawn, template);
                    x += bw + gap;
                    if (Widgets.ButtonText(new Rect(x, y, bw, 22f), "✗ 忽略")) Manager.DismissTemplateForPawn(pawn, template);
                    break;
                case ModificationStatus.InProgress:
                    GUI.color = Color.green; LabelWithHeight(new Rect(x, y, 200f, 22f), "▶ 进行中..."); GUI.color = Color.white; break;
                case ModificationStatus.Delayed:
                {
                    int rt = record.delayedUntilTick - Find.TickManager.TicksGame;
                    LabelWithHeight(new Rect(x, y, 250f, 22f), $"已延迟，约 {rt / 60000f:F1} 天后提示");
                    if (Widgets.ButtonText(new Rect(x + 260f, y, bw, 22f), "立即开始")) Manager.ConfirmTemplateForPawn(pawn, template);
                    break;
                }
                case ModificationStatus.Dismissed:
                    GUI.color = Color.gray; LabelWithHeight(new Rect(x, y, 200f, 22f), "已忽略"); GUI.color = Color.white;
                    if (Widgets.ButtonText(new Rect(x + 210f, y, bw, 22f), "重新激活")) Manager.ConfirmTemplateForPawn(pawn, template);
                    break;
                case ModificationStatus.Completed:
                    GUI.color = new Color(0.3f, 0.8f, 0.3f); LabelWithHeight(new Rect(x, y, 200f, 22f), "✓ 已完成"); GUI.color = Color.white; break;
                case ModificationStatus.Idle:
                    if (Widgets.ButtonText(new Rect(x, y, bw + 20f, 22f), "强制开始")) Manager.ConfirmTemplateForPawn(pawn, template);
                    break;
            }
        }

        // ==================== Helpers ====================

        private void ConfirmAllPending()
        {
            if (Manager == null) return;
            var list = Manager.GetPendingConfirmations();
            foreach (var (pawn, template) in list) Manager.ConfirmTemplateForPawn(pawn, template);
            Messages.Message($"已确认 {list.Count} 项改造，手术将依次开始。", MessageTypeDefOf.NeutralEvent, false);
        }

        private string GetStatusLabel(PawnModificationRecord record)
        {
            if (record == null) return "未分配";
            switch (record.status)
            {
                case ModificationStatus.Idle: return "⏳ 条件不满足";
                case ModificationStatus.PendingConfirmation: return "⚡ 等待确认";
                case ModificationStatus.InProgress: return "▶ 进行中";
                case ModificationStatus.Completed: return "✓ 已完成";
                case ModificationStatus.Dismissed: return "✗ 已忽略";
                case ModificationStatus.Delayed: return "⏱ 已延迟";
                default: return "?";
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

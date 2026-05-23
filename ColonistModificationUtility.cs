using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ColonistModification
{
    public static class ColonistModificationUtility
    {
        public static bool PawnMatchesTemplate(Pawn pawn, UserTemplate template)
        {
            if (pawn == null || template == null) return false;
            if (!pawn.RaceProps.Humanlike) return false;
            if (template.colonistsOnly && !pawn.IsColonist) return false;
            if (!template.includeSlaves && pawn.IsSlave) return false;
            return true;
        }

        /// <summary>
        /// 检查手术条件，返回 (是否满足, 失败原因)。
        /// </summary>
        public static (bool can, string reason) CheckSurgeryConditions(Pawn pawn, RecipeDef recipe, Map map)
        {
            if (pawn == null || recipe == null || map == null)
                return (false, "无效参数");
            if (pawn.Dead)
                return (false, "殖民者已死亡");
            if (!pawn.Spawned)
                return (false, "殖民者不在当前地图");
            if (recipe.Worker == null)
                return (false, "手术定义无效");

            var parts = recipe.Worker.GetPartsToApplyOn(pawn, recipe);
            if (parts == null || !parts.Any())
                return (false, "无可用的身体部位");

            if (!recipe.Worker.AvailableOnNow(pawn, null))
                return (false, "手术当前不可用");

            if (!HasAvailableSurgeon(recipe, map))
                return (false, "无可用医生（需满足技能且非倒地/失控）");

            if (!HasRequiredMedicine(recipe, map, pawn))
                return (false, "缺少所需药品");

            if (!HasRequiredMaterials(recipe, map))
                return (false, "缺少手术所需材料");

            return (true, null);
        }

        /// <summary>保留旧接口兼容</summary>
        public static bool CanPerformSurgery(Pawn pawn, RecipeDef recipe, Map map)
            => CheckSurgeryConditions(pawn, recipe, map).can;

        public static bool HasAvailableSurgeon(RecipeDef recipe, Map map)
        {
            if (recipe.workSkill == null) return true;
            foreach (Pawn pawn in map.mapPawns.FreeColonists)
            {
                if (pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) &&
                    !pawn.Downed && !pawn.InMentalState &&
                    pawn.skills.GetSkill(recipe.workSkill) != null)
                    return true;
            }
            return false;
        }

        public static bool HasRequiredMedicine(RecipeDef recipe, Map map, Pawn patient)
        {
            // 如果配方需要药物，检查是否有足够药物
            if (recipe.ingredients == null) return true;
            foreach (var ing in recipe.ingredients)
            {
                if (ing.filter.Allows(ThingDefOf.MedicineHerbal) ||
                    ing.filter.Allows(ThingDefOf.MedicineIndustrial) ||
                    ing.filter.Allows(ThingDefOf.MedicineUltratech))
                {
                    var best = GetBestAvailableMedicine(map);
                    if (best == null) return false;
                }
            }
            return true;
        }

        public static ThingDef GetBestAvailableMedicine(Map map)
        {
            var candidates = new List<ThingDef>
            {
                ThingDefOf.MedicineUltratech,
                ThingDefOf.MedicineIndustrial,
                ThingDefOf.MedicineHerbal
            };

            foreach (var medDef in candidates)
            {
                if (medDef == null) continue;
                var medicines = map.listerThings.ThingsOfDef(medDef);
                foreach (Thing med in medicines)
                {
                    if (!med.IsForbidden(Faction.OfPlayer) && med.stackCount > 0)
                        return medDef;
                }
            }
            return null;
        }

        public static bool HasRequiredMaterials(RecipeDef recipe, Map map)
        {
            if (recipe.ingredients == null || recipe.ingredients.Count == 0) return true;
            foreach (var ingredient in recipe.ingredients)
            {
                if (ingredient.filter.Allows(ThingDefOf.MedicineHerbal) ||
                    ingredient.filter.Allows(ThingDefOf.MedicineIndustrial) ||
                    ingredient.filter.Allows(ThingDefOf.MedicineUltratech))
                    continue;

                if (!HasEnoughIngredient(ingredient, map))
                    return false;
            }
            return true;
        }

        private static bool HasEnoughIngredient(IngredientCount ingredient, Map map)
        {
            float required = ingredient.GetBaseCount();
            float found = 0f;
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (ingredient.filter.Allows(thing) && !thing.IsForbidden(Faction.OfPlayer))
                {
                    found += thing.stackCount;
                    if (found >= required) return true;
                }
            }
            return false;
        }

        public static Bill_ColonistModification CreateBillForStep(
            RecipeDef recipe, Pawn patient, UserTemplate template, int stepIndex, int retryCount = 0)
        {
            Bill_ColonistModification bill = new Bill_ColonistModification(recipe);
            bill.templateId = template.id;
            bill.template = template;
            bill.currentStepIndex = stepIndex;
            bill.retryCount = retryCount;
            bill.SetPawnRestriction(patient);

            if (recipe.targetsBodyPart)
            {
                var parts = recipe.Worker.GetPartsToApplyOn(patient, recipe);
                var bestPart = parts?.FirstOrDefault();
                if (bestPart != null) bill.Part = bestPart;
            }

            if (recipe.defName == "ImplantXenogerm" && ModsConfig.BiotechActive)
                TryBindXenogerm(bill, patient);

            return bill;
        }

        private static void TryBindXenogerm(Bill_ColonistModification bill, Pawn patient)
        {
            var map = patient.Map;
            if (map == null) return;

            foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.Xenogerm))
            {
                Xenogerm xenogerm = thing as Xenogerm;
                if (xenogerm != null && !xenogerm.IsForbidden(Faction.OfPlayer) && !xenogerm.Position.Fogged(map))
                {
                    bill.xenogerm = xenogerm;
                    return;
                }
            }
        }

        public static bool HasCompletedTemplate(Pawn pawn, UserTemplate template)
        {
            if (pawn == null || template == null) return false;
            var record = ColonistModificationManager.Instance?.GetRecord(pawn, template);
            if (record != null && record.completedRecipeDefNames.Count >= template.StepCount)
                return true;
            // Fallback: check hediffs
            foreach (var recipe in template.resolvedRecipes)
            {
                if (record != null && record.completedRecipeDefNames.Contains(recipe.defName))
                    continue;
                if (recipe.addsHediff != null && !pawn.health.hediffSet.HasHediff(recipe.addsHediff))
                    return false;
            }
            return true;
        }

        public static int GetNextStepIndex(Pawn pawn, UserTemplate template)
        {
            var record = ColonistModificationManager.Instance?.GetRecord(pawn, template);
            for (int i = 0; i < template.resolvedRecipes.Count; i++)
            {
                var recipe = template.resolvedRecipes[i];
                if (record != null && record.completedRecipeDefNames.Contains(recipe.defName))
                    continue;
                return i;
            }
            return -1;
        }

        public static Pawn FindBestSurgeon(Map map, RecipeDef recipe, Pawn patient)
        {
            Pawn bestSurgeon = null;
            float bestSkill = -1f;

            foreach (Pawn pawn in map.mapPawns.FreeColonists)
            {
                if (pawn == patient) continue;
                if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) ||
                    pawn.Downed || pawn.InMentalState) continue;

                float skillLevel = 0f;
                if (recipe.workSkill != null && pawn.skills != null)
                    skillLevel = pawn.skills.GetSkill(recipe.workSkill).Level;

                if (skillLevel > bestSkill)
                {
                    bestSkill = skillLevel;
                    bestSurgeon = pawn;
                }
            }
            return bestSurgeon;
        }

        /// <summary>
        /// 动态获取所有植入物配方，按身体部位分组。
        /// 通过 RecipeWorker 类型筛选（而非 defName 前缀），mod 添加的植入物自动出现。
        /// 返回：部位组标签 -> 该组下的配方列表。
        /// </summary>
        public static Dictionary<string, List<RecipeDef>> GetImplantRecipesByGroup()
        {
            var result = new Dictionary<string, List<RecipeDef>>();
            foreach (var recipe in DefDatabase<RecipeDef>.AllDefs)
            {
                if (!recipe.targetsBodyPart || recipe.addsHediff == null) continue;
                if (!typeof(Recipe_InstallArtificialBodyPart).IsAssignableFrom(recipe.workerClass) &&
                    !typeof(Recipe_InstallNaturalBodyPart).IsAssignableFrom(recipe.workerClass)) continue;

                string groupName = recipe.appliedOnFixedBodyPartGroups?.FirstOrDefault()?.LabelCap
                    ?? recipe.appliedOnFixedBodyParts?.FirstOrDefault()?.LabelCap
                    ?? "其他";
                if (!result.ContainsKey(groupName))
                    result[groupName] = new List<RecipeDef>();
                result[groupName].Add(recipe);
            }

            // 每个组内按标签排序
            foreach (var list in result.Values)
                list.Sort((a, b) => a.label.CompareTo(b.label));

            return result;
        }

        public static List<RecipeDef> GetXenogermRecipes()
        {
            var result = new List<RecipeDef>();
            var implant = DefDatabase<RecipeDef>.GetNamedSilentFail("ImplantXenogerm");
            if (implant != null) result.Add(implant);
            return result;
        }
    }
}

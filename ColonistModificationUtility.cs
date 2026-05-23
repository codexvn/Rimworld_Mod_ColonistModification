using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ColonistModification
{
    public static class ColonistModificationUtility
    {
        public static (bool can, string reason) CheckSurgeryConditions(Pawn pawn, RecipeDef recipe, Map map,
            MedicineCategory minMedicineCategory = MedicineCategory.None)
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

            if (!HasRequiredMedicine(recipe, map, minMedicineCategory))
                return (false, "缺少所需药品");

            if (!HasRequiredMaterials(recipe, map))
                return (false, "缺少手术所需材料");

            return (true, null);
        }

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

        public static bool HasRequiredMedicine(RecipeDef recipe, Map map, MedicineCategory minCategory = MedicineCategory.None)
        {
            if (recipe.ingredients == null) return true;

            bool needsMedicine = false;
            foreach (var ing in recipe.ingredients)
            {
                if (ing.filter.Allows(ThingDefOf.MedicineHerbal) ||
                    ing.filter.Allows(ThingDefOf.MedicineIndustrial) ||
                    ing.filter.Allows(ThingDefOf.MedicineUltratech))
                {
                    needsMedicine = true;
                    break;
                }
            }
            if (!needsMedicine) return true;

            var candidates = new[] { ThingDefOf.MedicineUltratech, ThingDefOf.MedicineIndustrial, ThingDefOf.MedicineHerbal };
            foreach (var medDef in candidates)
            {
                if (medDef == null) continue;
                if (GetMedicineCategory(medDef) < minCategory) continue;

                var medicines = map.listerThings.ThingsOfDef(medDef);
                foreach (Thing med in medicines)
                {
                    if (!med.IsForbidden(Faction.OfPlayer) && med.stackCount > 0)
                        return true;
                }
            }
            return false;
        }

        private static MedicineCategory GetMedicineCategory(ThingDef medDef)
        {
            if (medDef == ThingDefOf.MedicineUltratech) return MedicineCategory.Glitter;
            if (medDef == ThingDefOf.MedicineIndustrial) return MedicineCategory.Industrial;
            if (medDef == ThingDefOf.MedicineHerbal) return MedicineCategory.Herbal;
            return MedicineCategory.None;
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

            string targetXenotypeDefName = bill.template?.xenogermTargetXenotypeDefName;
            XenotypeDef targetXenotype = null;
            if (!string.IsNullOrEmpty(targetXenotypeDefName))
                targetXenotype = DefDatabase<XenotypeDef>.GetNamedSilentFail(targetXenotypeDefName);

            foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.Xenogerm))
            {
                Xenogerm xenogerm = thing as Xenogerm;
                if (xenogerm == null || xenogerm.IsForbidden(Faction.OfPlayer) || xenogerm.Position.Fogged(map))
                    continue;
                if (targetXenotype != null && xenogerm.xenotypeName != targetXenotype.label)
                    continue;
                bill.xenogerm = xenogerm;
                return;
            }
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

        public static Dictionary<string, List<RecipeDef>> GetImplantRecipesByGroup(BodyDef bodyDef = null)
        {
            var body = bodyDef ?? BodyDefOf.Human;
            var result = new Dictionary<string, List<RecipeDef>>();
            var groupToPartLabels = new Dictionary<BodyPartGroupDef, HashSet<string>>();

            foreach (var recipe in DefDatabase<RecipeDef>.AllDefs)
            {
                if (!recipe.targetsBodyPart || recipe.addsHediff == null) continue;
                if (!typeof(Recipe_InstallArtificialBodyPart).IsAssignableFrom(recipe.workerClass) &&
                    !typeof(Recipe_InstallNaturalBodyPart).IsAssignableFrom(recipe.workerClass)) continue;

                var groupNames = new List<string>();

                if (recipe.appliedOnFixedBodyPartGroups != null && recipe.appliedOnFixedBodyPartGroups.Count > 0)
                {
                    var g = recipe.appliedOnFixedBodyPartGroups[0];
                    if (!groupToPartLabels.TryGetValue(g, out var labels))
                    {
                        labels = new HashSet<string>();
                        foreach (var part in body.AllParts)
                        {
                            if (part.groups != null && part.groups.Contains(g))
                                labels.Add(part.LabelCap);
                        }
                        groupToPartLabels[g] = labels;
                    }
                    groupNames.AddRange(labels);
                }

                if (recipe.appliedOnFixedBodyParts != null)
                {
                    foreach (var p in recipe.appliedOnFixedBodyParts)
                    {
                        var parts = body.GetPartsWithDef(p);
                        if (parts.Count > 0)
                        {
                            foreach (var part in parts)
                                groupNames.Add(part.LabelCap);
                        }
                        else
                        {
                            groupNames.Add(p.LabelCap);
                        }
                    }
                }

                if (groupNames.Count == 0)
                    groupNames.Add("其他");

                foreach (string groupName in groupNames.Distinct())
                {
                    if (!result.ContainsKey(groupName))
                        result[groupName] = new List<RecipeDef>();
                    if (!result[groupName].Contains(recipe))
                        result[groupName].Add(recipe);
                }
            }

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

using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ColonistModification
{
    /// <summary>
    /// 殖民者制式改造工具类
    /// 提供手术条件检测、Bill创建、最佳医生查找等通用功能
    /// </summary>
    public static class ColonistModificationUtility
    {
        /// <summary>
        /// 检查殖民者是否匹配模板的过滤条件
        /// </summary>
        /// <param name="pawn">要检查的殖民者</param>
        /// <param name="template">改造模板</param>
        /// <returns>是否匹配</returns>
        public static bool PawnMatchesTemplate(Pawn pawn, ColonistModificationTemplateDef template)
        {
            if (pawn == null || template == null)
                return false;

            // 必须是人类
            if (!pawn.RaceProps.Humanlike)
                return false;

            // 殖民者/奴隶过滤
            if (template.colonistsOnly && !pawn.IsColonist)
                return false;

            if (!template.includeSlaves && pawn.IsSlave)
                return false;

            // 年龄过滤
            if (template.minBiologicalAge > 0 && pawn.ageTracker.AgeBiologicalYears < template.minBiologicalAge)
                return false;

            // 异种类型过滤（如果设置了限制）
            if (template.targetXenotypeDefNames.Count > 0)
            {
                if (pawn.genes?.Xenotype == null)
                    return false;

                bool xenotypeMatch = false;
                foreach (string xenotypeDefName in template.targetXenotypeDefNames)
                {
                    if (pawn.genes.Xenotype.defName == xenotypeDefName)
                    {
                        xenotypeMatch = true;
                        break;
                    }
                }
                if (!xenotypeMatch)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 检查指定殖民者的手术条件是否满足
        /// </summary>
        /// <param name="pawn">目标殖民者</param>
        /// <param name="recipe">手术Recipe</param>
        /// <param name="map">当前地图</param>
        /// <returns>true表示条件满足，可以开始手术</returns>
        public static bool CanPerformSurgery(Pawn pawn, RecipeDef recipe, Map map)
        {
            if (pawn == null || recipe == null || map == null)
                return false;

            // 检查pawn是否存活且在地图上
            if (pawn.Dead || !pawn.Spawned)
                return false;

            // 检查recipe是否适用于该pawn
            RecipeWorker worker = recipe.Worker;
            if (worker == null)
                return false;

            // 获取可应用的身体部位
            IEnumerable<BodyPartRecord> parts = worker.GetPartsToApplyOn(pawn, recipe);
            if (parts == null || !parts.Any())
                return false;

            // 检查手术是否可用于该pawn
            if (!worker.AvailableOnNow(pawn, null))
                return false;

            // 检查是否有满足技能要求的医生
            if (!HasAvailableSurgeon(recipe, map))
                return false;

            // 检查药品是否充足
            if (!HasSufficientMedicine(recipe, map))
                return false;

            // 检查其他材料
            if (!HasRequiredMaterials(recipe, map))
                return false;

            return true;
        }

        /// <summary>
        /// 检查地图上是否有满足技能要求的可用医生
        /// </summary>
        public static bool HasAvailableSurgeon(RecipeDef recipe, Map map)
        {
            if (recipe.workSkill == null)
                return true;

            foreach (Pawn pawn in map.mapPawns.FreeColonists)
            {
                if (pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) &&
                    !pawn.Downed &&
                    !pawn.InMentalState)
                {
                    if (pawn.skills.GetSkill(recipe.workSkill) != null)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 检查地图上是否有足够的药品
        /// </summary>
        public static bool HasSufficientMedicine(RecipeDef recipe, Map map)
        {
            // 手术通常需要药品，检查地图上是否有可用药品
            ThingDef medicineDef = GetBestAvailableMedicine(map, recipe);
            return medicineDef != null;
        }

        /// <summary>
        /// 获取地图上最佳可用药品的ThingDef
        /// </summary>
        public static ThingDef GetBestAvailableMedicine(Map map, RecipeDef recipe)
        {
            // 按优先级查找：闪耀世界 > 工业 > 草药
            List<ThingDef> medicineCandidates = new List<ThingDef>
            {
                ThingDefOf.MedicineUltratech,
                ThingDefOf.MedicineIndustrial,
                ThingDefOf.MedicineHerbal
            };

            foreach (ThingDef medDef in medicineCandidates)
            {
                if (medDef == null) continue;

                // 检查是否有可用药品
                List<Thing> medicines = map.listerThings.ThingsOfDef(medDef);
                foreach (Thing med in medicines)
                {
                    if (!med.IsForbidden(Faction.OfPlayer) &&
                        med.stackCount > 0)
                    {
                        return medDef;
                    }
                }
            }

            return null; // 无可用药品
        }

        /// <summary>
        /// 检查是否有手术所需的其他材料
        /// </summary>
        public static bool HasRequiredMaterials(RecipeDef recipe, Map map)
        {
            if (recipe.ingredients == null || recipe.ingredients.Count == 0)
                return true;

            foreach (IngredientCount ingredient in recipe.ingredients)
            {
                // 跳过药品需求（已在HasSufficientMedicine中检查）
                if (ingredient.filter.Allows(ThingDefOf.MedicineHerbal) ||
                    ingredient.filter.Allows(ThingDefOf.MedicineIndustrial) ||
                    ingredient.filter.Allows(ThingDefOf.MedicineUltratech))
                    continue;

                // 检查地图上是否有足够材料
                if (!HasEnoughIngredient(ingredient, map))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 检查是否有足够的某个材料
        /// </summary>
        private static bool HasEnoughIngredient(IngredientCount ingredient, Map map)
        {
            float requiredAmount = ingredient.GetBaseCount();
            float foundAmount = 0f;

            List<Thing> things = map.listerThings.AllThings;
            foreach (Thing thing in things)
            {
                if (ingredient.filter.Allows(thing) && !thing.IsForbidden(Faction.OfPlayer))
                {
                    foundAmount += thing.stackCount;
                    if (foundAmount >= requiredAmount)
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 为指定步骤创建自定义手术Bill
        /// </summary>
        /// <param name="recipe">手术Recipe</param>
        /// <param name="patient">目标殖民者</param>
        /// <param name="template">所属改造模板</param>
        /// <param name="stepIndex">步骤索引</param>
        /// <param name="retryCount">重试次数（首次为0）</param>
        /// <returns>创建好的Bill_ColonistModification</returns>
        public static Bill_ColonistModification CreateBillForStep(
            RecipeDef recipe,
            Pawn patient,
            ColonistModificationTemplateDef template,
            int stepIndex,
            int retryCount = 0)
        {
            Bill_ColonistModification bill = new Bill_ColonistModification(recipe);
            bill.template = template;
            bill.currentStepIndex = stepIndex;
            bill.retryCount = retryCount;
            bill.SetPawnRestriction(patient);

            // 设置身体部位（如果是身体部位相关的手术）
            if (recipe.targetsBodyPart)
            {
                RecipeWorker worker = recipe.Worker;
                IEnumerable<BodyPartRecord> parts = worker.GetPartsToApplyOn(patient, recipe);
                BodyPartRecord bestPart = parts?.FirstOrDefault();
                if (bestPart != null)
                {
                    bill.Part = bestPart;
                }
            }

            // 处理异种胚植入手术：查找可用异种胚并绑定到Bill
            if (recipe.defName == "ImplantXenogerm" && ModsConfig.BiotechActive)
            {
                TryBindXenogerm(bill, patient);
            }

            return bill;
        }

        /// <summary>
        /// 尝试查找可用异种胚并绑定到Bill
        /// </summary>
        private static void TryBindXenogerm(Bill_ColonistModification bill, Pawn patient)
        {
            Map map = patient.Map;
            if (map == null)
                return;

            List<Thing> xenogerms = map.listerThings.ThingsOfDef(ThingDefOf.Xenogerm);
            foreach (Thing thing in xenogerms)
            {
                Xenogerm xenogerm = thing as Xenogerm;
                if (xenogerm != null && !xenogerm.IsForbidden(Faction.OfPlayer) && !xenogerm.Position.Fogged(map))
                {
                    bill.xenogerm = xenogerm;
                    return;
                }
            }
        }

        /// <summary>
        /// 检查殖民者是否已经完成了模板中的所有步骤
        /// </summary>
        public static bool HasCompletedTemplate(Pawn pawn, ColonistModificationTemplateDef template)
        {
            if (pawn == null || template == null)
                return false;

            PawnModificationRecord record = ColonistModificationManager.Instance?.GetRecord(pawn, template);

            for (int i = 0; i < template.resolvedRecipes.Count; i++)
            {
                RecipeDef recipe = template.resolvedRecipes[i];

                // 如果record记录显示此步骤已完成，跳过检查
                if (record != null && i <= record.lastCompletedStepIndex)
                    continue;

                if (recipe.addsHediff != null)
                {
                    if (!pawn.health.hediffSet.HasHediff(recipe.addsHediff))
                        return false;
                }
                else
                {
                    // 对于不添加hediff的recipe，如果record没有记录完成则未完成
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 检查殖民者正在进行的改造进度（返回已完成的步骤数）
        /// </summary>
        public static int GetCompletedStepCount(Pawn pawn, ColonistModificationTemplateDef template)
        {
            if (pawn == null || template == null)
                return 0;

            int completed = 0;
            foreach (RecipeDef recipe in template.resolvedRecipes)
            {
                if (recipe.addsHediff != null)
                {
                    if (pawn.health.hediffSet.HasHediff(recipe.addsHediff))
                        completed++;
                }
            }
            return completed;
        }

        /// <summary>
        /// 获取模板的下一个待执行步骤索引，如果全部完成则返回-1
        /// 对于addsHediff的recipe，通过检查pawn身上的hediff判断完成
        /// 对于不添加hediff的recipe（如异种胚植入），通过record的lastCompletedStepIndex判断
        /// </summary>
        public static int GetNextStepIndex(Pawn pawn, ColonistModificationTemplateDef template)
        {
            PawnModificationRecord record = ColonistModificationManager.Instance?.GetRecord(pawn, template);

            for (int i = 0; i < template.resolvedRecipes.Count; i++)
            {
                RecipeDef recipe = template.resolvedRecipes[i];

                // 如果record记录显示此步骤已完成，跳过
                if (record != null && i <= record.lastCompletedStepIndex)
                    continue;

                if (recipe.addsHediff != null)
                {
                    if (!pawn.health.hediffSet.HasHediff(recipe.addsHediff))
                        return i;
                }
                else
                {
                    // 对于不添加hediff的手术，依赖record追踪
                    // record已经显示此步骤未完成（上面已跳过已完成的），直接返回此索引
                    return i;
                }
            }
            return -1; // 全部完成
        }

        /// <summary>
        /// 获取地图上最适合执行手术的医生
        /// </summary>
        public static Pawn FindBestSurgeon(Map map, RecipeDef recipe, Pawn patient)
        {
            Pawn bestSurgeon = null;
            float bestSkill = -1f;

            foreach (Pawn pawn in map.mapPawns.FreeColonists)
            {
                if (pawn == patient)
                    continue; // 不能给自己做手术（游戏会自动处理，但提前过滤）

                if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) ||
                    pawn.Downed ||
                    pawn.InMentalState)
                    continue;

                float skillLevel = 0f;
                if (recipe.workSkill != null && pawn.skills != null)
                {
                    skillLevel = pawn.skills.GetSkill(recipe.workSkill).Level;
                }

                if (skillLevel > bestSkill)
                {
                    bestSkill = skillLevel;
                    bestSurgeon = pawn;
                }
            }
            return bestSurgeon;
        }
    }
}

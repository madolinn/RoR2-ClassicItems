﻿using RoR2;
using UnityEngine;
using System.Collections.ObjectModel;
using TILER2;
using static TILER2.MiscUtil;

namespace ThinkInvisible.ClassicItems {
    public class OldBox : Item<OldBox> {
        public override string displayName => "Old Box";
		public override ItemTier itemTier => ItemTier.Lunar;
		public override ReadOnlyCollection<ItemTag> itemTags => new ReadOnlyCollection<ItemTag>(new[]{ItemTag.Utility});
        public override bool itemAIB {get; protected set;} = true; //TODO: find a way to make fear work on players... random movement and forced sprint? halt movement (root)?

        [AutoUpdateEventInfo(AutoUpdateEventFlags.InvalidateDescToken)]
        [AutoItemConfig("Fraction of max health required as damage taken to trigger Old Box (halved per additional stack).", AutoItemConfigFlags.None, 0f, 1f)]
        public float healthThreshold {get; private set;} = 0.5f;

        [AutoUpdateEventInfo(AutoUpdateEventFlags.InvalidateDescToken)]
        [AutoItemConfig("AoE radius for Old Box.", AutoItemConfigFlags.None, 0f, float.MaxValue)]
        public float radius {get; private set;} = 25f;
        
        [AutoUpdateEventInfo(AutoUpdateEventFlags.InvalidateDescToken)]
        [AutoItemConfig("Duration of fear debuff applied by Old Box.", AutoItemConfigFlags.None, 0f, float.MaxValue)]
        public float duration {get; private set;} = 2f;

        [AutoItemConfig("If true, damage to shield and barrier (from e.g. Personal Shield Generator, Topaz Brooch) will not count towards triggering Old Box.")]
        public bool requireHealth {get; private set;} = true;
        protected override string NewLangName(string langid = null) => displayName;
        protected override string NewLangPickup(string langid = null) => "Chance to fear enemies when attacked.";
        protected override string NewLangDesc(string langid = null) => "<style=cDeath>When hit for more than " + Pct(healthThreshold) + " max health</style> <style=cStack>(/2 per stack)</style>, <style=cIsUtility>fear enemies</style> within <style=cIsUtility>" + radius.ToString("N0") + " m</style> for <style=cIsUtility>" + duration.ToString("N1") + " seconds</style>. <style=cIsUtility>Feared enemies will run out of melee</style>, <style=cDeath>but that won't stop them from shooting you.</style>";
        protected override string NewLangLore(string langid = null) => "A relic of times long past (ClassicItems mod)";

        public OldBox() {}

        protected override void LoadBehavior() {
			On.RoR2.HealthComponent.TakeDamage += On_HCTakeDamage;
        }

        protected override void UnloadBehavior() {
            On.RoR2.HealthComponent.TakeDamage -= On_HCTakeDamage;
        }

        private void On_HCTakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo di) {
            var oldHealth = self.health;
            var oldCH = self.combinedHealth;

			orig(self, di);

			int icnt = GetCount(self.body);
            float adjThreshold = healthThreshold * Mathf.Pow(2, 1-icnt);
			if(icnt < 1
                || (requireHealth && (oldHealth - self.health)/self.fullHealth < adjThreshold)
                || (!requireHealth && (oldCH - self.combinedHealth)/self.fullCombinedHealth < adjThreshold))
                return;

            /*Vector3 corePos = Util.GetCorePosition(self.body);
			var thisThingsGonnaX = GlobalEventManager.instance.explodeOnDeathPrefab;
			var x = thisThingsGonnaX.GetComponent<DelayBlast>();
			EffectManager.SpawnEffect(x.explosionEffect, new EffectData {
				origin = corePos,
				rotation = Quaternion.identity,
                color = Color.blue,
				scale = radius
			}, true);*/

            var tind = TeamIndex.Monster | TeamIndex.Neutral | TeamIndex.Player;
			tind &= ~self.body.teamComponent.teamIndex;
			ReadOnlyCollection<TeamComponent> teamMembers = TeamComponent.GetTeamMembers(tind);
			float sqrad = radius * radius;
			foreach(TeamComponent tcpt in teamMembers) {
				if ((tcpt.transform.position - self.body.corePosition).sqrMagnitude <= sqrad) {
					if (tcpt.body && tcpt.body.mainHurtBox && tcpt.body.isActiveAndEnabled) {
                        tcpt.body.AddTimedBuff(ClassicItemsPlugin.fearBuff, duration);
					}
				}
			}
        }
	}
}

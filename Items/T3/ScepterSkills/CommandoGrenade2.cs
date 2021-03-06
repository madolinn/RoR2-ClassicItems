﻿using UnityEngine;
using RoR2.Skills;
using static TILER2.MiscUtil;
using R2API;
using RoR2.Projectile;
using RoR2;
using EntityStates.Commando.CommandoWeapon;
using R2API.Utils;

namespace ThinkInvisible.ClassicItems {
    public static class CommandoGrenade2 {
        private static GameObject projReplacer;
        public static SkillDef myDef {get; private set;}

        internal static void SetupAttributes() {
            var oldDef = Resources.Load<SkillDef>("skilldefs/commandobody/ThrowGrenade");
            myDef = CloneSkillDef(oldDef);

            var nametoken = "CLASSICITEMS_SCEPCOMMANDO_GRENADENAME";
            var desctoken = "CLASSICITEMS_SCEPCOMMANDO_GRENADEDESC";
            var namestr = "Carpet Bomb";
            LanguageAPI.Add(nametoken, namestr);
            LanguageAPI.Add(desctoken, Language.GetString(oldDef.skillDescriptionToken) + "\n<color=#d299ff>SCEPTER: Half damage and knockback; throw eight at once.</color>");
            
            myDef.skillName = namestr;
            myDef.skillNameToken = nametoken;
            myDef.skillDescriptionToken = desctoken;
            myDef.icon = Resources.Load<Sprite>("@ClassicItems:Assets/ClassicItems/icons/scepter/commando_grenadeicon.png");

            LoadoutAPI.AddSkillDef(myDef);

            projReplacer = Resources.Load<GameObject>("prefabs/projectiles/CommandoGrenadeProjectile").InstantiateClone("CIScepCommandoGrenade");
            var pie = projReplacer.GetComponent<ProjectileImpactExplosion>();
            pie.blastDamageCoefficient *= 0.5f;
            pie.bonusBlastForce *= 0.5f;

            ProjectileCatalog.getAdditionalEntries += (list) => list.Add(projReplacer);
        }
        
        internal static void LoadBehavior() {
            On.EntityStates.Commando.CommandoWeapon.FireFMJ.Fire += On_FireFMJFire;
        }

        internal static void UnloadBehavior() {
            On.EntityStates.Commando.CommandoWeapon.FireFMJ.Fire -= On_FireFMJFire;
        }

        private static void On_FireFMJFire(On.EntityStates.Commando.CommandoWeapon.FireFMJ.orig_Fire orig, FireFMJ self) {
            var cc = self.outer.commonComponents;
            bool isBoosted = self is ThrowGrenade
                && Util.HasEffectiveAuthority(self.outer.networkIdentity)
                && Scepter.instance.GetCount(cc.characterBody) > 0;
            if(isBoosted) self.projectilePrefab = projReplacer;
            orig(self);
            if(isBoosted) {
                for(var i = 0; i < 7; i++) {
                    Ray r;
			        if (cc.inputBank) r = new Ray(cc.inputBank.aimOrigin, cc.inputBank.aimDirection);
                    else r = new Ray(cc.transform.position, cc.transform.forward);
				    r.direction = Util.ApplySpread(r.direction, self.minSpread + 7f, self.maxSpread + 15f, 1f, 1f, 0f, self.projectilePitchBonus);
				    ProjectileManager.instance.FireProjectile(
                        self.projectilePrefab,
                        r.origin, Util.QuaternionSafeLookRotation(r.direction),
                        self.outer.gameObject,
                        (float)typeof(FireFMJ).GetFieldCached("damageStat").GetValue(self) * self.damageCoefficient,
                        self.force,
                        Util.CheckRoll((float)typeof(FireFMJ).GetFieldCached("critStat").GetValue(self), cc.characterBody.master),
                        DamageColorIndex.Default, null, -1f);
                }
            }
        }
    }
}
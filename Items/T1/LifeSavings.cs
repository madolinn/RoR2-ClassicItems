﻿using RoR2;
using System;
using UnityEngine;
using BepInEx.Configuration;

namespace ThinkInvisible.ClassicItems
{
    public class LifeSavings : ItemBoilerplate
    {
        public override string itemCodeName{get;} = "LifeSavings";

        private ConfigEntry<float> cfgGainPerSec;
        private ConfigEntry<int> cfgInvertCount;

        public float gainPerSec {get;private set;}
        public int invertCount {get;private set;}

        public bool holdIt {get; private set;} = false; //https://www.youtube.com/watch?v=vDMwDT6BhhE

        protected override void SetupConfigInner(ConfigFile cfl) {
            cfgGainPerSec = cfl.Bind(new ConfigDefinition("Items." + itemCodeName, "GainPerSec"), 1f, new ConfigDescription(
                "Money to add to players per second per Life Savings stack (without taking into account InvertCount).",
                new AcceptableValueRange<float>(0f,float.MaxValue)));
            cfgInvertCount = cfl.Bind(new ConfigDefinition("Items." + itemCodeName, "InvertCount"), 3, new ConfigDescription(
                "With <InvertCount stacks, number of stacks affects time per interval instead of multiplying money gained.",
                new AcceptableValueRange<int>(0,int.MaxValue)));

            gainPerSec = cfgGainPerSec.Value;
            invertCount = cfgInvertCount.Value;
        }

        protected override void SetupAttributesInner() {
            modelPathName = "savingscard.prefab";
            iconPathName = "lifesavings_icon.png";
            RegLang("Life Savings",
            	"Earn gold over time.",
            	"Generates <style=cIsUtility>$" + gainPerSec + "</style> <style=cStack>(+$" + gainPerSec + " per stack)</style> every second.",
            	"A relic of times long past (ClassicItems mod)");
            _itemTags = new[]{ItemTag.Utility};
            itemTier = ItemTier.Tier1;
        }

        protected override void SetupBehaviorInner() {
            On.RoR2.CharacterBody.OnInventoryChanged += On_CBOnInventoryChanged;
            On.RoR2.CharacterBody.FixedUpdate += On_CBFixedUpdate;
            On.RoR2.SceneExitController.Begin += On_SECBegin;
            On.RoR2.SceneExitController.OnDestroy += On_SECDestroy;
        }

        private void On_SECDestroy(On.RoR2.SceneExitController.orig_OnDestroy orig, SceneExitController self) {
            holdIt = false;
            orig(self);
        }

        private void On_SECBegin(On.RoR2.SceneExitController.orig_Begin orig, SceneExitController self) {
            holdIt = true;
            orig(self);
        }

        private void On_CBFixedUpdate(On.RoR2.CharacterBody.orig_FixedUpdate orig, CharacterBody self) {
            LifeSavingsComponent cpt = self.GetComponent<LifeSavingsComponent>();
            if(self.inventory && self.master && cpt) {
                int icnt = GetCount(self);
                if(icnt > 0)
                    cpt.moneyBuffer += Time.fixedDeltaTime * gainPerSec * ((icnt < invertCount)?(1f/(float)(invertCount-icnt+1)):(icnt-invertCount+1));
                //Disable during teleport animation, but keep tracking time so it stacks up after teleport is complete
                //Accumulator is emptied into actual money variable whenever a tick passes and it has enough for a change in integer value
                if(cpt.moneyBuffer >= 1.0f && !holdIt){
                    if(BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.funkfrog_sipondo.sharesuite") && Compat_ShareSuite.MoneySharing())
                        Compat_ShareSuite.GiveMoney((uint)Math.Floor(cpt.moneyBuffer));
                    else
                        self.master.GiveMoney((uint)Math.Floor(cpt.moneyBuffer));
                    cpt.moneyBuffer %= 1.0f;
                }
            }
            orig(self);
        }

        private void On_CBOnInventoryChanged(On.RoR2.CharacterBody.orig_OnInventoryChanged orig, CharacterBody self) {
            orig(self);
            var cpt = self.GetComponent<LifeSavingsComponent>();
            if(!cpt) cpt = self.gameObject.AddComponent<LifeSavingsComponent>();
        }
    }
        
    public class LifeSavingsComponent : MonoBehaviour
    {
        public float moneyBuffer = 0f;
    }
}

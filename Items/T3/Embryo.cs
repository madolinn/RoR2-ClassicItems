﻿using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using System;
using UnityEngine;
using R2API.Utils;
using BepInEx.Configuration;
using static ThinkInvisible.ClassicItems.MiscUtil;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine.Networking;
using R2API;

namespace ThinkInvisible.ClassicItems {
    public class Embryo : ItemBoilerplate<Embryo> {
        public override string itemCodeName {get;} = "Embryo";

        private ConfigEntry<float> cfgProcChance;

        private Dictionary<EquipmentIndex,ConfigEntry<bool>> cfgSubEnable;
        private Dictionary<Type,ConfigEntry<bool>> cfgSubEnableInternal;

        private ConfigEntry<bool> cfgSubEnableModded;

        public float procChance {get;private set;}

        public ReadOnlyDictionary<EquipmentIndex,bool> subEnable {get;private set;}
        public ReadOnlyDictionary<Type,bool> subEnableInternal {get;private set;}

        public bool subEnableModded {get;private set;}

        private ConfigFile cachedCfl;
        protected override void SetupConfigInner(ConfigFile cfl) {
            cachedCfl = cfl;
            cfgProcChance = cfl.Bind<float>(new ConfigDefinition("Items." + itemCodeName, "ProcChance"), 30f, new ConfigDescription(
                "Percent chance of triggering an equipment twice. Stacks additively.",
                new AcceptableValueRange<float>(0f,100f)));
            procChance = cfgProcChance.Value;

            cfgSubEnable = new Dictionary<EquipmentIndex,ConfigEntry<bool>>();
            Dictionary<EquipmentIndex,bool> _subEnable = new Dictionary<EquipmentIndex, bool>();
            
            foreach(EquipmentIndex ind in (EquipmentIndex[]) Enum.GetValues(typeof(EquipmentIndex))) {
                if(ind == EquipmentIndex.AffixBlue || ind == EquipmentIndex.AffixGold || ind == EquipmentIndex.AffixHaunted || ind == EquipmentIndex.AffixPoison || ind == EquipmentIndex.AffixRed || ind == EquipmentIndex.AffixWhite || ind == EquipmentIndex.AffixYellow
                    || ind == EquipmentIndex.BurnNearby || ind == EquipmentIndex.CrippleWard || ind == EquipmentIndex.LunarPotion || ind == EquipmentIndex.SoulCorruptor || ind == EquipmentIndex.Tonic
                    || ind == EquipmentIndex.GhostGun || ind == EquipmentIndex.OrbitalLaser || ind == EquipmentIndex.SoulJar
                    || ind == EquipmentIndex.Count || ind == EquipmentIndex.Enigma || ind == EquipmentIndex.None || ind == EquipmentIndex.QuestVolatileBattery
                    )
                    continue;
                cfgSubEnable.Add(ind, cfl.Bind<bool>(new ConfigDefinition("Items." + itemCodeName, "SubEnable" + ind.ToString()), true, new ConfigDescription(
                "If false, Beating Embryo will not affect " + ind.ToString() + ".")));
                _subEnable.Add(ind, cfgSubEnable[ind].Value);
            }
            subEnable = new ReadOnlyDictionary<EquipmentIndex,bool>(_subEnable);

            cfgSubEnableModded = cfl.Bind<bool>(new ConfigDefinition("Items." + itemCodeName, "SubEnableModded"), false, new ConfigDescription(
                "If false, Beating Embryo will not affect equipment added by other mods. If true, these items will be triggered twice when Beating Embryo procs, which may not work with some items."));
            subEnableModded = cfgSubEnableModded.Value;
        }
        
        protected override void SetupAttributesInner() {
            modelPathName = "embryo_model.prefab";
            iconPathName = "embryo_icon.png";
            RegLang("Beating Embryo",
            	"Equipment has a 30% chance to deal double the effect.",
            	"Upon activating an equipment, adds a <style=cIsUtility>" + pct(procChance, 0, 1) + "</style> <style=cStack>(+" + pct(procChance, 0, 1) + " per stack)</style> chance to <style=cIsUtility>double its effects somehow</style>.",
            	"A relic of times long past (ClassicItems mod)");
            _itemTags = new List<ItemTag>{ItemTag.EquipmentRelated};
            itemTier = ItemTier.Tier3;
        }

        private bool ILFailed = false;

        private GameObject embryoCptPrefab;
        private GameObject boostedGatewayPrefab;
        private GameObject boostedScannerPrefab;

        private readonly ReadOnlyCollection<EquipmentIndex> simpleDoubleEqps = new ReadOnlyCollection<EquipmentIndex>(new[] {
            EquipmentIndex.Fruit,
            EquipmentIndex.Lightning,
            EquipmentIndex.DroneBackup,
            EquipmentIndex.PassiveHealing,
            EquipmentIndex.Saw
        });

        protected override void SetupBehaviorInner() {
            ///// WARNING: late config setup. is there a safer way to do this? eqpIsLunar and itemIsEquipment are defined during attributes stage
            Debug.Log("ClassicItems: adding late config for Embryo");
            cfgSubEnableInternal = new Dictionary<Type,ConfigEntry<bool>>();
            Dictionary<Type,bool> _subEnableInternal = new Dictionary<Type,bool>();
            foreach(ItemBoilerplate bpl in ClassicItemsPlugin.masterItemList) {
                if(bpl.itemIsEquipment) {
                    cfgSubEnableInternal.Add(bpl.GetType(), cachedCfl.Bind<bool>(new ConfigDefinition("Items." + itemCodeName, "SubEnable" + bpl.itemCodeName), !bpl.eqpIsLunar, new ConfigDescription(
                    "If false, Beating Embryo will not affect " + bpl.itemCodeName + " (added by CustomItems).")));
                    _subEnableInternal.Add(bpl.GetType(), cfgSubEnableInternal[bpl.GetType()].Value);
                }
            }
            subEnableInternal = new ReadOnlyDictionary<Type,bool>(_subEnableInternal);
            ///// end late config setup


            boostedScannerPrefab = Resources.Load<GameObject>("Prefabs/NetworkedObjects/ChestScanner").InstantiateClone("boostedScannerPrefab");
            boostedScannerPrefab.GetComponent<ChestRevealer>().revealDuration *= 2f;

            boostedGatewayPrefab = Resources.Load<GameObject>("Prefabs/NetworkedObjects/Zipline").InstantiateClone("boostedGatewayPrefab");
            var ziplineCtrl = boostedGatewayPrefab.GetComponent<ZiplineController>();
            ziplineCtrl.ziplineVehiclePrefab = ziplineCtrl.ziplineVehiclePrefab.InstantiateClone("boostedGatewayVehiclePrefab");
            var zvh = ziplineCtrl.ziplineVehiclePrefab.GetComponent<ZiplineVehicle>();
            zvh.maxSpeed *= 2f;
            zvh.acceleration *= 2f;

            var eCptPrefab2 = new GameObject("embryoCptPrefabPrefab");
            eCptPrefab2.AddComponent<NetworkIdentity>();
            eCptPrefab2.AddComponent<EmbryoComponent>();
            eCptPrefab2.AddComponent<NetworkedBodyAttachment>().forceHostAuthority = true;
            embryoCptPrefab = eCptPrefab2.InstantiateClone("embryoCptPrefab");
            GameObject.Destroy(eCptPrefab2);

            On.RoR2.CharacterBody.OnInventoryChanged += On_CBOnInventoryChanged;
            On.RoR2.EquipmentSlot.PerformEquipmentAction += On_ESPerformEquipmentAction;

            IL.RoR2.EquipmentSlot.PerformEquipmentAction += IL_ESPerformEquipmentAction;
            IL.RoR2.EquipmentSlot.FixedUpdate += IL_ESFixedUpdate;

            if(subEnable[EquipmentIndex.GoldGat]) {
                IL.EntityStates.GoldGat.GoldGatFire.FireBullet += IL_EntGGFFireBullet;
                if(ILFailed) IL.EntityStates.GoldGat.GoldGatFire.FireBullet -= IL_EntGGFFireBullet;
                ILFailed = false;
            }

            if(subEnable[EquipmentIndex.Gateway]) {
                IL.RoR2.EquipmentSlot.FireGateway += IL_ESFireGateway;
                if(ILFailed) IL.RoR2.EquipmentSlot.FireGateway -= IL_ESFireGateway;
                ILFailed = false;
            }

            if(subEnable[EquipmentIndex.Jetpack]) {
                IL.RoR2.JetpackController.FixedUpdate += IL_JCFixedUpdate;
                if(ILFailed) IL.RoR2.JetpackController.FixedUpdate -= IL_JCFixedUpdate;
                ILFailed = false;
            }

            if(subEnable[EquipmentIndex.CritOnUse]) {
                IL.RoR2.EquipmentSlot.RpcOnClientEquipmentActivationRecieved += IL_ESRpcOnEquipmentActivationReceived;
                if(ILFailed) IL.RoR2.EquipmentSlot.RpcOnClientEquipmentActivationRecieved -= IL_ESRpcOnEquipmentActivationReceived;
                ILFailed = false;
            }
        }
        
        public bool CheckProc(CharacterBody body) {
            return itemEnabled && Util.CheckRoll(GetCount(body)*procChance, body.master);
        }
        public bool CheckProc(EquipmentIndex subind, CharacterBody body) {
            return subEnable[subind] && CheckProc(body);
        }
        public bool CheckProc<T>(CharacterBody body) where T:ItemBoilerplate {
            return itemEnabled && subEnableInternal[typeof(T)] && Util.CheckRoll(GetCount(body)*procChance, body.master);
        }

        private void On_CBOnInventoryChanged(On.RoR2.CharacterBody.orig_OnInventoryChanged orig, CharacterBody self) {            
            orig(self);
            if(!NetworkServer.active || GetCount(self) < 1) return;
            var cpt = self.GetComponentInChildren<EmbryoComponent>();
            if(!cpt) {
                var cptInst = GameObject.Instantiate(embryoCptPrefab, self.transform);
                cptInst.GetComponent<NetworkedBodyAttachment>().AttachToGameObjectAndSpawn(self.gameObject);
            }
        }

        private bool On_ESPerformEquipmentAction(On.RoR2.EquipmentSlot.orig_PerformEquipmentAction orig, EquipmentSlot slot, EquipmentIndex ind) {
            var retv = orig(slot, ind);
            foreach(ItemBoilerplate bpl in ClassicItemsPlugin.masterItemList) {
                if(bpl.itemEnabled && bpl.itemIsEquipment && ind == bpl.regIndexEqp) return retv; //Embryo is handled in individual item code for CI items
            }
            if(slot.characterBody && Util.CheckRoll(GetCount(slot.characterBody)*procChance)) {
                if((simpleDoubleEqps.Contains(ind) && subEnable[ind])
                    || (ind >= EquipmentIndex.Count && subEnableModded))
                    retv = retv || orig(slot, ind);
            }
            return retv;
        }

        private void IL_ESPerformEquipmentAction(ILContext il) {            
            ILCursor c = new ILCursor(il);

            //Insert a check for Embryo procs at the top of the function
            bool boost = false;
            EmbryoComponent cpt = null;
            c.Emit(OpCodes.Ldarg_0);
            c.EmitDelegate<Action<EquipmentSlot>>((slot)=>{
                boost = Util.CheckRoll(GetCount(slot.characterBody)*procChance);
                cpt = slot.characterBody?.GetComponentInChildren<EmbryoComponent>();
            });
            
            bool ILFound;

            ILLabel[] swarr = new ILLabel[]{};
            //Load switch case locations
            ILFound = c.TryGotoNext(
                x=>x.MatchSwitch(out swarr));
            if(!ILFound) {
                Debug.LogError("ClassicItems: failed to apply Beating Embryo IL patch (ALL EQUIPMENTS): couldn't find switch!");
                return;
            }

            //CommandMissile: double number of missiles fired in the same timespan
            if((int)EquipmentIndex.CommandMissile >= swarr.Length)
                Debug.LogError("ClassicItems: failed to apply Beating Embryo IL patch: CommandMissile (PerformEquipmentAction); not in switch");
            else if(subEnable[EquipmentIndex.CommandMissile]) {
                //Find: default missile increment (+= (int)12)
                c.GotoLabel(swarr[(int)EquipmentIndex.CommandMissile]);
                ILFound = c.TryGotoNext(
                    x=>x.MatchLdfld<EquipmentSlot>("remainingMissiles"),
                    x=>x.OpCode == OpCodes.Ldc_I4_S,
                    x=>x.MatchAdd());

                if(ILFound) {
                    c.Index+=2;
                    //Replace original increment number with a custom function to check for Embryo proc
                    //If proc happens, doubles number of missiles added and marks the total number of missiles added as boosted; otherwise returns original
                    c.EmitDelegate<Func<int,int>>((int origMissiles)=>{
                        if(boost && cpt) cpt.boostedMissiles += origMissiles*2;
                        return (sbyte)(boost ? origMissiles*2 : origMissiles);
                    });
                } else {
                    Debug.LogError("ClassicItems: failed to apply Beating Embryo IL patch: CommandMissile (PerformEquipmentAction); target instructions not found");
                }
            }

            //Blackhole: double yoink radius
            if((int)EquipmentIndex.CommandMissile >= swarr.Length)
                Debug.LogError("ClassicItems: failed to apply Beating Embryo IL patch: Blackhole; not in switch");
            else if(subEnable[EquipmentIndex.Blackhole]) {
                //Find: string "Prefabs/Projectiles/GravSphere", ldloc 15 (Vector3 position)
                c.GotoLabel(swarr[(int)EquipmentIndex.Blackhole]);
                ILFound = c.TryGotoNext(MoveType.After,
                    x=>x.MatchLdstr("Prefabs/Projectiles/GravSphere"),
                    x=>x.MatchCallOrCallvirt<Resources>("Load"));

                if(ILFound) {
                    //Insert a custom function to check for Embryo proc (captures GravSphere projectile prefab)
                    //If proc happens, radius of prefab's RadialForce component (GravSphere pull range) is doubled
                    c.EmitDelegate<Func<GameObject,GameObject>>((obj)=>{
                        var newobj = UnityEngine.Object.Instantiate(obj);
                        if(boost) newobj.GetComponent<RadialForce>().radius *= 2;
                        return newobj;
                    });
                } else {
                    Debug.LogError("ClassicItems: failed to apply Beating Embryo IL patch: Blackhole; target instructions not found");
                }
            }

            //CritOnUse: double duration
            if((int)EquipmentIndex.CritOnUse >= swarr.Length)
                Debug.LogError("ClassicItems: failed to apply Beating Embryo IL patch: CritOnUse; not in switch");
            else if(subEnable[EquipmentIndex.CritOnUse]) {
                //Find: AddTimedBuff(BuffIndex.FullCrit, 8f)
                c.GotoLabel(swarr[(int)EquipmentIndex.CritOnUse]);
                ILFound = c.TryGotoNext(
                    x=>x.MatchLdcI4((int)BuffIndex.FullCrit),
                    x=>x.OpCode == OpCodes.Ldc_R4,
                    x=>x.MatchCallOrCallvirt<CharacterBody>("AddTimedBuff"));
                    
                if(ILFound) {
                    //Advance cursor to the found ldcR4 (time argument of AddTimedBuff)
                    c.Index+=2;
                    //Replace original buff time with a custom function to check for Embryo proc
                    //If proc happens, doubles the buff time; otherwise returns original
                    c.EmitDelegate<Func<float,float>>((origBuffTime) => {
                        if(cpt) cpt.lastCOUBoosted = boost;
                        return boost ? origBuffTime*2 : origBuffTime;
                    });
                } else {
                    Debug.LogError("ClassicItems: failed to apply Beating Embryo IL patch: CritOnUse; target instructions not found");
                }
            }

            //Gateway: double speed
            if((int)EquipmentIndex.Gateway >= swarr.Length)
                Debug.LogError("ClassicItems: failed to apply Beating Embryo IL patch: Gateway; not in switch");
            else if(subEnable[EquipmentIndex.Gateway]) {
                //Find: start of Gateway label
                c.GotoLabel(swarr[(int)EquipmentIndex.Gateway]);

                //Insert a custom function to check boost
                //If proc happens, increments the player's boosted gateway counter; this will be spent once the gateway spawns
                c.EmitDelegate<Action>(()=>{
                    if(boost && cpt) cpt.boostedGates++;
                });
            }

            //Scanner: double duration
            if((int)EquipmentIndex.Scanner >= swarr.Length)
                Debug.LogError("ClassicItems: failed to apply Beating Embryo IL patch: Scanner; not in switch");
            else if(subEnable[EquipmentIndex.Scanner]) {
                //Find: loading of prefab
                c.GotoLabel(swarr[(int)EquipmentIndex.Scanner]);
                ILFound = c.TryGotoNext(MoveType.After,
                    x=>x.MatchLdstr("Prefabs/NetworkedObjects/ChestScanner"),
                    x=>x.MatchCall<UnityEngine.Resources>("Load"));

                if(ILFound) {
                    //Insert a custom function to check boost
                    //If proc happens, replace the loaded prefab with a boosted copy (allows proper networking)
                    c.EmitDelegate<Func<GameObject,GameObject>>((origObj)=>{
                        if(boost) return boostedScannerPrefab;
                        else return origObj;
                    });
                } else {
                    Debug.LogError("ClassicItems: failed to apply Beating Embryo IL patch: Scanner; target instructions not found");
                }
            }

            //BFG: double impact damage
            if((int)EquipmentIndex.BFG >= swarr.Length)
                Debug.LogError("ClassicItems: failed to apply Beating Embryo IL patch: BFG; not in switch");
            else if(subEnable[EquipmentIndex.BFG]) {
                //Find: loading of (int)2 into EquipmentSlot.bfgChargeTimer
                c.GotoLabel(swarr[(int)EquipmentIndex.BFG]);
                ILFound = c.TryGotoNext(MoveType.After,
                    x=>x.OpCode == OpCodes.Ldc_R4,
                    x=>x.MatchStfld<EquipmentSlot>("bfgChargeTimer"));

                if(ILFound) {
                    //Insert a custom function to check boost
                    //If proc happens, increments the player's boosted BFG shot counter; this will be spent once the BFG actually fires
                    c.EmitDelegate<Action>(()=>{
                        if(boost && cpt) cpt.boostedBFGs++;
                    });
                } else {
                    Debug.LogError("ClassicItems: failed to apply Beating Embryo IL patch: BFG (PerformEquipmentAction); target instructions not found");
                }
            }

            //Jetpack: double duration
            if((int)EquipmentIndex.Jetpack >= swarr.Length)
                Debug.LogError("ClassicItems: failed to apply Beating Embryo IL patch: Jetpack; not in switch");
            else if(subEnable[EquipmentIndex.Jetpack]) {
                //Find: start of Jetpack label
                c.GotoLabel(swarr[(int)EquipmentIndex.Jetpack]);

                //Insert a custom function to check boost
                //If proc happens, increments the player's boosted jetpack counter; this will be spent during the RPC duration reset
                c.EmitDelegate<Action>(()=>{
                    if(boost && cpt) {
                        cpt.boostedJetTime = 15f;
                    }
                });
            }

            //FireBallDash: double speed and damage
            if((int)EquipmentIndex.FireBallDash >= swarr.Length)
                Debug.LogError("ClassicItems: failed to apply Beating Embryo IL patch: FireBallDash; not in switch");
            else if(subEnable[EquipmentIndex.FireBallDash]) {
                //Find: string "Prefabs/NetworkedObjects/FireballVehicle"
                //Then find: instantiation of the prefab
                c.GotoLabel(swarr[(int)EquipmentIndex.FireBallDash]);
                ILFound = c.TryGotoNext(
                    x=>x.MatchLdstr("Prefabs/NetworkedObjects/FireballVehicle"))
                && c.TryGotoNext(
                    x=>x.MatchCallOrCallvirt<UnityEngine.Object>("Instantiate"));

                if(ILFound) {
                    c.Index++;

                    //Insert a custom function to check boost (captures a dup of the instantiated FireballVehicle)
                    //If proc happens, doubles the instanced vehicle's target speed, acceleration, and blast and collision damage
                    c.Emit(OpCodes.Dup);
                    c.EmitDelegate<Action<GameObject>>((go)=>{
                        if(boost) {
                            go.GetComponent<FireballVehicle>().targetSpeed *= 2f;
                            go.GetComponent<FireballVehicle>().acceleration *= 2f;
                            go.GetComponent<FireballVehicle>().blastDamageCoefficient *= 2f;
                            go.GetComponent<FireballVehicle>().overlapDamageCoefficient *= 2f;
                        }
                    });
                } else {
                    Debug.LogError("ClassicItems: failed to apply Beating Embryo IL patch: FireBallDash; target instructions not found");
                }
            }

            //GainArmor: double duration
            if((int)EquipmentIndex.GainArmor >= swarr.Length)
                Debug.LogError("ClassicItems: failed to apply Beating Embryo IL patch: GainArmor; not in switch");
            else if(subEnable[EquipmentIndex.GainArmor]) {
                //Find: AddTimedBuff(BuffIndex.ElephantArmorBoost, 5f)
                c.GotoLabel(swarr[(int)EquipmentIndex.GainArmor]);
                ILFound = c.TryGotoNext(
                    x=>x.MatchLdcI4((int)BuffIndex.ElephantArmorBoost),
                    x=>x.OpCode == OpCodes.Ldc_R4,
                    x=>x.MatchCallvirt<CharacterBody>("AddTimedBuff"));

                if(ILFound) {
                    //Advance cursor to the found ldcR4 (time argument of AddTimedBuff)
                    c.Index+=2;

                    //Replace original buff time (5f) with a custom function to check for Embryo proc
                    //If proc happens, doubles the buff time to 10f; otherwise returns original
                    c.EmitDelegate<Func<float,float>>((origBuffTime)=>{
                        return boost?2*origBuffTime:origBuffTime;
                    });
                } else {
                    Debug.LogError("ClassicItems: failed to apply Beating Embryo IL patch: GainArmor; target instructions not found");
                }
            }

            //Cleanse: double projectile delete radius
            if((int)EquipmentIndex.Cleanse >= swarr.Length)
                Debug.LogError("ClassicItems: failed to apply Beating Embryo IL patch: Cleanse; not in switch");
            else if(subEnable[EquipmentIndex.Cleanse]) {
                //Find: num3 = 6f; num4 = num3 * num3;
                float origRadius = 6f;
                c.GotoLabel(swarr[(int)EquipmentIndex.Cleanse]);
                ILFound = c.TryGotoNext(
                    x=>x.MatchCallOrCallvirt<SetStateOnHurt>("Cleanse"),
                    x=>x.MatchLdcR4(out origRadius),
                    x=>x.MatchDup(),
                    x=>x.MatchMul(),
                    x=>x.OpCode == OpCodes.Stloc_S);

                if(ILFound) {
                    c.Index+=2;
                    c.EmitDelegate<Func<float,float>>((ofl)=>{
                        return boost?2*ofl:ofl;
                    });
                } else {
                    Debug.LogError("ClassicItems: failed to apply Beating Embryo IL patch: Cleanse; target instructions not found");
                }
            }
            
            //Recycle: double recycle count
            if((int)EquipmentIndex.Recycle >= swarr.Length)
                Debug.LogError("ClassicItems: failed to apply Beating Embryo IL patch: Recycle; not in switch");
            else if(subEnable[EquipmentIndex.Recycle]) {
                c.GotoLabel(swarr[(int)EquipmentIndex.Recycle]);
                ILFound = c.TryGotoNext(
                    x=>x.MatchLdloc(out _),
                    x=>x.MatchLdcI4(1),
                    x=>x.MatchCallOrCallvirt<GenericPickupController>("set_NetworkRecycled"));

                if(ILFound) {
                    c.Index++;
                    c.Emit(OpCodes.Dup);
                    c.Index++;
                    c.EmitDelegate<Func<GenericPickupController,bool,bool>>((pctrl,origRecyc) => {
                        if(boost && pctrl && pctrl.GetComponent<EmbryoRecycleFlag>() == null) {
                            pctrl.gameObject.AddComponent<EmbryoRecycleFlag>();
                            return false;
                        }
                        return true;
                    });
                } else {
                    Debug.LogError("ClassicItems: failed to apply Beating Embryo IL patch: Recycle; target instructions not found");
                }
            }
        }

        private void IL_ESFixedUpdate(ILContext il) {
            ILCursor c = new ILCursor(il);

            EmbryoComponent cpt = null;
            c.Emit(OpCodes.Ldarg_0);
            c.EmitDelegate<Action<EquipmentSlot>>((slot)=>{
                cpt = slot.characterBody?.GetComponentInChildren<EmbryoComponent>();
            });
                
            bool ILFound;

            if(subEnable[EquipmentIndex.CommandMissile]) {
                //Find: loading of 0.125f into EquipmentSlot.missileTimer
                ILFound = c.TryGotoNext(
                    x=>x.OpCode == OpCodes.Ldc_R4,
                    x=>x.MatchStfld<EquipmentSlot>("missileTimer"));

                if(ILFound) {
                    //Replace original missile cooldown (0.125f) with a custom function to check for Embryo-boosted missiles
                    //If boosts exist, halves the missile cooldown to 0.0625f and deducts a boost from CPD; otherwise returns original
                    c.Index++;
                    c.EmitDelegate<Func<float,float>>((origCooldown) => {
                        if(cpt && cpt.boostedMissiles > 0) {
                            cpt.boostedMissiles --;
                            return origCooldown/2;
                        }
                        return origCooldown;
                    });
                } else {
                    Debug.LogError("ClassicItems: failed to apply Beating Embryo IL patch: CommandMissile (FixedUpdate)");
                }
            }

            if(subEnable[EquipmentIndex.BFG]) {
                //Find: string "Prefabs/Projectiles/BeamSphere"
                //Then find: CharacterBody.get_damage * 2f (damage argument of FireProjectile)
                ILFound = c.TryGotoNext(
                    x=>x.MatchLdstr("Prefabs/Projectiles/BeamSphere"))
                && c.TryGotoNext(
                    x=>x.MatchCallvirt<CharacterBody>("get_damage"),
                    x=>x.OpCode == OpCodes.Ldc_R4,
                    x=>x.MatchMul());

                if(ILFound) {
                    //Advance cursor to found ldcR4
                    c.Index+=2;
                        
                    //Replace original FireProjectile damage coefficient (2f) with a custom function to check for Embryo-boosted BFG shots
                    //If boosts exist, doubles the damage coefficient to 4f and deducts a boost from CPD; otherwise returns original
                    c.EmitDelegate<Func<float,float>>((origDamage)=>{
                        if(cpt && cpt.boostedBFGs > 0) {
                            cpt.boostedBFGs --;
                            return origDamage*2f;
                        }
                        return origDamage;
                    });
                } else {
                    Debug.LogError("ClassicItems: failed to apply Beating Embryo IL patch: BFG (FixedUpdate)");
                }
            }
        }

        private void IL_ESFireGateway(ILContext il) {
            ILCursor c = new ILCursor(il);
            
            EmbryoComponent cpt = null;
            c.Emit(OpCodes.Ldarg_0);
            c.EmitDelegate<Action<EquipmentSlot>>((slot)=>{
                cpt = slot.characterBody?.GetComponentInChildren<EmbryoComponent>();
            });

            bool ILFound = c.TryGotoNext(
                x=>x.MatchLdstr("Prefabs/NetworkedObjects/Zipline"),
                x=>x.MatchCallOrCallvirt<UnityEngine.Resources>("Load"),
                x=>x.MatchCallOrCallvirt<UnityEngine.Object>("Instantiate"));

            if(ILFound) {
                c.Index += 2;
                c.EmitDelegate<Func<GameObject,GameObject>>((origCtrl) => {
                    if(cpt && cpt.boostedGates > 0) {
                        cpt.boostedGates --;
                        return boostedGatewayPrefab;
                    } else {
                        return origCtrl;
                    }
                });
            } else {
                Debug.LogError("ClassicItems: failed to apply Beating Embryo IL patch: Gateway (FireGateway)");
            }
        }

        private void IL_EntGGFFireBullet(ILContext il) {
            ILCursor c = new ILCursor(il);
            
            //Insert a check for Embryo procs at the top of the function
            bool boost = false;
            c.Emit(OpCodes.Ldarg_0);
            c.EmitDelegate<Action<EntityStates.GoldGat.GoldGatFire>>((ggf)=>{
                var n = ggf.GetFieldValue<NetworkedBodyAttachment>("networkedBodyAttachment");
                boost = Util.CheckRoll(GetCount(n?.attachedBodyObject?.GetComponent<CharacterBody>())*procChance);
            });

            bool ILFound;

            //Find: loading of a value into GoldGatFire.fireFrequency
            ILFound = c.TryGotoNext(
                x=>x.MatchStfld<EntityStates.GoldGat.GoldGatFire>("fireFrequency"));

            if(ILFound) {
                //Double the original fire frequency for boosted shots
                c.EmitDelegate<Func<float,float>>((origFreq) => {
                    return boost ? origFreq*2 : origFreq;
                });
            } else {
                Debug.LogError("ClassicItems: failed to apply Beating Embryo IL patch: GoldGat (FireBullet); target instructions not found");
                ILFailed = true;
            }
        }

        private void IL_JCFixedUpdate(ILContext il) {
            ILCursor c = new ILCursor(il);

            bool ILFound = c.TryGotoNext(
                x=>x.MatchCallOrCallvirt<UnityEngine.Time>("get_fixedDeltaTime"),
                x=>x.MatchAdd(),
                x=>x.MatchStfld<JetpackController>("stopwatch"));

            if(ILFound) {
                c.Index++;
                c.Emit(OpCodes.Ldarg_0);
                c.EmitDelegate<Func<float,JetpackController,float>>((origDecr,jpc)=>{
                    EmbryoComponent cpt = jpc.NetworktargetObject?.GetComponentInChildren<EmbryoComponent>();
                    if(!cpt || cpt.boostedJetTime <= 0) return origDecr;
                    cpt.boostedJetTime -= origDecr;
                    return 0f;
                });
            } else {
                Debug.LogError("ClassicItems: failed to apply Beating Embryo IL patch: Jetpack (FixedUpdate); target instructions not found");
                ILFailed = true;
            }
        }

        private void IL_ESRpcOnEquipmentActivationReceived(ILContext il) {
            ILCursor c = new ILCursor(il);

            bool ILFound = c.TryGotoNext(MoveType.After,
                x=>x.MatchLdstr("activeDuration"),
                x=>x.MatchLdcR4(out _));

            if(ILFound) {
                c.Emit(OpCodes.Ldarg_0);
                c.EmitDelegate<Func<float,EquipmentSlot,float>>((origValue, slot)=>{
                    return (slot.characterBody?.GetComponentInChildren<EmbryoComponent>()?.lastCOUBoosted == true) ? origValue * 2f : origValue;
                });
            } else {
                Debug.LogError("ClassicItems: failed to apply Beating Embryo IL patch: CritOnUse VFX (RpcOnEquipmentActivationReceived); target instructions not found");
                ILFailed = true;
            }
        }
    }

    public class EmbryoRecycleFlag : MonoBehaviour {}
    
    public class EmbryoComponent : NetworkBehaviour {
        public int boostedMissiles = 0;
        public int boostedBFGs = 0;
        [SyncVar]
        public int boostedGates;
        [SyncVar]
        public float boostedJetTime;
        [SyncVar]
        public bool lastCOUBoosted = false;

        public void Awake() {
            boostedGates = 0;
            boostedJetTime = 0f;
        }
    }
}

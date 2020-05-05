﻿using BepInEx;
using MonoMod.Cil;
using R2API;
using R2API.AssetPlus;
using R2API.Utils;
using RoR2;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using BepInEx.Configuration;
using Mono.Cecil.Cil;
using System;
using TMPro;
using UnityEngine.Networking;

//TODO:
// Add missing documentation in... a whole lotta places... whoops.
// Change H3AD-5T V2 to a green item if removing the stomp effect?
// Add lots of missing items!
// Figure out skill modification/overwrites, for e.g. Ancient Scepter
// Watch for R2API.StatsAPI or similar, for use in some items like Bitter Root, Mysterious Vial, Rusty Jetpack
// Find out how to safely and instantaneously change money counter, for cases like Life Savings that shouldn't have the sound effects
// Engineer turrets spammed errors during FixedUpdate and/or RecalculateStats at one point?? Probably resolved now but keep an eye out for things like this

namespace ThinkInvisible.ClassicItems {
    [BepInPlugin(ModGuid, ModName, ModVer)]
    [BepInDependency(R2API.R2API.PluginGUID)]
    [BepInDependency("com.funkfrog_sipondo.sharesuite",BepInDependency.DependencyFlags.SoftDependency)]
    [R2APISubmoduleDependency(nameof(ItemAPI), nameof(LanguageAPI), nameof(ResourcesAPI), nameof(PlayerAPI), nameof(PrefabAPI), nameof(BuffAPI), nameof(CommandHelper))]
    public class ClassicItemsPlugin:BaseUnityPlugin {
        public const string ModVer =
            #if DEBUG
                "0." +
            #endif
            "3.0.1";
        public const string ModName = "ClassicItems";
        public const string ModGuid = "com.ThinkInvisible.ClassicItems";

        private static ConfigFile cfgFile;
        
        public static MiscUtil.FilingDictionary<ItemBoilerplate> masterItemList = new MiscUtil.FilingDictionary<ItemBoilerplate>();
        
        private static ConfigEntry<bool> gCfgHSV2NoStomp;
        private static ConfigEntry<bool> gCfgAllCards;
        private static ConfigEntry<bool> gCfgHideDesc;
        private static ConfigEntry<bool> gCfgSpinMod;
        private static ConfigEntry<bool> gCfgCoolYourJets;

        public static BuffIndex freezeBuff {get;private set;}

        public static bool gHSV2NoStomp {get;private set;}
        public static bool gAllCards {get;private set;}
        public static bool gHideDesc {get;private set;}
        public static bool gSpinMod {get;private set;}
        public static bool gCoolYourJets {get;private set;}

        public ClassicItemsPlugin() {
            #if DEBUG
            Debug.LogWarning("ClassicItems: running test build with debug enabled! If you're seeing this after downloading the mod from Thunderstore, please panic.");
            #endif
            Debug.Log("ClassicItems: loading assets...");
            using(var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ClassicItems.classicitems_assets")) {
                var bundle = AssetBundle.LoadFromStream(stream);
                var provider = new AssetBundleResourcesProvider("@ClassicItems", bundle);
                ResourcesAPI.AddProvider(provider);
            }
            cfgFile = new ConfigFile(Paths.ConfigPath + "\\" + ModGuid + ".cfg", true);
            

            Debug.Log("ClassicItems: loading global configs...");

            gCfgHSV2NoStomp = cfgFile.Bind(new ConfigDefinition("Global.VanillaTweaks", "NoHeadStompV2"), true, new ConfigDescription(
                "If true, removes the hold-space-to-stomp functionality of H3AD-5T V2 (due to overlap in functionality with ClassicItems Headstompers). H3AD-5T V2 will still increase jump height and prevent fall damage."));   
            gCfgAllCards = cfgFile.Bind(new ConfigDefinition("Global.VanillaTweaks", "AllCards"), false, new ConfigDescription(
                "If true, replaces the pickup models for most vanilla items and equipments with trading cards."));
            gCfgHideDesc = cfgFile.Bind(new ConfigDefinition("Global.VanillaTweaks", "HideDesc"), false, new ConfigDescription(
                "If true, hides the dynamic description text on trading card-style pickup models. Enabling this may slightly improve performance."));
            gCfgSpinMod = cfgFile.Bind(new ConfigDefinition("Global.VanillaTweaks", "SpinMod"), true, new ConfigDescription(
                "If true, trading card-style pickup models will have customized spin behavior which makes descriptions more readable. Disabling this may slightly improve compatibility and performance."));
            gCfgCoolYourJets = cfgFile.Bind(new ConfigDefinition("Global.Interaction", "CoolYourJets"), true, new ConfigDescription(
                "If true, disables the Rusty Jetpack gravity reduction while Photon Jetpack is active. If false, there shall be yeet."));

            gHSV2NoStomp = gCfgHSV2NoStomp.Value;
            gAllCards = gCfgAllCards.Value;
            gHideDesc = gCfgHideDesc.Value;
            gSpinMod = gCfgSpinMod.Value;
            gCoolYourJets = gCfgCoolYourJets.Value;

            Debug.Log("ClassicItems: instantiating item classes...");

            foreach(Type type in Assembly.GetAssembly(typeof(ItemBoilerplate)).GetTypes().Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(ItemBoilerplate)))) {
                masterItemList.Add((ItemBoilerplate)Activator.CreateInstance(type));
            }

            Debug.Log("ClassicItems: loading item configs...");

            foreach(ItemBoilerplate x in masterItemList) {
                x.SetupConfig(cfgFile);
            }

            masterItemList.RemoveWhere(x=>x.itemEnabled==false);

            Debug.Log("ClassicItems: registering item attributes...");

            foreach(ItemBoilerplate x in masterItemList) {
                x.SetupAttributes();
                Debug.Log("CI"+x.itemCodeName + ": " + (x.itemIsEquipment ? ("EQP"+((int)x.regIndexEqp).ToString()) : ((int)x.regIndex).ToString()));
            }
        }

        #if DEBUG
        public void Update() {
            var i3 = Input.GetKeyDown(KeyCode.F3);
            var i4 = Input.GetKeyDown(KeyCode.F4);
            var i5 = Input.GetKeyDown(KeyCode.F5);
            var i6 = Input.GetKeyDown(KeyCode.F6);
            var i7 = Input.GetKeyDown(KeyCode.F7);
            if (i3 || i4 || i5 || i6 || i7) {
                var trans = PlayerCharacterMasterController.instances[0].master.GetBodyObject().transform;

                List<PickupIndex> spawnList;
                if(i3) spawnList = Run.instance.availableTier1DropList;
                else if(i4) spawnList = Run.instance.availableTier2DropList;
                else if(i5) spawnList = Run.instance.availableTier3DropList;
                else if(i6) spawnList = Run.instance.availableEquipmentDropList;
                else spawnList = Run.instance.availableLunarDropList;
                PickupDropletController.CreatePickupDroplet(spawnList[Run.instance.spawnRng.RangeInt(0,spawnList.Count)], trans.position, new Vector3(0f, -5f, 0f));
            }
        }
        #endif
        
        internal static Type nodeRefType;
        internal static Type nodeRefTypeArr;

        public void Awake() {
            Debug.Log("ClassicItems: performing plugin setup...");
            
            CommandHelper.AddToConsoleWhenReady();
            nodeRefType = typeof(DirectorCore).GetNestedTypes(BindingFlags.NonPublic).First(t=>t.Name == "NodeReference");
            nodeRefTypeArr = nodeRefType.MakeArrayType();

            Debug.Log("ClassicItems: tweaking vanilla stuff...");

            //Remove the H3AD-5T V2 state transition from idle to stomp, as Headstompers has similar functionality
            if(gHSV2NoStomp)
                IL.EntityStates.Headstompers.HeadstompersIdle.FixedUpdate += IL_ESHeadstompersIdleFixedUpdate;
            On.RoR2.PickupCatalog.Init += On_PickupCatalogInit;
            if(gSpinMod)
                IL.RoR2.PickupDisplay.Update += IL_PickupDisplayUpdate;

            Debug.Log("ClassicItems: registering shared buffs...");
            //used only for purposes of Death Mark; applied by Permafrost and Snowglobe
            var freezeBuffDef = new CustomBuff(new BuffDef {
                buffColor = Color.cyan,
                canStack = false,
                isDebuff = true,
                name = "CIFreeze",
                iconPath = "@ClassicItems:Assets/ClassicItems/icons/permafrost_icon.png"
            });
            freezeBuff = BuffAPI.Add(freezeBuffDef);

            Debug.Log("ClassicItems: registering item behaviors...");

            foreach(ItemBoilerplate x in masterItemList) {
                x.SetupBehavior();
            }

            Debug.Log("ClassicItems: done!");
        }
        public void IL_PickupDisplayUpdate(ILContext il) {
            ILCursor c = new ILCursor(il);

            
            bool ILFound = c.TryGotoNext(MoveType.After,
                x=>x.MatchLdfld<PickupDisplay>("modelObject"));
            GameObject puo = null;
            if(ILFound) {
                c.Emit(OpCodes.Dup);
                c.EmitDelegate<Action<GameObject>>(x=>{
                    puo=x;
                });
            } else {
                Debug.LogError("ClassicItems: failed to apply vanilla IL patch (pickup model spin modifier)");
                return;
            }

            ILFound = c.TryGotoNext(MoveType.After,
                x=>x.MatchLdarg(0),
                x=>x.MatchLdfld<PickupDisplay>("spinSpeed"),
                x=>x.MatchLdarg(0),
                x=>x.MatchLdfld<PickupDisplay>("localTime"),
                x=>x.MatchMul());
            if(ILFound) {
                c.EmitDelegate<Func<float,float>>((origAngle) => {
                    if(!puo.GetComponent<SpinModFlag>() || !NetworkClient.active) return origAngle;
                    var body = PlayerCharacterMasterController.instances[0].master.GetBody();
                    if(!body) return origAngle;
                    return Util.QuaternionSafeLookRotation(body.coreTransform.position - puo.transform.position).eulerAngles.y
                        + (float)Math.Tanh(((origAngle/100.0f) % 6.2832f - 3.1416f) * 2f) * 180f
                        + 180f
                        - (puo.transform.parent?.eulerAngles.y ?? 0f);
                });
            } else {
                Debug.LogError("ClassicItems: failed to apply vanilla IL patch (pickup model spin modifier)");
            }

        }
        public void IL_ESHeadstompersIdleFixedUpdate(ILContext il) {
            ILCursor c = new ILCursor(il);
            bool ILFound = c.TryGotoNext(
                x=>x.MatchLdarg(0),
                x=>x.OpCode == OpCodes.Ldfld,
                x=>x.MatchNewobj<EntityStates.Headstompers.HeadstompersCharge>(),
                x=>x.MatchCallOrCallvirt<EntityStateMachine>("SetNextState"));
            if(ILFound) {
                c.RemoveRange(4);
            } else {
                Debug.LogError("ClassicItems: failed to apply vanilla IL patch (HSV2NoStomp)");
            }
        }
        public void On_PickupCatalogInit(On.RoR2.PickupCatalog.orig_Init orig) {
            orig();

            Debug.Log("ClassicItems: processing pickup models...");

            foreach(ItemBoilerplate bpl in masterItemList) {
                PickupIndex pind;
                if(bpl.itemIsEquipment) pind = PickupCatalog.FindPickupIndex(bpl.regIndexEqp);
                else pind = PickupCatalog.FindPickupIndex(bpl.regIndex);
                var pickup = PickupCatalog.GetPickupDef(pind);
                Debug.Log(pickup.internalName);
                pickup.displayPrefab = pickup.displayPrefab.InstantiateClone(pickup.internalName + "CICardPrefab", false);
            }

            if(gAllCards) {
                var eqpCardPrefab = Resources.Load<GameObject>("@ClassicItems:Assets/ClassicItems/models/VOvr/EqpCard.prefab");
                var lunarCardPrefab = Resources.Load<GameObject>("@ClassicItems:Assets/ClassicItems/models/VOvr/LunarCard.prefab");
                var t1CardPrefab = Resources.Load<GameObject>("@ClassicItems:Assets/ClassicItems/models/VOvr/CommonCard.prefab");
                var t2CardPrefab = Resources.Load<GameObject>("@ClassicItems:Assets/ClassicItems/models/VOvr/UncommonCard.prefab");
                var t3CardPrefab = Resources.Load<GameObject>("@ClassicItems:Assets/ClassicItems/models/VOvr/RareCard.prefab");
                var bossCardPrefab = Resources.Load<GameObject>("@ClassicItems:Assets/ClassicItems/models/VOvr/BossCard.prefab");

                int replacedItems = 0;
                int replacedEqps = 0;

                foreach(var pickup in PickupCatalog.allPickups) {
                    GameObject npfb;
                    if(pickup.interactContextToken == "EQUIPMENT_PICKUP_CONTEXT") {
                        if(pickup.equipmentIndex >= EquipmentIndex.Count || pickup.equipmentIndex < 0) continue;
                        var eqp = EquipmentCatalog.GetEquipmentDef(pickup.equipmentIndex);
                        if(!eqp.canDrop) continue;
                        npfb = eqpCardPrefab;
                        replacedEqps ++;
                    } else if(pickup.interactContextToken == "ITEM_PICKUP_CONTEXT") {
                        if(pickup.itemIndex >= ItemIndex.Count || pickup.itemIndex < 0) continue;
                        var item = ItemCatalog.GetItemDef(pickup.itemIndex);
                        switch(item.tier) {
                            case ItemTier.Tier1:
                                npfb = t1CardPrefab; break;
                            case ItemTier.Tier2:
                                npfb = t2CardPrefab; break;
                            case ItemTier.Tier3:
                                npfb = t3CardPrefab; break;
                            case ItemTier.Lunar:
                                npfb = lunarCardPrefab; break;
                            case ItemTier.Boss:
                                npfb = bossCardPrefab; break;
                            default:
                                continue;
                        }
                        replacedItems ++;
                    } else continue;
                    pickup.displayPrefab = npfb.InstantiateClone(pickup.internalName + "CICardPrefab", false);
                }

                Debug.Log("ClassicItems: replaced " + replacedItems + " item models and " + replacedEqps + " equipment models.");
            }

            int replacedDescs = 0;

            var tmpfont = Resources.Load<TMP_FontAsset>("tmpfonts/misc/tmpRiskOfRainFont Bold OutlineSDF");
            var tmpmtl = Resources.Load<Material>("tmpfonts/misc/tmpRiskOfRainFont Bold OutlineSDF");

            foreach(var pickup in PickupCatalog.allPickups) {
                var ctsf = pickup.displayPrefab?.transform;
                if(!ctsf) continue;
                var cfront = ctsf.Find("cardfront");
                if(cfront == null) continue;
                var croot = cfront.Find("carddesc");
                var cnroot = cfront.Find("cardname");
                var csprite = ctsf.Find("ovrsprite");
                
                csprite.GetComponent<MeshRenderer>().material.mainTexture = pickup.iconTexture;

                if(gSpinMod)
                    pickup.displayPrefab.AddComponent<SpinModFlag>();

                string pname;
                string pdesc;
                Color prar = new Color(1f, 0f, 1f);
                if(pickup.interactContextToken == "EQUIPMENT_PICKUP_CONTEXT") {
                    var eqp = EquipmentCatalog.GetEquipmentDef(pickup.equipmentIndex);
                    if(eqp == null) continue;
                    pname = Language.GetString(eqp.nameToken);
                    pdesc = Language.GetString(eqp.descriptionToken);
                    prar = new Color(1f, 0.7f, 0.4f);
                } else if(pickup.interactContextToken == "ITEM_PICKUP_CONTEXT") {
                    var item = ItemCatalog.GetItemDef(pickup.itemIndex);
                    if(item == null) continue;
                    pname = Language.GetString(item.nameToken);
                    pdesc = Language.GetString(item.descriptionToken);
                    switch(item.tier) {
                        case ItemTier.Boss: prar = new Color(1f, 1f, 0f); break;
                        case ItemTier.Lunar: prar = new Color(0f, 0.6f, 1f); break;
                        case ItemTier.Tier1: prar = new Color(0.8f, 0.8f, 0.8f); break;
                        case ItemTier.Tier2: prar = new Color(0.2f, 1f, 0.2f); break;
                        case ItemTier.Tier3: prar = new Color(1f, 0.2f, 0.2f); break;
                    }
                } else continue;

                if(gHideDesc) {
                    Destroy(croot.gameObject);
                    Destroy(cnroot.gameObject);
                } else {
                    var cdsc = croot.gameObject.AddComponent<TextMeshPro>();
                    cdsc.richText = true;
                    cdsc.enableWordWrapping = true;
                    cdsc.alignment = TextAlignmentOptions.Center;
                    cdsc.margin = new Vector4(4f, 1.874178f, 4f, 1.015695f);
                    cdsc.enableAutoSizing = true;
                    cdsc.overrideColorTags = false;
                    cdsc.fontSizeMin = 1;
                    cdsc.fontSizeMax = 8;
                    _ = cdsc.renderer;
                    cdsc.font = tmpfont;
                    cdsc.material = tmpmtl;
                    cdsc.color = Color.black;
                    cdsc.text = pdesc;

                    var cname = cnroot.gameObject.AddComponent<TextMeshPro>();
                    cname.richText = true;
                    cname.enableWordWrapping = false;
                    cname.alignment = TextAlignmentOptions.Center;
                    cname.margin = new Vector4(6.0f, 1.2f, 6.0f, 1.4f);
                    cname.enableAutoSizing = true;
                    cname.overrideColorTags = true;
                    cname.fontSizeMin = 1;
                    cname.fontSizeMax = 10;
                    _ = cname.renderer;
                    cname.font = tmpfont;
                    cname.material = tmpmtl;
                    cname.outlineColor = prar;
                    cname.outlineWidth = 0.15f;
                    cname.color = Color.black;
                    cname.fontStyle = FontStyles.Bold;
                    cname.text = pname;
                }
                replacedDescs ++;
            }
            Debug.Log("ClassicItems: " + (gHideDesc ? "destroyed " : "inserted ") + replacedDescs + " pickup model descriptions.");
        }

        private RoR2.UI.LogBook.Entry[] On_LogbookBuildPickupEntries(On.RoR2.UI.LogBook.LogBookController.orig_BuildPickupEntries orig) {
            var retv = orig();
            Debug.Log("ClassicItems: processing logbook models...");
            int replacedModels = 0;
            foreach(RoR2.UI.LogBook.Entry e in retv) {
                if(!(e.extraData is PickupIndex)) continue;
                if(e.modelPrefab.transform.Find("cardfront")) {
                    e.modelPrefab = PickupCatalog.GetPickupDef((PickupIndex)e.extraData).displayPrefab;
                    replacedModels++;
                }
            }
            Debug.Log("ClassicItems: modified " + replacedModels + " logbook models.");
            return retv;
        }
    }

    public class SpinModFlag : MonoBehaviour {}
}

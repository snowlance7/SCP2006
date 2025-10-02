using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using LethalLib.Modules;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;

namespace SCP2006
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInDependency(LethalLib.Plugin.ModGUID)]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin PluginInstance;
        public static ManualLogSource LoggerInstance;
        private readonly Harmony harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        public static PlayerControllerB localPlayer { get { return GameNetworkManager.Instance.localPlayerController; } }
        public static PlayerControllerB PlayerFromId(ulong id) { return StartOfRound.Instance.allPlayerScripts.Where(x => x.actualClientId == id).First(); }
        public static bool IsServerOrHost { get { return NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost; } }

        public static AssetBundle? ModAssets;

        // Configs

        // SCP-4666 Configs
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public static ConfigEntry<string> config2006LevelRarities;
        public static ConfigEntry<string> config2006CustomLevelRarities;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        private void Awake()
        {
            if (PluginInstance == null)
            {
                PluginInstance = this;
            }

            LoggerInstance = PluginInstance.Logger;

            harmony.PatchAll();

            InitializeNetworkBehaviours();

            InitConfigs();

            // Loading Assets
            string sAssemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            ModAssets = AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(Info.Location), "spooky_assets"));
            if (ModAssets == null)
            {
                Logger.LogError($"Failed to load custom assets.");
                return;
            }
            LoggerInstance.LogDebug($"Got AssetBundle at: {Path.Combine(sAssemblyLocation, "spooky_assets")}");

            /*Item Knife = ModAssets.LoadAsset<Item>("Assets/ModAssets/Knife/YulemanKnifeItem.asset");
            if (Knife == null) { LoggerInstance.LogError("Error: Couldnt get YulemanKnifeItem from assets"); return; }
            LoggerInstance.LogDebug($"Got YulemanKnife prefab");
            Knife.minValue = configKnifeMinValue.Value;
            Knife.maxValue = configKnifeMaxValue.Value;
            LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(Knife.spawnPrefab);
            Utilities.FixMixerGroups(Knife.spawnPrefab);
            LethalLib.Modules.Items.RegisterScrap(Knife);*/

            EnemyType SCP2006 = ModAssets.LoadAsset<EnemyType>("");
            if (SCP2006 == null) { LoggerInstance.LogError("Error: Couldnt get SCP-2006 from assets"); return; }
            LoggerInstance.LogDebug($"Got SCP-2006 prefab");
            TerminalNode SpookyTN = ModAssets.LoadAsset<TerminalNode>("");
            TerminalKeyword SpookyTK = ModAssets.LoadAsset<TerminalKeyword>("");
            LoggerInstance.LogDebug("Registering enemy network prefab...");
            LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(SCP2006.enemyPrefab);
            LoggerInstance.LogDebug("Registering enemy...");
            Enemies.RegisterEnemy(SCP2006, GetLevelRarities(config2006LevelRarities.Value), GetCustomLevelRarities(config2006CustomLevelRarities.Value), SpookyTN, SpookyTK);

            // Finished
            Logger.LogInfo($"{MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} has loaded!");
        }

        void InitConfigs()
        {
            // SCP-4666 Configs
            config2006LevelRarities = Config.Bind("SCP-2006 Rarities", "Level Rarities", "All: 50, Modded: 50", "Rarities for each level. See default for formatting.");
            config2006CustomLevelRarities = Config.Bind("SCP-2006 Rarities", "Custom Level Rarities", "", "Rarities for modded levels. Same formatting as level rarities.");
        }

        public Dictionary<Levels.LevelTypes, int> GetLevelRarities(string levelsString)
        {
            try
            {
                Dictionary<Levels.LevelTypes, int> levelRaritiesDict = new Dictionary<Levels.LevelTypes, int>();

                if (levelsString != null && levelsString != "")
                {
                    string[] levels = levelsString.Split(',');

                    foreach (string level in levels)
                    {
                        string[] levelSplit = level.Split(':');
                        if (levelSplit.Length != 2) { continue; }
                        string levelType = levelSplit[0].Trim();
                        string levelRarity = levelSplit[1].Trim();

                        if (Enum.TryParse<Levels.LevelTypes>(levelType, out Levels.LevelTypes levelTypeEnum) && int.TryParse(levelRarity, out int levelRarityInt))
                        {
                            levelRaritiesDict.Add(levelTypeEnum, levelRarityInt);
                        }
                        else
                        {
                            LoggerInstance.LogError($"Error: Invalid level rarity: {levelType}:{levelRarity}");
                        }
                    }
                }
                return levelRaritiesDict;
            }
            catch (Exception e)
            {
                Logger.LogError($"Error: {e}");
                return null!;
            }
        }

        public Dictionary<string, int> GetCustomLevelRarities(string levelsString)
        {
            try
            {
                Dictionary<string, int> customLevelRaritiesDict = new Dictionary<string, int>();

                if (levelsString != null)
                {
                    string[] levels = levelsString.Split(',');

                    foreach (string level in levels)
                    {
                        string[] levelSplit = level.Split(':');
                        if (levelSplit.Length != 2) { continue; }
                        string levelType = levelSplit[0].Trim();
                        string levelRarity = levelSplit[1].Trim();

                        if (int.TryParse(levelRarity, out int levelRarityInt))
                        {
                            customLevelRaritiesDict.Add(levelType, levelRarityInt);
                        }
                        else
                        {
                            LoggerInstance.LogError($"Error: Invalid level rarity: {levelType}:{levelRarity}");
                        }
                    }
                }
                return customLevelRaritiesDict;
            }
            catch (Exception e)
            {
                Logger.LogError($"Error: {e}");
                return null!;
            }
        }

        public static void RebuildRig(PlayerControllerB pcb)
        {
            if (pcb != null && pcb.playerBodyAnimator != null)
            {
                pcb.playerBodyAnimator.WriteDefaultValues();
                pcb.playerBodyAnimator.GetComponent<RigBuilder>()?.Build();
            }
        }

        public static void FreezePlayer(PlayerControllerB player, bool value)
        {
            player.disableInteract = value;
            player.disableLookInput = value;
            player.disableMoveInput = value;
        }

        public static void MakePlayerInvisible(PlayerControllerB player, bool value)
        {
            GameObject scavengerModel = player.gameObject.transform.Find("ScavengerModel").gameObject;
            if (scavengerModel == null) { LoggerInstance.LogError("ScavengerModel not found"); return; }
            scavengerModel.transform.Find("LOD1").gameObject.SetActive(!value);
            scavengerModel.transform.Find("LOD2").gameObject.SetActive(!value);
            scavengerModel.transform.Find("LOD3").gameObject.SetActive(!value);
            player.playerBadgeMesh.gameObject.SetActive(value);
        }

        public static bool IsPlayerChild(PlayerControllerB player)
        {
            return player.thisPlayerBody.localScale.y < 1f;
        }

        public void AllowPlayerDeathAfterDelay(float delay)
        {
            IEnumerator AllowPlayerDeathAfterDelay(float delay)
            {
                yield return new WaitForSeconds(delay);
                StartOfRound.Instance.allowLocalPlayerDeath = true;
            }

            StartCoroutine(AllowPlayerDeathAfterDelay(delay));
        }

        private static void InitializeNetworkBehaviours()
        {
            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var method in methods)
                {
                    var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                    if (attributes.Length > 0)
                    {
                        method.Invoke(null, null);
                    }
                }
            }
            LoggerInstance.LogDebug("Finished initializing network behaviours");
        }
    }
}

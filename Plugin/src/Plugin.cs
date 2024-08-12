using System.Reflection;
using UnityEngine;
using BepInEx;
using LethalLib.Modules;
using BepInEx.Logging;
using System.IO;
using TaysonEnemy.Configuration;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace TaysonEnemy {
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInDependency(LethalLib.Plugin.ModGUID)] 
    public class Plugin : BaseUnityPlugin {
        internal static new ManualLogSource Logger = null!;
        internal static PluginConfig BoundConfig { get; private set; } = null!;
        public static AssetBundle? ModAssets;
        public static Dictionary<string, AudioClip>? soundEffects;

        private void Awake() {
            Logger = base.Logger;

            // If you don't want your mod to use a configuration file, you can remove this line, Configuration.cs, and other references.
            BoundConfig = new PluginConfig(base.Config);

            // This should be ran before Network Prefabs are registered.
            InitializeNetworkBehaviours();

            // We load the asset bundle that should be next to our DLL file, with the specified name.
            // You may want to rename your asset bundle from the AssetBundle Browser in order to avoid an issue with
            // asset bundle identifiers being the same between multiple bundles, allowing the loading of only one bundle from one mod.
            // In that case also remember to change the asset bundle copying code in the csproj.user file.
            var bundleName = "taysonenemybundle";
            ModAssets = AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(Info.Location), bundleName));
            if (ModAssets == null) {
                Logger.LogError($"Failed to load custom assets.");
                return;
            }
            Logger.LogInfo("Loaded the bundle");
            // We load our assets from our asset bundle. Remember to rename them both here and in our Unity project.
            var TaysonEnemy = ModAssets.LoadAsset<EnemyType>("TaysonEnemy");
            var TaysonEnemyTN = ModAssets.LoadAsset<TerminalNode>("TaysonEnemyTN");
            var TaysonEnemyTK = ModAssets.LoadAsset<TerminalKeyword>("TaysonEnemyTK");
            var assetNames = ModAssets.GetAllAssetNames();
            soundEffects  = new Dictionary<string, AudioClip>();
            soundEffects.Add("running", ModAssets.LoadAsset<AudioClip>("running.sfx"));
            soundEffects.Add("purring", ModAssets.LoadAsset<AudioClip>("purring.sfx"));
            if (TaysonEnemy == null || TaysonEnemyTK == null || TaysonEnemyTN == null)
            {
                Logger.LogError("Failed to load asset(s)");
                if (TaysonEnemy == null)
                    Debug.LogError("Failed to load the model");
                if (TaysonEnemyTK == null)
                    Debug.LogError("Failed to load the terminal keywords");
                if (TaysonEnemyTN == null)
                    Debug.LogError("Failed to load the terminal node");
                return;
            }
            else
            {
                Logger.LogInfo("Succesfully loaded all assets");
            }
    
            // Optionally, we can list which levels we want to add our enemy to, while also specifying the spawn weight for each.
            /*
            var TaysonEnemyLevelRarities = new Dictionary<Levels.LevelTypes, int> {
                {Levels.LevelTypes.ExperimentationLevel, 10},
                {Levels.LevelTypes.AssuranceLevel, 40},
                {Levels.LevelTypes.VowLevel, 20},
                {Levels.LevelTypes.OffenseLevel, 30},
                {Levels.LevelTypes.MarchLevel, 20},
                {Levels.LevelTypes.RendLevel, 50},
                {Levels.LevelTypes.DineLevel, 25},
                // {Levels.LevelTypes.TitanLevel, 33},
                // {Levels.LevelTypes.All, 30},     // Affects unset values, with lowest priority (gets overridden by Levels.LevelTypes.Modded)
                {Levels.LevelTypes.Modded, 60},     // Affects values for modded moons that weren't specified
            };
            // We can also specify custom level rarities
            var TaysonEnemyCustomLevelRarities = new Dictionary<string, int> {
                {"EGyptLevel", 50},
                {"46 Infernis", 69},    // Either LLL or LE(C) name can be used, LethalLib will handle both
            };
            */

            // Network Prefabs need to be registered. See https://docs-multiplayer.unity3d.com/netcode/current/basics/object-spawning/
            // LethalLib registers prefabs on GameNetworkManager.Start.
            NetworkPrefabs.RegisterNetworkPrefab(TaysonEnemy.enemyPrefab);

            // For different ways of registering your enemy, see https://github.com/EvaisaDev/LethalLib/blob/main/LethalLib/Modules/Enemies.cs
            Enemies.RegisterEnemy(TaysonEnemy, BoundConfig.SpawnWeight.Value, Levels.LevelTypes.All, TaysonEnemyTN, TaysonEnemyTK);
            // For using our rarity tables, we can use the following:
            // Enemies.RegisterEnemy(TaysonEnemy, TaysonEnemyLevelRarities, TaysonEnemyCustomLevelRarities, TaysonEnemyTN, TaysonEnemyTK);
            
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
        }

        private static void InitializeNetworkBehaviours() {
            // See https://github.com/EvaisaDev/UnityNetcodePatcher?tab=readme-ov-file#preparing-mods-for-patching
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
        } 
    }
}
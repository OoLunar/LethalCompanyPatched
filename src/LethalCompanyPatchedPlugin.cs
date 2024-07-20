using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using OoLunar.LethalCompanyPatched.Patches;

namespace OoLunar.LethalCompanyPatched
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public sealed class LethalCompanyPatchedPlugin : BaseUnityPlugin
    {
        private readonly Harmony _harmony = new(MyPluginInfo.PLUGIN_GUID);

        internal static ManualLogSource StaticLogger = null!;
        internal static ConfigEntry<float> InstantSprint = null!;
        internal static ConfigEntry<float> Slipperiness = null!;
        internal static ConfigEntry<float> JumpDelay = null!;
        internal static ConfigEntry<bool> ShowHudPercentages = null!;
        internal static ConfigEntry<bool> CrouchHold = null!;

        [SuppressMessage("Roslyn", "IDE0051", Justification = "Unity will call this method through reflection. Should've been an interface method but w/e.")]
        private void Awake()
        {
            // Plugin startup logic
            Logger.LogDebug($"{MyPluginInfo.PLUGIN_NAME} started loading!");

            StaticLogger = Logger;
            CrouchHold = Config.Bind("General", "crouchHold", true, "Enable/disable crouch hold. If disabled, crouch functions as a toggle with the additional behavior of going back into a crouch upon landing from a crouch jump.");
            JumpDelay = Config.Bind("General", "jumpDelay", 0.0f, "Configure the time till jump, defaults to instant with 0.0. Vanilla is 0.15");
            InstantSprint = Config.Bind("General", "instantSprint", 2.25f, "How fast to accelerate to sprint value of 2.25. 2.25 is the max, so it's instant acceleration. Vanilla is 1.0");
            Slipperiness = Config.Bind("General", "slipperiness", 10f, "The amount of slipperiness when running and changing direction. 10-15f is a good value for little to no slippery feeling. Vanilla is 5.0");
            ShowHudPercentages = Config.Bind("General", "showHealthStamina", true, "Show your health and sprint/stamina % on the HUD.");
            
            foreach (Type type in typeof(LethalCompanyPatchedPlugin).Assembly.GetTypes())
            {
                // Find all types in this assembly that have the LethalPatchAttribute applied to them.
                if (type.GetCustomAttribute<LethalPatchAttribute>() is not null)
                {
                    _harmony.PatchAll(type);
                }
            }
            _harmony.PatchAll(typeof(HUDManagerPatch));
            

            Logger.LogInfo($"{MyPluginInfo.PLUGIN_NAME} finished loading!");
        }
    }
}

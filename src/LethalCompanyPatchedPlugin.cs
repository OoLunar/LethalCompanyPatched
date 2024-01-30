using System;
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
        internal static ConfigEntry<bool> InstantJump = null!;
        internal static ConfigEntry<bool> ShowHudPercentages = null!;

        private void Awake()
        {
            // Plugin startup logic
            Logger.LogDebug($"{MyPluginInfo.PLUGIN_NAME} started loading!");

            StaticLogger = Logger;
            InstantJump = Config.Bind("General", "instantJump", true, "Enable/disable instant jump. Removes the delay with jumping when enabled.");
            InstantSprint = Config.Bind("General", "instantSprint", 2.25f, "How fast to accelerate to sprint value of 2.25. 2.25 is the max, so it's instant acceleration.");
            Slipperiness = Config.Bind("General", "slipperiness", 10f, "The amount of slipperiness when running and changing direction. 10-15f is a good value for little to no slippery feeling.");
            ShowHudPercentages = Config.Bind("General", "showHealthStamina", true, "Show your health and sprint/stamina % on the HUD.");
            foreach (Type type in typeof(LethalCompanyPatchedPlugin).Assembly.GetTypes())
            {
                // Find all types in this assembly that inherit from `IPatch` and pass them to Harmony.
                if (type.GetInterface(nameof(IPatch)) != null)
                {
                    _harmony.PatchAll(type);
                }
            }

            Logger.LogInfo($"{MyPluginInfo.PLUGIN_NAME} finished loading!");
        }
    }
}

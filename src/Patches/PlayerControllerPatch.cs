using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace OoLunar.LethalCompanyPatched.Patches
{
    [HarmonyPatch(typeof(PlayerControllerB)), LethalPatch]
    internal class PlayerControllerPatch
    {
        private static readonly FieldInfo _playerCarryWeight = typeof(PlayerControllerB).GetField("carryWeight");
        private static readonly FieldInfo? _slipperinessConfigField = AccessTools.Field(typeof(LethalCompanyPatchedPlugin), nameof(LethalCompanyPatchedPlugin.Slipperiness));
        private static readonly FieldInfo? _jumpDelayConfigField = AccessTools.Field(typeof(LethalCompanyPatchedPlugin), nameof(LethalCompanyPatchedPlugin.JumpDelay));
        private static readonly FieldInfo? _instantSprintConfigField = AccessTools.Field(typeof(LethalCompanyPatchedPlugin), nameof(LethalCompanyPatchedPlugin.InstantSprint));
        private static readonly MethodInfo? _configEntryFloatValueMethod = AccessTools.Method(typeof(ConfigEntry<float>), "get_Value");

        private static bool _tempCrouch;
        private static readonly int Crouching = Animator.StringToHash("crouching");
        private static readonly int StartCrouching = Animator.StringToHash("startCrouching");

        private static int? FindInstruction(List<CodeInstruction> instructions, Predicate<int> predicate, int lookAhead = 0, int lookBehind = 0)
        {
            int max_index = instructions.Count - lookAhead;
            for (int index = lookBehind; index < max_index; index++)
            {
                if (predicate.Invoke(index))
                {
                    return index;
                }
            }

            return null;
        }

        private static void ReplaceInstruction(List<CodeInstruction> instructions, List<CodeInstruction> new_instructions, Predicate<int> predicate, int lookAhead = 0, int lookBehind = 0)
        {
            if (FindInstruction(instructions, predicate, lookAhead, lookBehind) is { } i)
            {
                instructions.RemoveAt(i);
                instructions.InsertRange(i, new_instructions);
            }
            else
            {
                LethalCompanyPatchedPlugin.StaticLogger.LogError("Failed to replace instruction");
            }
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(PlayerControllerB), "PlayerJump", MethodType.Enumerator)]
        public static IEnumerable<CodeInstruction> RemoveJumpDelay(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> list = [.. instructions];
            List<CodeInstruction> new_instructions =
            [
                new CodeInstruction(OpCodes.Ldsfld, _jumpDelayConfigField),
                new CodeInstruction(OpCodes.Callvirt, _configEntryFloatValueMethod),
            ];

            ReplaceInstruction(list, new_instructions, i => list[i + 1].opcode == OpCodes.Newobj && (list[i + 1].operand as ConstructorInfo)?.DeclaringType == typeof(WaitForSeconds), 1);
            LethalCompanyPatchedPlugin.StaticLogger.LogDebug("Patched Instant-Jump");
            return list;
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(PlayerControllerB), "Update")]
        public static IEnumerable<CodeInstruction> FixSlipperiness(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> list = [.. instructions];
            List<CodeInstruction> new_instructions =
            [
                new CodeInstruction(OpCodes.Ldsfld, _slipperinessConfigField),
                new CodeInstruction(OpCodes.Callvirt, _configEntryFloatValueMethod),
            ];

            // list[i] = new CodeInstruction(OpCodes.Ldc_R4, LethalCompanyPatchedPlugin.Slipperiness.Value);
            // Replace (5.0 / (this.carryWeight * 1.5)) with (LethalCompanyPatchedPlugin.Slipperiness.Value / (this.carryWeight * 1.5))
            ReplaceInstruction(list, new_instructions, i => list[i].opcode == OpCodes.Ldc_R4 && Mathf.Approximately((float)list[i].operand, 5f) && list[i + 2].LoadsField(_playerCarryWeight) && list[i + 3].opcode == OpCodes.Ldc_R4 && Mathf.Approximately((float)list[i + 3].operand, 1.5f), 3);
            LethalCompanyPatchedPlugin.StaticLogger.LogDebug("Patched Slipperiness");
            return list;
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(PlayerControllerB), "Update")]
        public static IEnumerable<CodeInstruction> InstantSprint(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> list = [.. instructions];
            List<CodeInstruction> new_instructions =
            [
                new CodeInstruction(OpCodes.Ldsfld, _instantSprintConfigField),
                new CodeInstruction(OpCodes.Callvirt, _configEntryFloatValueMethod),
            ];

            // list[i + 2] = new CodeInstruction(OpCodes.Ldc_R4, LethalCompanyPatchedPlugin.InstantSprint.Value);
            // Replace Time.DeltaTime * 1f with Time.DeltaTime * LethalCompanyPatchedPlugin.InstantSprint.Value
            ReplaceInstruction(list, new_instructions, i => list[i - 3].opcode == OpCodes.Ldfld && list[i - 3].ToString() == "ldfld float GameNetcodeStuff.PlayerControllerB::sprintMultiplier" && list[i - 2].opcode == OpCodes.Ldc_R4 && Mathf.Approximately((float)list[i - 2].operand, 2.25f) && list[i].opcode == OpCodes.Ldc_R4 && Mathf.Approximately((float)list[i].operand, 1f), 0, 3);
            LethalCompanyPatchedPlugin.StaticLogger.LogDebug("Patched Instant-Sprint");
            return list;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerControllerB), "Update")]
        public static void HoldCrouch(PlayerControllerB __instance)
        {
            PlayerControllerB? playerController = GameNetworkManager.Instance.localPlayerController;

            // Testing conditions where the player crouch state cannot be changed
            if (!LethalCompanyPatchedPlugin.CrouchHold.Value
                || playerController == null
                || (__instance.IsOwner && __instance.isPlayerControlled && (!__instance.IsServer || __instance.isHostPlayerObject)) // Player put on the mask
                || __instance.isTestingPlayer // Unsure?
                || __instance.inTerminalMenu // Player is in a terminal
                || __instance.isTypingChat // Player is typing in chat
                || __instance.isPlayerDead // Player is dead
                || __instance.quickMenuManager.isMenuOpen // Player has the pause menu open
                || IngamePlayerSettings.Instance.playerInput.actions.FindAction("Crouch", true).IsPressed() // Player is holding the crouch button
                || !playerController.playerBodyAnimator.GetBool(Crouching) // Other players see that this player is not crouching
                || !CanJump(__instance)) // Player is in a space where they cannot jump/must crouch
            {
                return;
            }

            // The player is no longer holding the crouch button, OR the player was forced to uncrouch, OR crouch hold is disabled
            playerController.isCrouching = false;
            playerController.playerBodyAnimator.SetBool(Crouching, false);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(PlayerControllerB), "Jump_performed")]
        public static void PreJump(PlayerControllerB __instance)
        {
            if (!__instance.isCrouching || !CanJump(__instance))
            {
                return;
            }

            __instance.isCrouching = false;
            _tempCrouch = true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerControllerB), "Jump_performed")]
        public static void PostJump(PlayerControllerB __instance)
        {
            if (!_tempCrouch)
            {
                return;
            }

            __instance.isCrouching = true;
            _tempCrouch = false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerControllerB), "PlayerHitGroundEffects")]
        public static void PostPlayerHitGroundEffects(PlayerControllerB __instance)
        {
            if (!__instance.isCrouching)
            {
                return;
            }

            __instance.playerBodyAnimator.SetTrigger(StartCrouching);
            __instance.playerBodyAnimator.SetBool(Crouching, true);
        }

        private static bool CanJump(PlayerControllerB __instance) => !Physics.Raycast(
            __instance.gameplayCamera.transform.position,
            Vector3.up,
            out __instance.hit,
            0.72f,
            __instance.playersManager.collidersAndRoomMask,
            QueryTriggerInteraction.Ignore
        );
    }
}

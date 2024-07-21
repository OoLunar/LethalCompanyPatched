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
        private static readonly FieldInfo? _playerCarryWeight = AccessTools.Field(typeof(PlayerControllerB), "carryWeight");
        private static readonly FieldInfo? _slipperinessConfigField = AccessTools.Field(typeof(LethalCompanyPatchedPlugin), nameof(LethalCompanyPatchedPlugin.Slipperiness));
        private static readonly FieldInfo? _jumpDelayConfigField = AccessTools.Field(typeof(LethalCompanyPatchedPlugin), nameof(LethalCompanyPatchedPlugin.JumpDelay));
        private static readonly FieldInfo? _instantSprintConfigField = AccessTools.Field(typeof(LethalCompanyPatchedPlugin), nameof(LethalCompanyPatchedPlugin.InstantSprint));
        private static readonly MethodInfo? _configEntryFloatValueMethod = AccessTools.Method(typeof(ConfigEntry<float>), "get_Value");

        private static bool _tempCrouch;
        private static readonly int _crouchingId = Animator.StringToHash("crouching");
        private static readonly int _startCrouchingId = Animator.StringToHash("startCrouching");

        private static bool ReplaceInstruction(List<CodeInstruction> instructions, List<CodeInstruction> newInstructions, Predicate<int> whereClause, int lookAhead = 0, int lookBehind = 0)
        {
            // Locate the first instruction that matches the predicate
            int maxIndex = instructions.Count - lookAhead;
            for (int index = lookBehind; index < maxIndex; index++)
            {
                if (whereClause.Invoke(index))
                {
                    instructions.RemoveAt(index);
                    instructions.InsertRange(index, newInstructions);
                    return true;
                }
            }

            return false;
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(PlayerControllerB), "PlayerJump", MethodType.Enumerator)]
        public static IEnumerable<CodeInstruction> RemoveJumpDelay(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> currentInstructions = [.. instructions];
            List<CodeInstruction> newInstructions =
            [
                new CodeInstruction(OpCodes.Ldsfld, _jumpDelayConfigField),
                new CodeInstruction(OpCodes.Callvirt, _configEntryFloatValueMethod),
            ];

            if (ReplaceInstruction(currentInstructions, newInstructions, instructionIndex =>
                currentInstructions[instructionIndex + 1].opcode == OpCodes.Newobj // new WaitForSeconds
                && ((ConstructorInfo)currentInstructions[instructionIndex + 1].operand).DeclaringType == typeof(WaitForSeconds), 1))
            {
                LethalCompanyPatchedPlugin.StaticLogger.LogDebug("Patched Jump-Delay");
            }
            else
            {
                LethalCompanyPatchedPlugin.StaticLogger.LogError("Failed to patch Jump-Delay, the player's jump will have it's default delay.");
            }

            return currentInstructions;
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(PlayerControllerB), "Update")]
        public static IEnumerable<CodeInstruction> FixSlipperiness(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> currentInstructions = [.. instructions];
            List<CodeInstruction> newInstructions =
            [
                new CodeInstruction(OpCodes.Ldsfld, _slipperinessConfigField),
                new CodeInstruction(OpCodes.Callvirt, _configEntryFloatValueMethod),
            ];

            if (ReplaceInstruction(currentInstructions, newInstructions, instructionIndex =>
                // We're looking for where the slipperiness field is set to 5
                // And instead pushing the value of LethalCompanyPatchedPlugin.Slipperiness
                currentInstructions[instructionIndex].opcode == OpCodes.Ldc_R4
                && Mathf.Approximately((float)currentInstructions[instructionIndex].operand, 5f)
                && currentInstructions[instructionIndex + 2].LoadsField(_playerCarryWeight)
                && currentInstructions[instructionIndex + 3].opcode == OpCodes.Ldc_R4
                && Mathf.Approximately((float)currentInstructions[instructionIndex + 3].operand, 1.5f), 3))
            {
                LethalCompanyPatchedPlugin.StaticLogger.LogDebug("Patched Slipperiness");
            }
            else
            {
                LethalCompanyPatchedPlugin.StaticLogger.LogError("Failed to patch Slipperiness, the player will have the default slipperiness.");
            }

            return currentInstructions;
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(PlayerControllerB), "Update")]
        public static IEnumerable<CodeInstruction> InstantSprint(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> currentInstructions = [.. instructions];
            List<CodeInstruction> newInstructions =
            [
                new CodeInstruction(OpCodes.Ldsfld, _instantSprintConfigField),
                new CodeInstruction(OpCodes.Callvirt, _configEntryFloatValueMethod),
            ];

            if (ReplaceInstruction(currentInstructions, newInstructions, instructionIndex =>
                // We're looking for when the player starts and stops sprinting, and changing the sprint multiplier to 2.25
                currentInstructions[instructionIndex - 3].opcode == OpCodes.Ldfld
                && currentInstructions[instructionIndex - 3].ToString() == "ldfld float GameNetcodeStuff.PlayerControllerB::sprintMultiplier"
                && currentInstructions[instructionIndex - 2].opcode == OpCodes.Ldc_R4
                && Mathf.Approximately((float)currentInstructions[instructionIndex - 2].operand, 2.25f)
                && currentInstructions[instructionIndex].opcode == OpCodes.Ldc_R4
                && Mathf.Approximately((float)currentInstructions[instructionIndex].operand, 1f), 0, 3))
            {
                LethalCompanyPatchedPlugin.StaticLogger.LogDebug("Patched Instant-Sprint");
            }
            else
            {
                LethalCompanyPatchedPlugin.StaticLogger.LogError("Failed to patch Instant-Sprint, the player will have the default sprint speed.");
            }

            return currentInstructions;
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
                || !playerController.playerBodyAnimator.GetBool(_crouchingId) // Other players see that this player is not crouching
                || !CanJump(__instance)) // Player is in a space where they cannot jump/must crouch
            {
                return;
            }

            // The player is no longer holding the crouch button, OR the player was forced to uncrouch, OR crouch hold is disabled
            playerController.isCrouching = false;
            playerController.playerBodyAnimator.SetBool(_crouchingId, false);
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

            __instance.playerBodyAnimator.SetTrigger(_startCrouchingId);
            __instance.playerBodyAnimator.SetBool(_crouchingId, true);
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

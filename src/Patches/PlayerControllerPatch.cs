using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace OoLunar.LethalCompanyPatched.Patches
{
    [HarmonyPatch(typeof(PlayerControllerB))]
    internal class PlayerControllerPatch : IPatch
    {
        private static readonly FieldInfo _playerCarryWeight = typeof(PlayerControllerB).GetField("carryWeight");
        private static bool _tempCrouch = false;

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(PlayerControllerB), "Update")]
        public static IEnumerable<CodeInstruction> RemoveJumpDelay(IEnumerable<CodeInstruction> instructions)
        {
            if (!LethalCompanyPatchedPlugin.InstantJump.Value)
            {
                return instructions;
            }

            List<CodeInstruction> list = new(instructions);
            for (int i = 0; i < list.Count; i++)
            {
                CodeInstruction val = list[i];
                if (val.opcode != OpCodes.Newobj)
                {
                    continue;
                }

                ConstructorInfo? constructorInfo = val.operand as ConstructorInfo;
                if (constructorInfo?.DeclaringType == typeof(WaitForSeconds))
                {
                    list[i] = new CodeInstruction(OpCodes.Ldnull, null);
                    list.RemoveAt(i - 1);
                    i--;

                    LethalCompanyPatchedPlugin.StaticLogger.LogDebug("Patched Instant-Jump");
                }
            }

            return list;
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(PlayerControllerB), "Update")]
        public static IEnumerable<CodeInstruction> FixSlipperiness(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> list = new(instructions);
            for (int i = 0; i < list.Count - 3; i++)
            {
                CodeInstruction val = list[i];
                if (val.opcode != OpCodes.Ldc_R4
                    || (float)val.operand != 5f
                    || !CodeInstructionExtensions.LoadsField(list[i + 2], _playerCarryWeight, false)
                    || list[i + 3].opcode != OpCodes.Ldc_R4
                    || (float)list[i + 3].operand != 1.5f)
                {
                    continue;
                }

                list[i] = new CodeInstruction(OpCodes.Ldc_R4, LethalCompanyPatchedPlugin.Slipperiness.Value);
                LethalCompanyPatchedPlugin.StaticLogger.LogDebug("Patched Slipperiness");
            }

            return list;
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(PlayerControllerB), "Update")]
        public static IEnumerable<CodeInstruction> InstantSprint(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> list = new(instructions);
            for (int i = 1; i < list.Count - 2; i++)
            {
                CodeInstruction val = list[i];
                if (val.opcode != OpCodes.Ldc_R4
                    || (float)val.operand != 2.25f
                    || list[i - 1].opcode != OpCodes.Ldfld
                    || list[i - 1].ToString() != "ldfld float GameNetcodeStuff.PlayerControllerB::sprintMultiplier"
                    || list[i + 2].opcode != OpCodes.Ldc_R4
                    || (float)list[i + 2].operand != 1f)
                {
                    continue;
                }

                list[i + 2] = new CodeInstruction(OpCodes.Ldc_R4, LethalCompanyPatchedPlugin.InstantSprint.Value);
                LethalCompanyPatchedPlugin.StaticLogger.LogDebug("Patched Instant-Sprint");
            }

            return list;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerControllerB), "Update")]
        public static void HoldCrouch(PlayerControllerB __instance)
        {
            PlayerControllerB? playerController = GameNetworkManager.Instance.localPlayerController;

            // Testing conditions where the player crouch state cannot be changed
            if (playerController == null
                || (__instance.IsOwner && __instance.isPlayerControlled && (!__instance.IsServer || __instance.isHostPlayerObject)) // Player put on the mask
                || __instance.isTestingPlayer // Unsure?
                || __instance.inTerminalMenu // Player is in a terminal
                || __instance.isTypingChat // Player is typing in chat
                || __instance.isPlayerDead // Player is dead
                || __instance.quickMenuManager.isMenuOpen // Player has the pause menu open
                || IngamePlayerSettings.Instance.playerInput.actions.FindAction("Crouch", true).IsPressed() // Player is holding the crouch button
                || !playerController.playerBodyAnimator.GetBool("crouching") // Other players see that this player is not crouching
                || !CanJump(__instance)) // Player is in a space where they cannot jump/must crouch
            {
                return;
            }

            // The player is no longer holding the crouch button OR the player was forced to uncrouch
            playerController.isCrouching = false;
            playerController.playerBodyAnimator.SetBool("crouching", false);
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

            __instance.playerBodyAnimator.SetTrigger("startCrouching");
            __instance.playerBodyAnimator.SetBool("crouching", true);
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

using HarmonyLib;
using TMPro;
using UnityEngine;

namespace OoLunar.LethalCompanyPatched.Patches
{
    [HarmonyPatch]
    internal class HUDManagerPatch : MonoBehaviour
    {
        private static bool _instantiating = true;
        private static TextMeshProUGUI? _hudPercentagesText;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(StartOfRound), "SceneManager_OnLoadComplete1")]
        public static void CreateHudPercentages()
        {
            if (!_instantiating)
            {
                return;
            }

            GameObject val = GameObject.Find("Systems/UI/Canvas/IngamePlayerHUD/TopLeftCorner/WeightUI");
            GameObject val2 = GameObject.Find("Systems/UI/Canvas/IngamePlayerHUD/TopLeftCorner");
            GameObject val3 = Instantiate(val, val2.transform);
            val3.name = "HPSP";

            GameObject gameObject = val3.transform.GetChild(0).gameObject;
            RectTransform component = gameObject.GetComponent<RectTransform>();
            component.anchoredPosition = new Vector2(-45f, 10f);
            _hudPercentagesText = gameObject.GetComponent<TextMeshProUGUI>();
            _hudPercentagesText.faceColor = new Color(255f, 0f, 0f, 255f);
            _hudPercentagesText.fontSize = 12f;
            _hudPercentagesText.margin = new Vector4(0f, -36f, 100f, 0f);
            _hudPercentagesText.alignment = (TextAlignmentOptions)260;
            _hudPercentagesText.text = $"{100}\n\n\n{100}%";
            _instantiating = false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GameNetworkManager), "Disconnect")]
        public static void UnInstantiate() => _instantiating = true;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(HUDManager), "Update")]
        public static void Update()
        {
            if (GameNetworkManager.Instance.localPlayerController == null || _instantiating || _hudPercentagesText == null)
            {
                return;
            }

            float health = Mathf.RoundToInt(GameNetworkManager.Instance.localPlayerController.health);
            float sprint = Mathf.RoundToInt(((GameNetworkManager.Instance.localPlayerController.sprintMeter * 100f) - 10f) / 90f * 100f);
            if (sprint < 0f)
            {
                sprint = 0f;
            }

            _hudPercentagesText.text = $"{health}\n\n\n\n{sprint}%";
        }
    }
}

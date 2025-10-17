using BepInEx.Logging;
using HarmonyLib;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Diagnostics;
using static SCP2006.Plugin;

/* bodyparts
 * 0 head
 * 1 right arm
 * 2 left arm
 * 3 right leg
 * 4 left leg
 * 5 chest
 * 6 feet
 * 7 right hip
 * 8 crotch
 * 9 left shoulder
 * 10 right shoulder */

namespace SCP2006
{
    [HarmonyPatch]
    public class TESTING : MonoBehaviour
    {
        private static ManualLogSource logger = LoggerInstance;

        [HarmonyPostfix, HarmonyPatch(typeof(HUDManager), nameof(HUDManager.PingScan_performed))]
        public static void PingScan_performedPostFix()
        {
            if (!Utils.isBeta) { return; }
            if (!Utils.testing) { return; }

            /*for (int i = 0; i < playerListSlots.Length; i++) // playerListSlots is in QuickMenuManager
            {
                if (playerListSlots[i].isConnected)
                {
                    float num = playerListSlots[i].volumeSlider.value / playerListSlots[i].volumeSlider.maxValue;
                    if (num == -1f)
                    {
                        SoundManager.Instance.playerVoiceVolumes[i] = -70f;
                    }
                    else
                    {
                        SoundManager.Instance.playerVoiceVolumes[i] = num;
                    }
                }
            }*/
        }

        [HarmonyPrefix, HarmonyPatch(typeof(HUDManager), nameof(HUDManager.SubmitChat_performed))]
        public static void SubmitChat_performedPrefix(HUDManager __instance)
        {
            if (!Utils.isBeta) { return; }
            if (!IsServerOrHost) { return; }
            string msg = __instance.chatTextField.text;
            string[] args = msg.Split(" ");
            LoggerInstance.LogDebug(msg);

            switch (args[0])
            {
                default:
                    Utils.ChatCommand(args);
                    break;
            }
        }
    }
}
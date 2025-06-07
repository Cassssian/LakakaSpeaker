using System;
using HarmonyLib;
using REPOLib.Modules;
using UnityEngine;

namespace LakakaSpeaker
{
    /// <summary>
    /// MonoBehaviour that loops random music clips when current clip finishes.
    /// </summary>
    public class RandomMusicLooper : MonoBehaviour
    {
        private AudioSource _src;

        void Awake()
        {
            _src = GetComponent<AudioSource>();
        }

        void Update()
        {
            if (_src == null || _src.isPlaying)
                return;

            var plugin = LakakaSpeaker.Instance;
            if (plugin == null)
                return;

            if (plugin.IsNcsMode)
            {
                plugin.StartCoroutine(plugin.PlayOneRandomNcsClip(_src));
            }
            else
            {
                var nextClip = plugin.GetRandomClip();
                if (nextClip != null)
                {
                    _src.clip = nextClip;
                    _src.Play();
                }
            }
        }

    }

    /// <summary>
    /// Harmony patch that intercepts AudioSource.Play() on ValuableObject instances
    /// to replace the clip with a random one and attach a looper.
    /// </summary>
    [HarmonyPatch(typeof(AudioSource), nameof(AudioSource.Play), new Type[0])]
    public static class Patch_AudioSourcePlay_ForValuables
    {
        static bool Prefix(AudioSource __instance)
        {
            if (__instance == null)
                return true;

            // Ne patcher que les objets Valuable
            if (__instance.gameObject.GetComponentInParent<ValuableObject>() == null)
                return true;

            var plugin = LakakaSpeaker.Instance;
            if (plugin == null)
                return true;

            if (__instance.gameObject.name.Contains("JBLSpeaker", StringComparison.OrdinalIgnoreCase))
            {
                __instance.playOnAwake = false;
                __instance.Stop();

                if (plugin.IsNcsMode)
                {
                    plugin.L.LogInfo($"🎵 [Patch Prefix] NCS mode activé pour {__instance.gameObject.name}");
                    plugin.StartCoroutine(plugin.PlayOneRandomNcsClip(__instance));
                }
                else
                {
                    var randomClip = plugin.GetRandomClip();
                    if (randomClip == null)
                    {
                        plugin.L.LogWarning("🎵 Aucun clip local à jouer.");
                        return false;
                    }

                    __instance.clip = randomClip;
                    __instance.volume = 0.8f;
                    __instance.loop = false;

                    plugin.L.LogInfo($"🎵 [Patch Prefix] lecture locale : {randomClip.name}");

                    if (__instance.gameObject.GetComponent<RandomMusicLooper>() == null)
                    {
                        __instance.gameObject.AddComponent<RandomMusicLooper>();
                        plugin.L.LogInfo("🔁 RandomMusicLooper ajouté");
                    }

                    __instance.Play();
                }

                // Ne pas laisser Unity appeler Play() — on le contrôle
                return false;
            }

            return true;
        }
    }
}

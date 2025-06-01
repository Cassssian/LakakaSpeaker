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
            if (_src == null) return;

            // When clip finished, play another random clip
            if (!_src.isPlaying)
            {
                var plugin = LakakaSpeaker.Instance;
                var nextClip = plugin?.GetRandomClip();
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
        static void Prefix(AudioSource __instance)
        {
            if (__instance == null)
                return;

            // Only apply to AudioSources belonging to a ValuableObject
            if (__instance.gameObject.GetComponentInParent<ValuableObject>() == null)
                return;

            var plugin = LakakaSpeaker.Instance;
            var randomClip = plugin?.GetRandomClip();
            if (randomClip == null)
                return;

            // Prevent default play and stop any current sound
            __instance.playOnAwake = false;
            __instance.Stop();

            // Assign random clip and set parameters
            __instance.clip = randomClip;
            __instance.volume = 0.8f;
            __instance.loop = false;

            plugin.L.LogInfo($"🎵 [Play Prefix] '{__instance.gameObject.name}' -> clip: {randomClip.name}");

            // Attach looper component if not already present
            if (__instance.gameObject.GetComponent<RandomMusicLooper>() == null)
            {
                __instance.gameObject.AddComponent<RandomMusicLooper>();
                plugin.L.LogInfo("🔁 RandomMusicLooper added");
            }
        }
    }
}

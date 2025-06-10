using HarmonyLib;
using REPOLib.Modules;
using System;
using UnityEngine;

namespace LakakaSpeaker
{
    /// <summary>
    /// MonoBehaviour qui boucle des musiques aléatoires quand le clip courant se termine (mode non-NCS).
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

            var nextClip = plugin.GetRandomClip();
            if (nextClip != null)
            {
                _src.clip = nextClip;
                _src.Play();
            }
        }
    }

    /// <summary>
    /// Patch Harmony pour intercepter AudioSource.Play() sur les ValuableObject de type JBLSpeaker.
    /// Gère deux modes : Non-NCS (boucle aléatoire), et NCS (looper séquentiel avec téléchargement).
    /// Implémente un bypass pour que les Play internes du looper NCS passent sans être interceptés.
    /// </summary>
    [HarmonyPatch(typeof(AudioSource), nameof(AudioSource.Play), new Type[0])]
    public static class Patch_AudioSourcePlay_ForValuables
    {
        static bool Prefix(AudioSource __instance)
        {
            if (__instance == null)
                return true;

            // 1) Bypass si Play interne du looper NCS
            if (NcsSpeakerLooper.ShouldBypass(__instance))
                return true;

            // 2) Cherche ValuableObject parent
            var valuable = __instance.gameObject.GetComponentInParent<ValuableObject>();
            if (valuable == null)
                return true;

            var instance = LakakaSpeaker.Instance;
            if (instance == null)
                return true;

            // Ne cibler que les objets dont le nom contient "JBLSpeaker"
            if (!valuable.gameObject.name.Contains("JBLSpeaker", StringComparison.OrdinalIgnoreCase))
                return true;

            // Mode non-NCS : boucle aléatoire
            if (!instance.IsNcsMode)
            {
                AudioClip audioClip = instance.GetRandomClip();
                if (audioClip != null)
                {
                    __instance.playOnAwake = false;
                    __instance.Stop();
                    __instance.clip = audioClip;
                    __instance.volume = 0.8f;
                    __instance.loop = false;
                    instance.L.LogInfo("🎵 [Play Prefix] '" + __instance.gameObject.name + "' -> clip: " + audioClip.name);

                    if (__instance.gameObject.GetComponent<RandomMusicLooper>() == null)
                    {
                        __instance.gameObject.AddComponent<RandomMusicLooper>();
                        instance.L.LogInfo("🔁 RandomMusicLooper ajouté");
                    }
                }
                else
                {
                    instance.L.LogWarning("RandomMusicLooper: aucun clip aléatoire disponible.");
                }
                // Intercepte Play pour JBLSpeaker hors NCS
                return false;
            }
            else
            {
                // Mode NCS
                var looper = __instance.GetComponentInParent<NcsSpeakerLooper>();
                if (looper == null)
                {
                    instance.L.LogWarning($"[Patch] NcsSpeakerLooper non trouvé sur '{valuable.gameObject.name}' au Play. Ajout dynamique.");
                    looper = valuable.gameObject.AddComponent<NcsSpeakerLooper>();
                    looper.Init(__instance);
                }
                if (!looper.IsStarted)
                {
                    looper.StartLoopIfNeeded();
                    instance.L.LogInfo($"[Patch] Démarrage du loop NCS pour '{valuable.gameObject.name}'.");
                }
                // Une fois démarré, on bloque tout Play manuel ultérieur pour éviter de perturber la boucle.
                return false;
            }
        }

        /// <summary>
        /// Patch pour ajouter le NcsSpeakerLooper dès Awake du ValuableObject si le mode NCS est déjà actif.
        /// </summary>
        [HarmonyPatch(typeof(ValuableObject), "Awake")]
        public class Patch_JBLSpeaker_Awake
        {
            static void Postfix(ValuableObject __instance)
            {
                var instance = LakakaSpeaker.Instance;
                if (instance == null)
                    return;

                if (instance.IsNcsMode && __instance.gameObject.name.Contains("JBLSpeaker", StringComparison.OrdinalIgnoreCase))
                {
                    var audioSource = __instance.GetComponentInChildren<AudioSource>();
                    if (audioSource != null && __instance.gameObject.GetComponent<NcsSpeakerLooper>() == null)
                    {
                        var looper = __instance.gameObject.AddComponent<NcsSpeakerLooper>();
                        looper.Init(audioSource);
                        instance.L.LogInfo($"[Awake Patch] NcsSpeakerLooper ajouté et initialisé sur '{__instance.gameObject.name}'.");
                    }
                }
            }
        }
    }
}

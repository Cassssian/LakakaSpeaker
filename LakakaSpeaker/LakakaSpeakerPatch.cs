using HarmonyLib;
using REPOLib;
using REPOLib.Modules;
using System;
using System.Collections;
using System.Collections.Generic;
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
            if (__instance == null) return true;

            // Bypass interne (depuis NcsSpeakerLooper)
            if (NcsSpeakerLooper.ShouldBypass(__instance))
                return true;

            var valuable = __instance.gameObject.GetComponentInParent<ValuableObject>();
            if (valuable == null || !valuable.name.Contains("JBLSpeaker", StringComparison.OrdinalIgnoreCase))
                return true;

            var instance = LakakaSpeaker.Instance;
            if (instance == null)
                return true;

            if (instance.IsNcsMode)
            {
                var looper = __instance.GetComponentInParent<NcsSpeakerLooper>();
                if (looper == null)
                {
                    looper = valuable.gameObject.AddComponent<NcsSpeakerLooper>();
                    looper.Init(__instance);
                    instance.L.LogInfo("[Patch] NcsSpeakerLooper ajouté dynamiquement.");
                }

                if (!looper.IsStarted)
                {
                    if (HostDetector.IsCurrentPlayerHost())
                    {
                        if (looper.IsReady)
                        {
                            looper.StartLoopIfNeeded();
                            instance.SendNcsUrlToAll_P2P(looper.CurrentUrl, Time.realtimeSinceStartup + 3f);
                            instance.L.LogInfo($"[Patch] Lecture immédiate avec clip déjà prêt (url={looper.CurrentUrl})");
                        }
                        else
                        {
                            looper.MarkPendingStart(); // À créer : _pendingStart = true;
                            instance.L.LogInfo("[Patch] Clip pas encore prêt, attente différée");
                        }
                    }
                    else
                    {
                        instance.L.LogInfo("[Patch] Client a cliqué, attente NCS_SYNC de l’hôte.");
                    }
                }

                return false;
            }
            else
            {
                if (HostDetector.IsCurrentPlayerHost())
                {
                    var clip = instance.GetRandomClip();
                    if (clip != null)
                    {
                        int clipId = instance.ReserveNextClipId();
                        float[] samples = new float[clip.samples * clip.channels];
                        clip.GetData(samples, 0);
                        byte[] data = new byte[samples.Length * sizeof(float)];
                        Buffer.BlockCopy(samples, 0, data, 0, data.Length);

                        float startTime = Time.realtimeSinceStartup + 3f;

                        instance.SendClipHeader_P2P(clipId, data.Length, clip.frequency, clip.channels);
                        instance.SendAllChunksForClip_P2P(clipId, data);
                        instance.SendAudioSyncToAll(clipId, startTime);

                        __instance.clip = clip;
                        __instance.volume = 0.8f;
                        __instance.loop = false;

                        RandomMusicLooper looper = __instance.gameObject.GetComponent<RandomMusicLooper>();
                        if (looper == null)
                            __instance.gameObject.AddComponent<RandomMusicLooper>();

                        NcsSpeakerLooper.MarkNextPlay(__instance);
                        instance.L.LogInfo($"[Patch] Lancement custom synchro à {startTime:F2}s clip='{clip.name}'");

                        instance.StartCoroutine(PlayDelayed(__instance, startTime));
                    }
                }
                else
                {
                    instance.L.LogInfo("[Patch] Client a cliqué sur enceinte en mode custom, ignoré.");
                }

                return false;
            }
        }

        private static IEnumerator PlayDelayed(AudioSource src, float startTime)
        {
            float delay = startTime - Time.realtimeSinceStartup;
            if (delay > 0)
                yield return new WaitForSeconds(delay);
            else
                LakakaSpeaker.Instance.L.LogWarning("[Patch] Retardé : lecture immédiate clip local.");

            src.Play();
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

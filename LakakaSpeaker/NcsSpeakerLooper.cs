using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace LakakaSpeaker
{
    /// <summary>
    /// Gère la boucle séquentielle de musiques NCS :
    /// - Téléchargement et pré-téléchargement d’un clip à la fois.
    /// - Bypass du Prefix Harmony lors de l’appel à AudioSource.Play() interne.
    /// - Validation des AudioClip téléchargés pour éviter les clips vides.
    /// - Démarrage différé si clic trop tôt (pendingStart).
    /// </summary>
    public class NcsSpeakerLooper : MonoBehaviour
    {
        // Ensemble statique pour marquer les AudioSource dont le Play interne doit bypasser le patch Harmony
        private static readonly HashSet<AudioSource> _bypassSet = new HashSet<AudioSource>();

        private AudioSource _src;
        private LakakaSpeaker _plugin;
        private bool _started = false;
        private bool _firstReady = false;
        private bool _pendingStart = false;
        private List<string> _remainingUrls = new List<string>();
        private AudioClip _currentClip = null;
        private AudioClip _nextClip = null;
        private string _nextUrl = null;

        public bool IsStarted => _started;
        public bool IsReady => _firstReady;

        public string CurrentUrl => _currentClip != null ? _currentClip.name : _nextUrl;

        /// <summary>
        /// Initialise le looper avec l'AudioSource donnée. Lance la préparation des deux premiers clips.
        /// </summary>
        public void Init(AudioSource src)
        {
            _src = src;
            _plugin = LakakaSpeaker.Instance;
            if (_plugin != null)
                _remainingUrls = new List<string>(_plugin.ncsUrls);
            if (_src != null && _plugin != null)
            {
                StartCoroutine(PrepareFirstTwoClips());
            }
            _plugin?.L.LogInfo("[NcsSpeakerLooper] Initialisation du NCS Speaker Looper");
        }

        /// <summary>
        /// Prépare le premier clip et pré-télécharge le second.
        /// Si un clip est invalide (null ou durée <= 0.1s), on tente quelques URLs suivantes.
        /// Si l’utilisateur a déjà cliqué avant que le premier clip soit prêt (_pendingStart), on démarre la boucle dès que possible.
        /// </summary>
        private IEnumerator PrepareFirstTwoClips()
        {
            _plugin.L.LogInfo("[NcsSpeakerLooper] Préparation des deux premiers morceaux NCS...");
            // Attendre que la liste de ncsUrls soit disponible
            while (_plugin.ncsUrls == null || _plugin.ncsUrls.Count == 0)
            {
                _plugin.L.LogWarning("[NcsSpeakerLooper] Liste NCS vide, attente...");
                yield return new WaitForSeconds(2f);
            }
            // Télécharger le premier clip valide, jusqu'à N tentatives
            _currentClip = null;
            int attempts = 0;
            const int maxAttempts = 5;
            while (_currentClip == null && attempts < maxAttempts)
            {
                string url1 = GetNextNcsUrl();
                attempts++;
                if (string.IsNullOrEmpty(url1))
                    break;
                _plugin.L.LogInfo($"[NcsSpeakerLooper] Téléchargement initial NCS: {url1}");
                using UnityWebRequest uwr = UnityWebRequestMultimedia.GetAudioClip(url1, AudioType.MPEG);
                yield return uwr.SendWebRequest();
                if (uwr.result == UnityWebRequest.Result.Success)
                {
                    var clipTmp = DownloadHandlerAudioClip.GetContent(uwr);
                    if (clipTmp != null && clipTmp.length > 0.1f)
                    {
                        clipTmp.name = Path.GetFileNameWithoutExtension(url1);
                        _currentClip = clipTmp;
                        break;
                    }
                    else
                    {
                        _plugin.L.LogWarning($"[NcsSpeakerLooper] Clip invalide ou trop court: {url1}");
                    }
                }
                else
                {
                    _plugin.L.LogError($"[NcsSpeakerLooper] Erreur téléchargement initial: {uwr.error} (URL: {url1})");
                }
            }
            if (_currentClip == null)
            {
                _plugin.L.LogError("[NcsSpeakerLooper] Impossible de charger un clip initial valide après plusieurs tentatives.");
                yield break;
            }
            // Pré-télécharger le prochain clip
            _nextClip = null;
            _nextUrl = GetNextNcsUrl();
            if (!string.IsNullOrEmpty(_nextUrl))
            {
                _plugin.L.LogInfo($"[NcsSpeakerLooper] Pré-téléchargement NCS: {_nextUrl}");
                yield return StartCoroutine(DownloadNextClip(_nextUrl, clip => _nextClip = clip));
            }
            _firstReady = (_currentClip != null);
            // Si un clic avait eu lieu avant préparation, démarrer la boucle automatiquement
            if (_firstReady && _pendingStart)
            {
                StartLoopIfNeeded();
            }
        }

        /// <summary>
        /// Démarre la boucle si le premier clip est prêt ; sinon, enregistre un démarrage différé (_pendingStart).
        /// </summary>
        public void StartLoopIfNeeded()
        {
            if (_started || _src == null || _plugin == null)
                return;
            if (!_firstReady)
            {
                _plugin.L.LogWarning("[NcsSpeakerLooper] Premier morceau pas encore prêt, démarrage différé.");
                _pendingStart = true;
                return;
            }
            _started = true;
            _plugin.L.LogInfo($"[NcsSpeakerLooper] Démarrage de la boucle NCS sur '{_src.gameObject.name}'.");
            StartCoroutine(LoopNcsBuffered());
        }

        /// <summary>
        /// Boucle principale : joue le clip courant, marque le Play pour bypass, et précharge le suivant pendant la lecture.
        /// </summary>
        private IEnumerator LoopNcsBuffered()
        {
            while (_currentClip != null)
            {
                // Configuration du AudioSource
                _src.playOnAwake = false;
                _src.Stop();
                _src.clip = _currentClip;
                _src.volume = 0.8f;
                _src.loop = false;

                // Affichage du log « Lecture en cours -> Artiste – Titre »
                if (_plugin.ncsTrackNamesByUrl.TryGetValue(_currentClip.name, out var display))
                    _plugin.L.LogInfo($"Lecture en cours -> {display}");
                else
                    _plugin.L.LogInfo($"Lecture en cours -> {_currentClip.name}");

                // Bypass de Harmony et démarrage
                MarkNextPlay(_src);
                _src.Play();

                // Pré-téléchargement du suivant si nécessaire
                if (_nextClip == null)
                {
                    string nextUrl = GetNextNcsUrl();
                    if (!string.IsNullOrEmpty(nextUrl))
                        yield return StartCoroutine(DownloadNextClip(nextUrl, clip => _nextClip = clip));
                }

                // Attente de la fin du morceau
                float clipLen = _src.clip.length;
                float waited = 0f;
                const float maxExtra = 300f;
                while (_src.isPlaying && _src.time < clipLen - 0.05f)
                {
                    yield return null;
                    waited += Time.deltaTime;
                    if (waited > clipLen + maxExtra)
                    {
                        _plugin.L.LogError("[NcsSpeakerLooper] Lecture bloquée, arrêt de la boucle.");
                        break;
                    }
                }

                // Petit délai
                yield return new WaitForSeconds(0.1f);

                // Passage au morceau suivant
                _currentClip = _nextClip;
                _nextClip = null;
                _nextUrl = null;
            }

            _plugin.L.LogInfo("[NcsSpeakerLooper] Fin de la playlist NCS.");
        }

        /// <summary>
        /// Télécharge un clip depuis URL, valide qu'il n'est pas null et a une durée > 0.1s.
        /// Appelle onReady(clip) ou onReady(null) si invalide.
        /// </summary>
        private IEnumerator DownloadNextClip(string url, Action<AudioClip> onReady)
        {
            _plugin.L.LogInfo($"[NcsSpeakerLooper] Téléchargement NCS: {url}");
            using var uwr = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG);
            yield return uwr.SendWebRequest();

            if (uwr.result == UnityWebRequest.Result.Success)
            {
                var clip = DownloadHandlerAudioClip.GetContent(uwr);
                if (clip != null && clip.length > 0.1f)
                {
                    string key = Path.GetFileNameWithoutExtension(url);
                    clip.name = key;
                    _plugin.L.LogInfo($"[NcsSpeakerLooper] Clip prêt : {key}");
                    onReady(clip);
                    yield break;
                }
                else
                {
                    _plugin.L.LogWarning($"[NcsSpeakerLooper] Clip invalide ou trop court : {url}");
                }
            }
            else
            {
                _plugin.L.LogError($"[NcsSpeakerLooper] Erreur téléchargement: {uwr.error} (URL: {url})");
            }

            onReady(null);
        }

        /// <summary>
        /// Obtient la prochaine URL à télécharger, en évitant de refaire celles déjà jouées jusqu’à vider la liste.
        /// </summary>
        private string GetNextNcsUrl()
        {
            if (_remainingUrls == null || _remainingUrls.Count == 0)
            {
                if (_plugin != null)
                    _remainingUrls = new List<string>(_plugin.ncsUrls);
            }
            if (_remainingUrls == null || _remainingUrls.Count == 0)
                return null;
            int idx = UnityEngine.Random.Range(0, _remainingUrls.Count);
            string url = _remainingUrls[idx];
            _remainingUrls.RemoveAt(idx);
            return url;
        }

        /// <summary>
        /// Marque l’AudioSource pour bypasser le patch Harmony lors du prochain Play().
        /// </summary>
        public static void MarkNextPlay(AudioSource src)
        {
            if (src != null)
                _bypassSet.Add(src);
        }

        /// <summary>
        /// Vérifie si l’AudioSource est marquée pour bypass ; si oui, la retire et retourne true.
        /// </summary>
        public static bool ShouldBypass(AudioSource src)
        {
            if (src != null && _bypassSet.Contains(src))
            {
                _bypassSet.Remove(src);
                return true;
            }
            return false;
        }

        public void StartLoopWithUrl(string url, float targetTime)
        {
            _plugin.L.LogInfo($"[NcsLooper] Démarrage avec synchro : {url} à {targetTime:F2}");
            StartCoroutine(DownloadThenStart(url, targetTime));
        }

        private IEnumerator DownloadThenStart(string url, float startTime)
        {
            _plugin.L.LogInfo($"[NcsLooper] Téléchargement pour synchro : {url}");
            AudioClip clip = null;

            using (var uwr = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
            {
                yield return uwr.SendWebRequest();
                if (uwr.result == UnityWebRequest.Result.Success)
                    clip = DownloadHandlerAudioClip.GetContent(uwr);
                else
                    _plugin.L.LogError($"[NcsLooper] Erreur téléchargement sync: {uwr.error}");
            }

            if (clip == null || clip.length <= 0.1f)
            {
                _plugin.L.LogWarning($"[NcsLooper] Clip invalide ou trop court (url={url})");
                yield break;
            }

            // Renommage
            string key = Path.GetFileNameWithoutExtension(url);
            clip.name = key;

            // Préparation et attente
            _currentClip = clip;
            _started = true;
            float wait = startTime - Time.realtimeSinceStartup;
            if (wait > 0f) yield return new WaitForSeconds(wait);
            else _plugin.L.LogWarning($"[NcsLooper] Retard détecté ({-wait:F2}s), lecture immédiate.");

            // Lecture
            MarkNextPlay(_src);
            _src.clip = _currentClip;
            _src.volume = 0.8f;
            _src.loop = false;
            _src.Play();
            _plugin.L.LogInfo($"Lecture synchronisée -> {_plugin.ncsTrackNamesByUrl.GetValueOrDefault(key, key)}");
        }


        public void MarkPendingStart()
        {
            _pendingStart = true;
        }

    }
}

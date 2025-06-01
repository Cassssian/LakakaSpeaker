using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro; // si vous utilisez TextMeshPro
using LakakaMod = LakakaSpeaker.LakakaSpeaker;

public class MusicManagerUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Saisir ici l'InputField ou TMP_InputField pour la recherche")]
    public TMP_InputField searchInput; // Remplacez par InputField si vous n'avez pas TextMeshPro

    [Tooltip("RectTransform parent qui contiendra les entrées de musique")]
    public RectTransform musicListParent;

    [Tooltip("Prefab pour chaque entrée de musique (contenant Toggle + AutoScrollText + TMP_Text)")]
    public GameObject musicEntryPrefab;

    // Dictionnaire local pour l'état on/off de chaque fichier
    private Dictionary<string, bool> musicStatus = new Dictionary<string, bool>();

    private void Start()
    {
        // 1) Remplir la liste des musiques au démarrage
        RefreshMusicList();

        // 2) Connecter l'événement de recherche
        if (searchInput != null)
        {
            searchInput.onValueChanged.AddListener(OnSearchChanged);
        }
        else
        {
            Debug.LogWarning("[MusicManagerUI] Le champ searchInput n'est pas assigné !");
        }
    }

    /// <summary>
    /// Vide l'UI, recalcule la liste des fichiers et reconstruit la vue.
    /// </summary>
    public void RefreshMusicList()
    {
        // 1️⃣ Vider l'UI
        foreach (Transform child in musicListParent)
        {
            Destroy(child.gameObject);
        }

        // 2️⃣ Récupérer tous les fichiers présents dans le dossier
        var files = System.IO.Directory.GetFiles(LakakaMod.Instance.MusicDirectory, "*.*")
            .Where(f => f.EndsWith(".mp3") || f.EndsWith(".wav") || f.EndsWith(".ogg") || f.EndsWith(".aiff"))
            .ToArray();

        // Ensemble des noms (sans extension)
        var currentFileNames = new HashSet<string>(
            files.Select(f => System.IO.Path.GetFileNameWithoutExtension(f))
        );

        // 3️⃣ Supprimer du dictionnaire local ceux qui n’existent plus physiquement
        var keysToRemove = musicStatus.Keys.Where(k => !currentFileNames.Contains(k)).ToList();
        foreach (var key in keysToRemove)
        {
            musicStatus.Remove(key);
        }

        // 4️⃣ Ajouter au dictionnaire local les nouveaux fichiers (activés par défaut ou selon config)
        foreach (var fileName in currentFileNames)
        {
            // Si on n’a jamais vu ce fichier, on l’ajoute dans notre dictionnaire
            if (!musicStatus.ContainsKey(fileName))
            {
                // Tenter de récupérer l’état depuis REPOConfig (LakakaSpeaker.musicToggles)
                bool enabledByDefault = LakakaMod.Instance.GetMusicToggleValue(fileName);
                musicStatus[fileName] = enabledByDefault;
                Debug.Log($"[MusicManagerUI] Détection nouveau fichier: {fileName}, statut par défaut: {(enabledByDefault ? "Activé" : "Désactivé")}");
            }
        }

        // 5️⃣ Construire l’UI pour chaque fichier
        foreach (var fileName in currentFileNames)
        {
            var entry = Instantiate(musicEntryPrefab, musicListParent);
            entry.name = fileName;

            // a) Texte du nom de fichier
            var textComponent = entry.GetComponentInChildren<TextMeshProUGUI>();
            if (textComponent != null)
            {
                textComponent.text = fileName;
            }
            else
            {
                Debug.LogWarning($"[MusicManagerUI] Aucun TextMeshProUGUI trouvé dans {musicEntryPrefab.name} !");
            }

            // b) Toggle
            var toggle = entry.GetComponentInChildren<Toggle>();
            if (toggle != null)
            {
                // Initialiser la valeur depuis musicStatus
                toggle.isOn = musicStatus[fileName];

                // Écouter les changements
                toggle.onValueChanged.AddListener(val =>
                {
                    musicStatus[fileName] = val;
                    Debug.Log($"[MusicManagerUI] {fileName} => {(val ? "Activé" : "Désactivé")}");

                    // Mettre à jour la config dans LakakaSpeaker
                    LakakaMod.Instance.SetMusicToggleValue(fileName, val);
                });
            }
            else
            {
                Debug.LogWarning($"[MusicManagerUI] Aucun Toggle trouvé dans {musicEntryPrefab.name} !");
            }

            // c) AutoScrollText (pour défilement si nécessaire)
            var autoScroll = entry.GetComponentInChildren<AutoScrollText>();
            if (autoScroll != null)
            {
                autoScroll.Init();
            }
            else
            {
                Debug.LogWarning($"[MusicManagerUI] Aucun composant AutoScrollText trouvé dans {musicEntryPrefab.name} !");
            }
        }
    }

    /// <summary>
    /// Filtre dynamiquement l'affichage des enfants de musicListParent selon la recherche.
    /// </summary>
    private void OnSearchChanged(string recherche)
    {
        string searchLower = recherche.ToLower();
        foreach (Transform child in musicListParent)
        {
            bool shouldShow = child.name.ToLower().Contains(searchLower);
            child.gameObject.SetActive(shouldShow);
        }
    }

    /// <summary>
    /// Expose pour LakakaSpeaker: récupérer le dictionnaire local (si besoin).
    /// </summary>
    public Dictionary<string, bool> GetMusicStatus() => musicStatus;
}

using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using ExitGames.Client.Photon;
using HarmonyLib;
using MenuLib;               // pour MenuAPI, REPOPopupPage, REPOButton, REPOInputField, etc.
using MenuLib.MonoBehaviors; // pour accéder à MenuManager.instance.StartCoroutine(...)
using MenuLib.Structs;      // pour la structure Padding
using Photon.Realtime;
using REPOLib.Modules;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TMPro;                // pour TMP_InputField, TMP_Text
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

namespace LakakaSpeaker
{


    #region Class
    [Serializable]
    public class NcsTrack
    {
        public string id;
        public string title;
        public string artist;
        public string previewUrl;   // URL du .mp3 (ou autre)  
        public string coverUrl;
        public int duration;
        public string date;
        public string[] genre;
        public string[] tags;
        public long listenCount;
        public string youtubeUrl;
        public string type;
    }

    // Wrapper pour JsonUtility
    [Serializable]
    public class NcsTrackList
    {
        public List<NcsTrack> tracks;
    }
    #endregion


    [BepInPlugin("cassian.LakakaSpeaker", "LakakaSpeaker", "1.0.0")]
    [BepInDependency("PaintedThornStudios.PaintedUtils", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("REPOLib", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("nickklmao.menulib", BepInDependency.DependencyFlags.HardDependency)]
    public partial class LakakaSpeaker : BaseUnityPlugin
    {


        #region Variable
        // Classe interne pour buffer audio
        private class MusicBuffer
        {
            private Dictionary<int, byte[]> chunks = new Dictionary<int, byte[]>();
            private int expectedChunks;
            public bool IsComplete => chunks.Count == expectedChunks;

            public void StoreChunk(int index, byte[] data)
            {
                chunks[index] = data;
            }
            public AudioClip AssembleClip()
            {
                // reconstruction du byte[] complet puis conversion AudioClip
                return null;
            }
        }

        new readonly ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource("LakakaSpeaker");
        public ManualLogSource L => Logger;


        /// <summary>
        /// Instance singleton exposée pour que MusicManagerUI et le menu R.E.P.O. y accèdent.
        /// </summary>
        internal static LakakaSpeaker Instance { get; private set; } = null;


        private bool _initialized = false;
        private string pluginDir;
        private string musicDir;
        private static MenuButtonPopUp menuButtonPopup;


        /// <summary>
        /// Ensemble des noms de fichiers (sans extension) qui sont “inclus” (chargés).
        /// Par défaut, on inclut tout.
        /// </summary>
        private HashSet<string> includedMusic = new HashSet<string>();


        private static readonly string BundleName = GetModName();
        private Dictionary<string, ConfigEntry<bool>> musicToggles = new Dictionary<string, ConfigEntry<bool>>();
        private static readonly Dictionary<ConfigEntryBase, object> changedEntryValues = new Dictionary<ConfigEntryBase, object>();

        private List<AudioClip> clips = new List<AudioClip>();


        private AssetBundle? _assetBundle;


        public string MusicDirectory => musicDir;


        private ConfigEntry<string> musicFoldersEntry;
        private List<string> musicFolders = new List<string>();
        internal static REPOButton lastClickedfolderButton;
        private static bool hasPopupMenuOpened;
        private static readonly Dictionary<ConfigEntryBase, object> originalEntryValues = new Dictionary<ConfigEntryBase, object>();



        #region NCS
        /// <summary>
        /// NCS URLs pour les musiques NCS (No Copyright Sounds).
        /// </summary>
        internal List<string> ncsUrls = new List<string>();
        private ConfigEntry<bool> ncsTrackPlayEntry;
        public bool IsNcsMode => ncsTrackPlayEntry?.Value ?? false;
        #endregion


        #region Partage client-serveur
        private class IncomingFile
        {
            public List<byte[]> Chunks = new List<byte[]>();
            public int ExpectedChunks;
            public int ReceivedChunks;
        }
        private Dictionary<int, MusicBuffer> MusicBuffers = new Dictionary<int, MusicBuffer>();
        private NetworkedEvent _chunkEvent;
        private int _nextClipToSend = 0;
        private const int CHUNK_SIZE = 16 * 1024;
        #endregion


        #endregion


        #region Partie code LakakaSpeaker
        private void LoadMusicFolders()
        {
            // Si la config n'existe pas ou est vide, on initialise avec le dossier de base
            var raw = musicFoldersEntry.Value;
            if (string.IsNullOrWhiteSpace(raw))
            {
                musicFolders = new List<string> { musicDir };
                SaveMusicFolders(); // On sauvegarde pour créer le .config si besoin
                return;
            }

            musicFolders = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(s => s.Trim())
                              .Where(s => !string.IsNullOrWhiteSpace(s))
                              .ToList();

            // Toujours inclure le dossier de base en première position
            if (!musicFolders.Contains(musicDir))
                musicFolders.Insert(0, musicDir);
            else
            {
                // Si le dossier de base n'est pas en première position, on le déplace devant
                musicFolders.Remove(musicDir);
                musicFolders.Insert(0, musicDir);
            }
        }



        private void SaveMusicFolders()
        {
            // Sauvegarde dans le .config
            musicFoldersEntry.Value = string.Join(";", musicFolders.Distinct());
        }


        private static string GetModName()
        {
            object obj = typeof(LakakaSpeaker).GetCustomAttributes(typeof(BepInPlugin), inherit: false)[0];
            BepInPlugin val = (BepInPlugin)((obj is BepInPlugin) ? obj : null);
            return ((val != null) ? val.Name : null) ?? "LakakaSpeaker";
        }



        private string SanitizeConfigKey(string input)
        {
            // Remplace les caractères interdits par un underscore
            var invalidChars = new[] { '=', '\n', '\t', '\\', '"', '\'', '[', ']', '【', '】' };
            foreach (var c in invalidChars)
            {
                input = input.Replace(c, '_');
            }
            return input;
        }


        private void Awake()
        {
            Instance = this;

            L.LogInfo("LakakaSpeaker initializing...");

            // 1) Dossier CustomMusic
            pluginDir = Path.Combine(Paths.PluginPath, "Cassian-LakakaSpeaker");
            musicDir = Path.Combine(pluginDir, "CustomMusic");

            if (!Directory.Exists(musicDir))
            {
                Directory.CreateDirectory(musicDir);
                L.LogMessage($"Created folder: {musicDir} (add your files there)");
            }

            // 2) Par défaut, on inclut tous les fichiers audio déjà présents
            var existingFiles = Directory.GetFiles(musicDir, "*.*")
                .Where(f => f.EndsWith(".mp3") || f.EndsWith(".wav") || f.EndsWith(".ogg") || f.EndsWith(".aiff"))
                .Select(f => Path.GetFileNameWithoutExtension(f));
            foreach (var name in existingFiles)
            {
                includedMusic.Add(name);
            }

            // INITIALISATION DE LA CONFIG AVANT USAGE
            musicFoldersEntry = Config.Bind(
                "Musiques LakakaSpeaker",
                "DossiersMusiques",
                "", // valeur par défaut
                new ConfigDescription(
                    "Liste des dossiers de musiques séparés par ';' (le dossier de base est toujours inclus automatiquement).",
                    null,
                    new object[] { "HideREPOConfig" }));

            LoadMusicFolders();

            ncsTrackPlayEntry = Config.Bind(
                "Mode NCS",
                "Activer lecture NCS",
                true,
                new ConfigDescription(
                    "Si activé, joue les musiques NCS aléatoires au lieu des musiques locales.",
                    null,
                    new object[] { "HideREPOConfig" }
                )
            );
            L.LogMessage("NCS mode enabled, loading NCS URLs only.");
            string jsonUrl = "https://raw.githubusercontent.com/Cassssian/LakakaSpeaker/refs/heads/master/LakakaSpeaker/ncs_music_link.json";
            StartCoroutine(LoadNcsUrlsOnly(jsonUrl));
            StartCoroutine(LoadAllClips());

            try
            {
                // Dans le Main Menu
                MenuAPI.AddElementToMainMenu(parent =>
                {
                    MenuAPI.CreateREPOButton("Lakaka", CreateLakakaFolderMenu, parent, new Vector2(120f, 55.5f));
                });
                //Dans le Lobby Menu
                MenuAPI.AddElementToLobbyMenu(parent =>
                {
                    MenuAPI.CreateREPOButton("Lakaka", CreateLakakaFolderMenuLobby, parent, new Vector2(186f, 65f));
                });
                // Dans le Escape Menu
                //MenuAPI.AddElementToEscapeMenu(parent =>
                //{
                //    MenuAPI.CreateREPOButton("Lakaka", CreateLakakaFolderMenuGame, parent, new Vector2(178f, 86f));
                //});
            }
            catch (Exception ex)
            {
                L.LogError($"[LakakaSpeaker] Impossible d'ajouter le bouton REPO “Lakaka” : {ex}");
            }

            //InitializeStreamingNetworkEvent();
            //StartCoroutine(WaitForLobbyThenStream());
        }


        /// <summary>
        /// Permet de relancer le chargement des clips (après modification de includedMusic).
        /// </summary>
        public void RefreshClips()
        {
            StopCoroutine(nameof(LoadAllClips));
            StartCoroutine(LoadAllClips());
        }



        private void LoadAssetBundle()
        {
            if (!((UnityEngine.Object)(object)_assetBundle != (UnityEngine.Object)null))
            {
                string directoryName = Path.GetDirectoryName(((BaseUnityPlugin)this).Info.Location);
                string text = Path.Combine(directoryName, BundleName + ".bundle");
                _assetBundle = AssetBundle.LoadFromFile(text);
                if ((UnityEngine.Object)(object)_assetBundle == (UnityEngine.Object)null)
                {
                    Logger.LogError((object)("Failed to load bundle from " + text));
                }
            }
        }



        private void LoadValuablesFromResources()
        {
            if ((UnityEngine.Object)(object)_assetBundle == (UnityEngine.Object)null)
            {
                return;
            }
            List<GameObject> list = (from name in _assetBundle.GetAllAssetNames()
                                     where name.Contains("/valuables/") && name.EndsWith(".prefab")
                                     select _assetBundle.LoadAsset<GameObject>(name)).ToList();
            foreach (GameObject item in list)
            {
                Valuables.RegisterValuable(item);
            }
            if (list.Count > 0)
            {
                Logger.LogInfo((object)$"Successfully registered {list.Count} valuables through REPOLib");
            }
        }



        /// <summary>
        /// Coroutine qui charge tous les AudioClips du dossier, 
        /// en ne tenant compte que des fichiers dont le nom est dans includedMusic.
        /// </summary>
        private IEnumerator LoadAllClips()
        {
            clips.Clear();

            string[] files = Directory.GetFiles(musicDir, "*.*")
                .Where(f => f.EndsWith(".mp3") || f.EndsWith(".wav") || f.EndsWith(".ogg") || f.EndsWith(".aiff"))
                .ToArray();

            foreach (var file in files)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);

                // Ne charge que si la musique est activée dans la config
                if (!GetMusicToggleValue(fileName))
                    continue;

                using UnityWebRequest uwr = UnityWebRequestMultimedia.GetAudioClip("file://" + file, GetAudioType(file));
                yield return uwr.SendWebRequest();

                if (uwr.result == UnityWebRequest.Result.Success)
                {
                    var clip = DownloadHandlerAudioClip.GetContent(uwr);
                    clip.name = fileName;
                    clips.Add(clip);
                    L.LogInfo($"Loaded clip: {fileName}");
                }
                else
                {
                    L.LogError($"Error loading {file}: {uwr.error}");
                }
            }

            if (clips.Count == 0)
                L.LogWarning("No audio clips loaded (maybe none are enabled?).");
        }



        /// <summary>
        /// Renvoie l’état (bool) de la config pour un fichier donné. 
        /// Si la ConfigEntry n’existe pas, crée une entrée par défaut à true.
        /// </summary>
        public bool GetMusicToggleValue(string fileName)
        {
            if (musicToggles.TryGetValue(fileName, out var entry))
            {
                return entry.Value;
            }
            else
            {
                // Crée une config par défaut activée
                string safeFileName = SanitizeConfigKey(fileName);
                string configKey = $"Activer {safeFileName}";

                var newEntry = Config.Bind(
                    "Musiques LakakaSpeaker",
                    configKey,
                    true,
                    new ConfigDescription(
                        $"Enable or disable the music {fileName}.",
                        null,
                        new object[] { "HideREPOConfig" }
                    )
                );

                musicToggles[fileName] = newEntry;
                return newEntry.Value;
            }
        }



        /// <summary>
        /// Modifie la valeur de la ConfigEntry pour un fichier donné.
        /// </summary>
        public void SetMusicToggleValue(string fileName, bool newValue)
        {
            if (musicToggles.TryGetValue(fileName, out var entry))
            {
                entry.Value = newValue;
            }
            else
            {
                // Si jamais elle n’existe pas, on la crée et on met à jour
                string safeFileName = SanitizeConfigKey(fileName);
                string configKey = $"Activer {safeFileName}";
                var newEntry = Config.Bind(
                    "Musiques LakakaSpeaker",
                    configKey,
                    true,
                    new ConfigDescription(
                        $"Enable or disable the music {fileName}.",
                        null,
                        new object[] { "HideREPOConfig" }
                    )
                );
                musicToggles[fileName] = newEntry;
            }
        }



        private AudioType GetAudioType(string path)
        {
            return Path.GetExtension(path).ToLower() switch
            {
                ".mp3" => AudioType.MPEG,
                ".wav" => AudioType.WAV,
                ".ogg" => AudioType.OGGVORBIS,
                ".aiff" => AudioType.AIFF,
                // Removed ".flac" case as AudioType does not support FLAC
                _ => AudioType.UNKNOWN
            };
        }
        
        
        
        public AudioClip GetRandomClip() =>
            (clips.Count == 0) ? null : clips[UnityEngine.Random.Range(0, clips.Count)];



        private void Update()
        {
            // 2) Dès qu'on a au moins 1 clip ET qu'on n'a pas encore initialisé Harmony + bundle
            if (!_initialized && clips.Count > 0 && ncsUrls.Count > 0)
            {
                _initialized = true;

                // Détache & cache le GameObject-plugin
                ((Component)this).gameObject.transform.parent = null;
                ((UnityEngine.Object)((Component)this).gameObject).hideFlags = HideFlags.HideAndDontSave;

                // Patch Harmony
                var harmony = new Harmony(Info.Metadata.GUID);
                harmony.PatchAll();
                L.LogInfo("Harmony patches applied.");

                // Charge le bundle et enregistre les Valuables
                LoadAssetBundle();
                LoadValuablesFromResources();
                L.LogInfo($"{Info.Metadata.GUID} v{Info.Metadata.Version} fully loaded.");
            }
        }
        
        #endregion


        #region Partie UI

        private static void CreateLakakaFolderMenu()
        {
            if (Instance == null || MenuManager.instance == null)
                return;

            REPOPopupPage folderPage = MenuAPI.CreateREPOPopupPage(
                "Music Folders",
                REPOPopupPage.PresetSide.Left,
                shouldCachePage: false,
                pageDimmerVisibility: true
            );

            folderPage.scrollView.scrollSpeed = 3f;

            REPOPopupPage rEPOPopupPage = folderPage;
            Padding maskPadding = folderPage.maskPadding;
            maskPadding.top = 35f;
            rEPOPopupPage.maskPadding = maskPadding;

            REPOPopupPage rEPOPopupPage2 = folderPage;

            rEPOPopupPage2.onEscapePressed = (REPOPopupPage.ShouldCloseMenuDelegate)Delegate.Combine(rEPOPopupPage2.onEscapePressed, (REPOPopupPage.ShouldCloseMenuDelegate)delegate
            {
                if (hasPopupMenuOpened)
                {
                    return false;
                }
                if (changedEntryValues.Count == 0)
                {
                    return true;
                }
                MenuAPI.OpenPopup("Unsaved Changes", Color.red, "You have unsaved changes, are you sure you want to exit?", delegate
                {
                    folderPage.ClosePage(closePagesAddedOnTop: true);
                    changedEntryValues.Clear();
                    hasPopupMenuOpened = false;
                }, delegate
                {
                    hasPopupMenuOpened = false;
                });
                hasPopupMenuOpened = true;
                return false;
            });

            // Liste locale pour les boutons de dossiers
            var localFolderButtons = new List<REPOButton>();

            folderPage.AddElement(delegate (Transform parent)
            {
                MenuAPI.CreateREPOInputField("Folder Search", delegate (string s)
                {
                    string text = (string.IsNullOrEmpty(s) ? null : s.ToLower().Trim());
                    foreach (REPOButton btn in localFolderButtons)
                    {
                        btn.repoScrollViewElement.visibility = text == null || btn.labelTMP.text.ToLower().Contains(text);
                    }
                    folderPage.scrollView.SetScrollPosition(0f);
                }, parent, new Vector2(83f, 272f)).transform.localScale = Vector3.one * 0.95f;
            });

            CreateFolderList(folderPage, localFolderButtons);

            folderPage.AddElement(delegate (Transform parent)
            {
                MenuAPI.CreateREPOButton("Back", delegate
                {
                    if (changedEntryValues.Count == 0 || hasPopupMenuOpened)
                    {
                        folderPage.ClosePage(closePagesAddedOnTop: true);
                    }
                    else
                    {
                        MenuAPI.OpenPopup("Unsaved Changes", Color.red, "You have unsaved changes, are you sure you want to exit?", delegate
                        {
                            folderPage.ClosePage(closePagesAddedOnTop: true);
                            changedEntryValues.Clear();
                            hasPopupMenuOpened = false;
                        }, delegate
                        {
                            hasPopupMenuOpened = false;
                        });
                        hasPopupMenuOpened = true;
                    }
                }, parent, new Vector2(66f, 18f));
            });


            folderPage.OpenPage(openOnTop: false);
        }

        private static void CreateLakakaFolderMenuLobby()
            {
                OpenPopupCustom("LakakaSpeaker", Color.cyan, "Choose which settings you want to change", "Music played (Multi)", "Speaker's settings", delegate
                    {
                        //option 1 : Music played (Multi)
                    }, delegate
                    {
                        //option 2 : Speaker's settings
                    }
                );
            }

        public static void OpenPopupCustom(string header, Color headerColor, string content, string Option1, string Option2, Action onLeftClicked, Action onRightClicked = null)
        {
            if (!menuButtonPopup)
            {
                menuButtonPopup = MenuManager.instance.gameObject.AddComponent<MenuButtonPopUp>();
            }

            menuButtonPopup.option1Event = new UnityEvent();
            menuButtonPopup.option2Event = new UnityEvent();
            if (onLeftClicked != null)
            {
                menuButtonPopup.option1Event.AddListener(onLeftClicked.Invoke);
            }

            if (onRightClicked != null)
            {
                menuButtonPopup.option2Event.AddListener(onRightClicked.Invoke);
            }

            MenuManager.instance.PagePopUpTwoOptions(menuButtonPopup, header, headerColor, content, Option1, Option2);
        }

        private static void CreateFolderList(REPOPopupPage folderPage, List<REPOButton> folderButtons)
        {
            // On vide la liste des boutons de dossier
            folderButtons.Clear();
            // On crée un bouton pour chaque dossier dans musicFolders
            foreach (KeyValuePair<string, ConfigEntryBase[]> folderConfigEntry in GetFolderEntries())
            {
                string folderName = folderConfigEntry.Key;
                string simplifiedFolderName = Path.GetFileName(folderName);
                ConfigEntryBase[] configEntryBases = folderConfigEntry.Value;
                folderPage.AddElementToScrollView(delegate (Transform parent)
                {
                    REPOButton folderButton = MenuAPI.CreateREPOButton(simplifiedFolderName, null, parent);
                    folderButton.labelTMP.fontStyle = FontStyles.Normal;
                    if (folderName.Length > 24)
                    {
                        REPOButton rEPOButton = folderButton;
                        Vector2 labelSize = folderButton.GetLabelSize();
                        labelSize.x = 250f;
                        rEPOButton.overrideButtonSize = labelSize;
                        REPOTextScroller rEPOTextScroller = ((Component)(object)folderButton.labelTMP).gameObject.AddComponent<REPOTextScroller>();
                        rEPOTextScroller.maxCharacters = 24;
                        MenuManager.instance.StartCoroutine(rEPOTextScroller.Animate());
                    }
                    folderButton.onClick = delegate
                    {
                        if (!(lastClickedfolderButton == folderButton))
                        {
                            if (changedEntryValues.Count == 0)
                            {
                                OpenPage();
                            }
                            else
                            {
                                MenuAPI.OpenPopup("Unsaved Changes", Color.red, "You have unsaved changes, are you sure you want to exit?", delegate
                                {
                                    changedEntryValues.Clear();
                                    OpenPage();
                                    hasPopupMenuOpened = false;
                                }, delegate
                                {
                                    hasPopupMenuOpened = false;
                                });
                                hasPopupMenuOpened = true;
                            }
                        }
                    };
                    folderButtons.Add(folderButton);
                    return folderButton.rectTransform;
                    void OpenPage()
                    {
                        var musicFolderButtons = new List<REPOButton>();
                        MenuAPI.CloseAllPagesAddedOnTop();
                        string simplifiedFolderName = Path.GetFileName(folderName);
                        REPOPopupPage folderPage = MenuAPI.CreateREPOPopupPage(simplifiedFolderName, REPOPopupPage.PresetSide.Right, shouldCachePage: false, pageDimmerVisibility: false, 5f);
                        folderPage.scrollView.scrollSpeed = 3f;
                        folderPage.onEscapePressed = () => !hasPopupMenuOpened && changedEntryValues.Count == 0;
                        folderPage.AddElement(delegate (Transform mainPageParent)
                        {
                            MenuAPI.CreateREPOButton("Save Changes", delegate
                            {
                                foreach (var kvp in changedEntryValues.ToArray())
                                {
                                    var configEntry = kvp.Key as ConfigEntry<bool>;
                                    if (configEntry != null)
                                    {
                                        bool newValue = (bool)kvp.Value;
                                        configEntry.Value = newValue;
                                        originalEntryValues[configEntry] = newValue;
                                    }
                                }
                                changedEntryValues.Clear();
                                Instance.RefreshClips(); // Recharge les musiques selon la nouvelle config
                            }, mainPageParent, new Vector2(370f, 18f));
                        });
                        folderPage.AddElement(delegate (Transform mainPageParent)
                        {
                            MenuAPI.CreateREPOButton("Revert", delegate
                            {
                                if (changedEntryValues.Count != 0)
                                {
                                    changedEntryValues.Clear();
                                    OpenPage();
                                }
                            }, mainPageParent, new Vector2(585f, 18f));
                        });

                        folderPage.AddElementToScrollView(delegate (Transform scrollView)
                        {
                            REPOButton rEPOButton2 = MenuAPI.CreateREPOButton("Reset To Default", delegate
                            {
                                MenuAPI.OpenPopup("Reset " + Path.GetFileName(folderName) + "'" + (Path.GetFileName(folderName).ToLower().EndsWith('s') ? string.Empty : "s") + " settings?", Color.red, "Are you sure you want to reset all settings back to default?", ResetToDefault);
                            }, scrollView);
                            rEPOButton2.rectTransform.localPosition = new Vector2((folderPage.maskRectTransform.rect.width - rEPOButton2.GetLabelSize().x) * 0.5f, 0f);
                            return rEPOButton2.rectTransform;
                        });

                        folderPage.AddElementToScrollView(delegate (Transform scrollView)
                        {
                            var inputField = MenuAPI.CreateREPOInputField("Music Search", delegate (string s)
                            {
                                string text = (string.IsNullOrEmpty(s) ? null : s.ToLower().Trim());
                                foreach (REPOButton btn in musicFolderButtons)
                                {
                                    btn.repoScrollViewElement.visibility = text == null || btn.labelTMP.text.ToLower().Contains(text);
                                }
                                folderPage.scrollView.SetScrollPosition(0f);
                            }, scrollView, new Vector2(5f, 272f));
                            inputField.transform.localScale = Vector3.one * 0.95f;
                            return inputField.GetComponent<RectTransform>();
                        });

                        folderPage.AddElementToScrollView(delegate (Transform scrollView)
                        {
                            Vector2 size = new Vector2(0f, 10f);
                            return MenuAPI.CreateREPOSpacer(scrollView, default(Vector2), size).rectTransform;
                        });

                        CreateMusicFolderButton(folderPage, folderName, musicFolderButtons);
                        
                        folderPage.OpenPage(openOnTop: true);
                        lastClickedfolderButton = folderButton;
                    }
                    void ResetToDefault()
                    {
                        ConfigEntryBase[] array2 = configEntryBases;
                        foreach (ConfigEntryBase obj in array2)
                        {
                            obj.BoxedValue = obj.DefaultValue;
                        }
                        changedEntryValues.Clear();
                        OpenPage();
                    }

                });
            }
        }

        private static void CreateMusicFolderButton(REPOPopupPage folderPage, string folderPath, List<REPOButton> musicFolderButtons)
        {
            musicFolderButtons.Clear();

            var files = Directory.GetFiles(folderPath, "*.mp3")
                .Concat(Directory.GetFiles(folderPath, "*.wav"))
                .Concat(Directory.GetFiles(folderPath, "*.ogg"))
                .Concat(Directory.GetFiles(folderPath, "*.aiff"))
                .ToArray();

            foreach (var file in files)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);

                // Récupérer ou créer la config pour ce fichier
                if (!Instance.musicToggles.TryGetValue(fileName, out var configEntry))
                {
                    Instance.GetMusicToggleValue(fileName);
                    configEntry = Instance.musicToggles[fileName];
                }

                // Valeur originale et valeur modifiée (si présente)
                bool originalValue = configEntry.Value;
                bool currentValue = originalValue;
                if (changedEntryValues.TryGetValue(configEntry, out var changedObj) && changedObj is bool changedVal)
                    currentValue = changedVal;

                string color = currentValue ? "green" : "red";
                string label = $"<color={color}>{fileName}</color>";

                folderPage.AddElementToScrollView(delegate (Transform parent)
                {
                    REPOButton fileButton = MenuAPI.CreateREPOButton(label, null, parent);
                    fileButton.labelTMP.fontStyle = FontStyles.Normal;
                    fileButton.labelTMP.richText = true;
                    fileButton.name = fileName;

                    if (fileName.Length > 24)
                    {
                        Vector2 labelSize = fileButton.GetLabelSize();
                        labelSize.x = 250f;
                        fileButton.overrideButtonSize = labelSize;
                        REPOTextScroller rEPOTextScroller = fileButton.labelTMP.gameObject.AddComponent<REPOTextScroller>();
                        rEPOTextScroller.maxCharacters = 24;
                        MenuManager.instance.StartCoroutine(rEPOTextScroller.Animate());
                    }

                    // Clic sur le bouton : modifie la valeur dans changedEntryValues et le label
                    fileButton.onClick = () =>
                    {
                        bool current = originalValue;
                        if (changedEntryValues.TryGetValue(configEntry, out var changedObj2) && changedObj2 is bool changedVal2)
                            current = changedVal2;

                        bool newValue = !current;
                        changedEntryValues[configEntry] = newValue;

                        fileButton.labelTMP.text = $"<color={(newValue ? "green" : "red")}>{fileName}</color>";
                    };

                    musicFolderButtons.Add(fileButton);
                    return fileButton.rectTransform;
                });
            }
        }

        private static Dictionary<string, ConfigEntryBase[]> GetMusicConfigEntries()
        {
            Dictionary<string, ConfigEntryBase[]> dictionary = new Dictionary<string, ConfigEntryBase[]>();
            foreach (var entry in Instance.musicFolders)
            {
                List<ConfigEntryBase> list = new List<ConfigEntryBase>();
                foreach (var file in Directory.GetFiles(entry, "*.mp3")
                                            .Concat(Directory.GetFiles(entry, "*.wav"))
                                            .Concat(Directory.GetFiles(entry, "*.ogg"))
                                            .Concat(Directory.GetFiles(entry, "*.aiff")))
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    string safeFileName = Instance.SanitizeConfigKey(fileName);
                    ConfigDefinition configDef = new ConfigDefinition("Music Folder", safeFileName);
                    ConfigEntryBase configEntry = Instance.Config.Bind<bool>(
                        "Music Folder",
                        safeFileName,
                        true,
                        new ConfigDescription(
                            "Internal use.",
                            null,
                            new object[] { "HideREPOConfig" }
                        )
                    );
                    list.Add(configEntry);
                }
                if (list.Count > 0)
                {
                    dictionary.TryAdd(entry, list.ToArray());
                }

            }
            return dictionary;
        }


        private static Dictionary<string, ConfigEntryBase[]> GetFolderEntries()
        {
            Dictionary<string, ConfigEntryBase[]> dictionary = new Dictionary<string, ConfigEntryBase[]>();
            foreach (var entry in Instance.musicFolders)
            {
                List<ConfigEntryBase> list = new List<ConfigEntryBase>();
                foreach (var file in Directory.GetFiles(entry, "*.mp3")
                                            .Concat(Directory.GetFiles(entry, "*.wav"))
                                            .Concat(Directory.GetFiles(entry, "*.ogg"))
                                            .Concat(Directory.GetFiles(entry, "*.aiff")))
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    string safeFileName = Instance.SanitizeConfigKey(fileName);
                    ConfigDefinition configDef = new ConfigDefinition("Music Folder", safeFileName);
                    ConfigEntryBase configEntry = Instance.Config.Bind<bool>(
                        "Music Folder",
                        safeFileName,
                        true,
                        new ConfigDescription(
                            "Internal use.",
                            null,
                            new object[] { "HideREPOConfig" }
                        )
                    );
                    list.Add(configEntry);
                }
                if (list.Count > 0)
                {
                    dictionary.TryAdd(entry, list.ToArray());
                }

            }
            return dictionary;
        }

        private static void ShowFilePickerAndCopyToBase()
        {
            // Utilise StandaloneFileBrowser ou un équivalent pour ouvrir un file picker natif
            // Après sélection, copie le fichier dans Instance.musicDir puis Instance.RefreshClips()
        }

        // Ouvre un folder picker natif et ajoute le dossier à la liste
        private static void ShowFolderPickerAndAdd()
        {
            // Utilise StandaloneFileBrowser ou un équivalent pour ouvrir un folder picker natif
            // Après sélection, ajoute le chemin à Instance.musicFolders, puis Instance.SaveMusicFolders()
        }

        private static int GetDecimalPlaces(float value)
        {
            string text = value.ToString(CultureInfo.InvariantCulture);
            int num = text.IndexOf('.');
            if (num != -1)
            {
                string text2 = text;
                int num2 = num + 1;
                return text2.Substring(num2, text2.Length - num2).Length;
            }
            return 0;
        }

        #endregion


        #region Partie NCS
        private IEnumerator LoadNcsUrlsOnly(string jsonUrl)
        {
            using UnityWebRequest uwr = UnityWebRequest.Get(jsonUrl);
            yield return uwr.SendWebRequest();

            if (uwr.result != UnityWebRequest.Result.Success)
            {
                L.LogError($"Erreur téléchargement JSON NCS : {uwr.error}");
                yield break;
            }

            // On entoure le JSON pour que JsonUtility sache désérialiser
            string rawJson = uwr.downloadHandler.text;
            //string wrappedJson = "{\"tracks\":" + rawJson + "}";
            NcsTrackList list = JsonUtility.FromJson<NcsTrackList>(rawJson);

            if (list == null || list.tracks == null)
            {
                L.LogError("Parsing du JSON NCS a échoué ou liste vide.");
                yield break;
            }

            L.LogInfo($"🔽 {list.tracks.Count} musiques NCS détectées dans le JSON.");

            // On remplit uniquement la liste d’URLs (previewUrl)
            foreach (var t in list.tracks)
            {
                if (!string.IsNullOrEmpty(t.previewUrl))
                    ncsUrls.Add(t.previewUrl);
            }

            L.LogInfo($"✅ {ncsUrls.Count} URLs NCS stockées pour usage ultérieur.");
        }

        // Coroutine pour télécharger et jouer un seul clip depuis URL
        public IEnumerator PlayOneRandomNcsClip(AudioSource src)
        {
            if (ncsUrls == null || ncsUrls.Count == 0)
            {
                L.LogWarning("Liste NCS vide : impossible de jouer un morceau aléatoire.");
                yield break;
            }

            // Choix aléatoire d’une URL dans ncsUrls
            string randomUrl = ncsUrls[UnityEngine.Random.Range(0, ncsUrls.Count)];
            L.LogInfo($"🎧 Téléchargement à la volée depuis NCS : {randomUrl}");

            using UnityWebRequest uwr = UnityWebRequestMultimedia.GetAudioClip(randomUrl, AudioType.MPEG);
            yield return uwr.SendWebRequest();

            if (uwr.result != UnityWebRequest.Result.Success)
            {
                L.LogError($"❌ Échec du téléchargement NCS : {uwr.error}");
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(uwr);
            clip.name = Path.GetFileNameWithoutExtension(randomUrl);

            // Affectation du clip et paramétrage
            src.clip = clip;
            src.volume = 0.8f;
            src.loop = false;
            L.LogInfo($"▶️ Lecture NCS aléatoire : {clip.name}");

            // Jouer une fois le clip chargé
            src.Play();

            // Ajouter RandomMusicLooper pour enchaîner après la fin du clip
            if (src.gameObject.GetComponent<RandomMusicLooper>() == null)
            {
                src.gameObject.AddComponent<RandomMusicLooper>();
                L.LogInfo("🔁 RandomMusicLooper ajouté pour enchaîner NCS.");
            }
        }
        #endregion


    //    #region Partie Client-Server

    //    // Appelé dans Awake()
    //    private void InitializeStreamingNetworkEvent()
    //    {
    //        // Déclare l'événement de diffusion de chunks
    //        _chunkEvent = new NetworkedEvent(
    //            "LakakaSpeaker_Chunk",
    //            (EventData data) =>
    //            {
    //                var arr = (object[])data.CustomData;
    //                int clipId = (int)arr[0];
    //                int chunkIdx = (int)arr[1];
    //                byte[] chunk = (byte[])arr[2];
    //                ReceiveChunk(clipId, chunkIdx, chunk);
    //            }
    //        );
    //    }

    //    // Remplace PhotonView.RPC par cet appel
    //    private void SendChunkToAll(int clipId, int chunkIdx, byte[] chunk)
    //    {
    //        object[] payload = new object[] { clipId, chunkIdx, chunk };
    //        // Envoie aux autres clients de la Steam Lobby
    //        var raiseOpts = new RaiseEventOptions { Receivers = ReceiverGroup.Others };
    //        // Fiable pour ne pas perdre de donnée
    //        _chunkEvent.RaiseEvent(payload, raiseOpts, SendOptions.SendReliable);
    //    }

    //    // Coroutine principale, appelée au début du lobby
    //    private IEnumerator WaitForLobbyThenStream()
    //    {
    //        // Attend d'être dans le Lobby Steam
    //        while (RunManager.instance == null
    //            || RunManager.instance.levelCurrent == null
    //            || !RunManager.instance.levelCurrent.name.Contains("Lobby"))
    //            yield return null;

    //        // Lancement du streaming séquentiel
    //        StartCoroutine(StreamClipsSequentially());
    //    }

    //    // Envoi morceau par morceau en parallèle de la lecture
    //    private IEnumerator StreamClipsSequentially()
    //    {
    //        while (_nextClipToSend < clips.Count)
    //        {
    //            int clipId = _nextClipToSend;
    //            AudioClip clip = clips[clipId];
    //            SendAllChunksForClip(clipId, clip);

    //            // Attendre le début de la lecture du clip sur le client (optionnel)
    //            yield return new WaitForSeconds(0.1f);

    //            // Durée d'un clip en secondes
    //            float duration = clip.length;
    //            // Pendant la lecture, on envoie les chunks progressivement
    //            int totalChunks = Mathf.CeilToInt((float)(clip.samples * clip.channels * sizeof(float)) / CHUNK_SIZE);

    //            for (int i = 0; i < totalChunks; i++)
    //            {
    //                // On envoie un chunk par frame ou toutes les X ms
    //                // Ici on détermine un intervalle basique
    //                SendChunkToAll(clipId, i, GetChunkData(clip, i));
    //                yield return null;
    //            }

    //            // Attendre la fin de la lecture avant de passer au suivant
    //            yield return new WaitForSeconds(duration);
    //            _nextClipToSend++;
    //        }

    //        L.LogInfo("✅ Streaming séquentiel terminé.");
    //    }

    //    // Prépare et envoie tous les chunks d'un clip d'un coup
    //    private void SendAllChunksForClip(int clipId, AudioClip clip)
    //    {
    //        int sampleCount = clip.samples * clip.channels;
    //        float[] floatData = new float[sampleCount];
    //        clip.GetData(floatData, 0);
    //        byte[] data = new byte[floatData.Length * sizeof(float)];
    //        Buffer.BlockCopy(floatData, 0, data, 0, data.Length);

    //        int totalChunks = Mathf.CeilToInt((float)data.Length / CHUNK_SIZE);
    //        for (int i = 0; i < totalChunks; i++)
    //        {
    //            int offset = i * CHUNK_SIZE;
    //            int size = Math.Min(CHUNK_SIZE, data.Length - offset);
    //            byte[] chunk = new byte[size];
    //            Buffer.BlockCopy(data, offset, chunk, 0, size);
    //            SendChunkToAll(clipId, i, chunk);
    //        }
    //    }

    //    // Extrait un chunk donné (optionnel si on envoie progressivement)
    //    private byte[] GetChunkData(AudioClip clip, int index)
    //    {
    //        int sampleCount = clip.samples * clip.channels;
    //        float[] floatData = new float[sampleCount];
    //        clip.GetData(floatData, 0);
    //        byte[] data = new byte[floatData.Length * sizeof(float)];
    //        Buffer.BlockCopy(floatData, 0, data, 0, data.Length);

    //        int offset = index * CHUNK_SIZE;
    //        int size = Math.Min(CHUNK_SIZE, data.Length - offset);
    //        byte[] chunk = new byte[size];
    //        Buffer.BlockCopy(data, offset, chunk, 0, size);
    //        return chunk;
    //    }

    //    // Réception et assemblage côté client (existing MusicBuffer)
    //    private void ReceiveChunk(int clipId, int chunkIndex, byte[] data)
    //    {
    //        if (!MusicBuffers.TryGetValue(clipId, out var buffer))
    //        {
    //            buffer = new MusicBuffer();
    //            MusicBuffers[clipId] = buffer;
    //        }
    //        buffer.StoreChunk(chunkIndex, data);
    //        if (buffer.IsComplete)
    //        {
    //            AudioClip assembled = buffer.AssembleClip();
    //            // Jouer ou stocker le clip
    //        }
    //    }


    //    #endregion
    }
}


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
using Steamworks;
using Steamworks.Data;
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
using Color = UnityEngine.Color;

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
    public class ClipReceiveBuffer
    {
        public int TotalBytes;
        public int SampleRate;
        public int Channels;
        public int TotalChunks;
        public byte[][] Chunks;
        public int ReceivedCount;
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
        private NetworkedEvent _ncsUrlEvent;
        private int _nextClipToSend = 0;
        private const int AUDIO_HEADER = 0;
        private const int AUDIO_CHUNK = 1;
        private const int NCS_URL = 2;
        private const int NCS_REQUEST = 3;

        private const int CHUNK_SIZE = 1000;
        private Dictionary<int, ClipReceiveBuffer> receiveBuffers = new Dictionary<int, ClipReceiveBuffer>();
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

            _ncsUrlEvent = new NetworkedEvent(
            "LakakaSpeaker_NcsUrl",
            (EventData data) =>
            {
                string url = data.CustomData as string;
                if (!string.IsNullOrEmpty(url))
                {
                    StartCoroutine(PlayNcsUrlOnClient(url));
                }
            }
        );

            SteamNetworking.OnP2PSessionRequest = (SteamId remote) =>
            {
                SteamNetworking.AcceptP2PSessionWithUser(remote);
                L.LogInfo($"[LakakaSpeaker] P2P session request accepted from {remote.Value}");
            };

            SteamNetworking.OnP2PConnectionFailed = (SteamId remote, P2PSessionError error) =>
            {
                L.LogWarning($"[LakakaSpeaker] P2P connection failed with {remote.Value}, erreur: {error}");
            };
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

                StartCoroutine(P2PPacket());
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

        /// <summary>
        /// Envoie aux pairs l’URL d’une musique NCS afin qu’ils téléchargent et jouent localement.
        /// </summary>
        private void SendNcsUrlToAll_P2P(string url)
        {
            if (!HostDetector.IsLocalPlayerHost()) return;
            byte[] urlBytes = System.Text.Encoding.UTF8.GetBytes(url);
            byte[] packet = new byte[1 + urlBytes.Length];
            packet[0] = NCS_URL;
            Buffer.BlockCopy(urlBytes, 0, packet, 1, urlBytes.Length);

            var lobbyOpt = SteamLobbyHelper.CurrentLobby;
            if (!lobbyOpt.HasValue || !lobbyOpt.Value.Id.IsValid)
            {
                Debug.LogWarning("[LakakaSpeaker] Pas de lobby courant valide");
                return;
            }
            Lobby lobby = lobbyOpt.Value;
            foreach (var member in lobby.Members)
            {
                SteamId memberSid = member.Id;
                if (memberSid == SteamClient.SteamId) continue;
                SteamNetworking.SendP2PPacket(memberSid, packet, packet.Length, nChannel: 0, P2PSend.Reliable);
            }
        }





        private IEnumerator PlayNcsUrlOnClient(string url)
        {
            L.LogInfo($"[Client] Téléchargement NCS synchronisé: {url}");
            using UnityWebRequest uwr = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG);
            yield return uwr.SendWebRequest();

            if (uwr.result == UnityWebRequest.Result.Success)
            {
                var clip = DownloadHandlerAudioClip.GetContent(uwr);
                if (clip != null && clip.length > 0.1f)
                {
                    var src = GetOrCreateAudioSource();
                    src.clip = clip;
                    src.volume = 0.8f;
                    src.loop = false;
                    src.Play();
                }
            }
            else
            {
                L.LogError($"[Client] Erreur téléchargement NCS: {uwr.error}");
            }
        }

        // Utilitaire pour obtenir/créer un AudioSource
        private AudioSource GetOrCreateAudioSource()
        {
            var go = GameObject.Find("LakakaSpeakerAudio") ?? new GameObject("LakakaSpeakerAudio");
            var src = go.GetComponent<AudioSource>() ?? go.AddComponent<AudioSource>();
            return src;
        }

        #endregion


        #region Partie Client-Server
        /// <summary>
        /// Découpe dataBytes en chunks et envoie chaque chunk aux pairs.
        /// </summary>
        private void SendAllChunksForClip_P2P(int clipId, byte[] dataBytes)
        {
            if (!HostDetector.IsLocalPlayerHost()) return;

            var lobbyOpt = SteamLobbyHelper.CurrentLobby;
            if (!lobbyOpt.HasValue || !lobbyOpt.Value.Id.IsValid)
            {
                Debug.LogWarning("[LakakaSpeaker] Pas de lobby courant valide");
                return;
            }
            Lobby lobby = lobbyOpt.Value;

            int totalBytes = dataBytes.Length;
            int totalChunks = Mathf.CeilToInt((float)totalBytes / CHUNK_SIZE);

            for (int i = 0; i < totalChunks; i++)
            {
                int offset = i * CHUNK_SIZE;
                int size = Math.Min(CHUNK_SIZE, totalBytes - offset);
                // paquet = messageType + clipId + chunkIndex + payload
                // messageType (1 byte) + clipId (4) + chunkIndex (4) + payload (size)
                byte[] packet = new byte[1 + 4 + 4 + size];
                packet[0] = (byte)AUDIO_CHUNK;
                Buffer.BlockCopy(BitConverter.GetBytes(clipId), 0, packet, 1, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(i), 0, packet, 1 + 4, 4);
                Buffer.BlockCopy(dataBytes, offset, packet, 1 + 4 + 4, size);

                foreach (var member in lobby.Members)
                {
                    SteamId memberSid = member.Id;
                    if (memberSid == Steamworks.SteamClient.SteamId) continue;
                    bool sent = SteamNetworking.SendP2PPacket(memberSid, packet, packet.Length, nChannel: 0, P2PSend.Reliable);
                    if (!sent)
                    {
                        L.LogWarning($"[LakakaSpeaker] Échec envoi chunk {i}/{totalChunks} du clip {clipId} à {member.Id}");
                    }
                }
            }
        }



        /// <summary>
        /// Envoie aux pairs le header d’un clip audio : clipId, totalBytes, sampleRate, channels.
        /// </summary>
        private void SendClipHeader_P2P(int clipId, int totalBytes, int sampleRate, int channels)
        {
            if (!HostDetector.IsLocalPlayerHost()) return;

            // messageType (1 byte) + clipId (4) + totalBytes (4) + sampleRate (4) + channels (4) = 17 bytes
            byte[] header = new byte[1 + 4 + 4 + 4 + 4];
            header[0] = (byte)AUDIO_HEADER;
            Buffer.BlockCopy(BitConverter.GetBytes(clipId), 0, header, 1, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(totalBytes), 0, header, 1 + 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(sampleRate), 0, header, 1 + 4 + 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(channels), 0, header, 1 + 4 + 4 + 4, 4);

            var lobbyOpt = SteamLobbyHelper.CurrentLobby;
            if (!lobbyOpt.HasValue || !lobbyOpt.Value.Id.IsValid)
            {
                Debug.LogWarning("[LakakaSpeaker] Pas de lobby courant valide");
                return;
            }
            Lobby lobby = lobbyOpt.Value;

            foreach (var member in lobby.Members)
            {
                SteamId memberSid = member.Id;
                if (memberSid == Steamworks.SteamClient.SteamId) continue;
                bool sent = SteamNetworking.SendP2PPacket(memberSid, header, header.Length, nChannel: 0, P2PSend.Reliable);
                if (!sent)
                {
                    L.LogWarning($"[LakakaSpeaker] Échec envoi header audio clip {clipId} à {member.Id}");
                }
            }
        }


        private void HandleIncomingPacket(SteamId sender, byte[] data)
        {
            if (data == null || data.Length == 0) return;
            byte messageType = data[0];

            switch (messageType)
            {
                case AUDIO_HEADER:
                    {
                        // data length doit être >= 1+4+4+4+4 = 17
                        if (data.Length < 1 + 4 + 4 + 4 + 4)
                        {
                            L.LogWarning("[LakakaSpeaker] Header audio invalide reçu");
                            return;
                        }
                        int clipId = BitConverter.ToInt32(data, 1);
                        int totalBytes = BitConverter.ToInt32(data, 1 + 4);
                        int sampleRate = BitConverter.ToInt32(data, 1 + 4 + 4);
                        int channels = BitConverter.ToInt32(data, 1 + 4 + 4 + 4);

                        int totalChunks = Mathf.CeilToInt((float)totalBytes / CHUNK_SIZE);
                        var buf = new ClipReceiveBuffer
                        {
                            TotalBytes = totalBytes,
                            SampleRate = sampleRate,
                            Channels = channels,
                            TotalChunks = totalChunks,
                            Chunks = new byte[totalChunks][],
                            ReceivedCount = 0
                        };
                        receiveBuffers[clipId] = buf;
                        L.LogInfo($"[LakakaSpeaker] Header audio reçu pour clip {clipId}, totalBytes={totalBytes}, sampleRate={sampleRate}, channels={channels}, totalChunks={totalChunks}");
                    }
                    break;

                case AUDIO_CHUNK:
                    {
                        // data length doit être >= 1+4+4 = 9
                        if (data.Length < 1 + 4 + 4)
                        {
                            L.LogWarning("[LakakaSpeaker] Chunk audio invalide reçu");
                            return;
                        }
                        int clipId = BitConverter.ToInt32(data, 1);
                        int chunkIndex = BitConverter.ToInt32(data, 1 + 4);
                        if (!receiveBuffers.TryGetValue(clipId, out var buffer))
                        {
                            L.LogWarning($"[LakakaSpeaker] Chunk reçu pour clip inconnu {clipId}");
                            return;
                        }
                        int payloadOffset = 1 + 4 + 4;
                        int payloadLength = data.Length - payloadOffset;
                        if (payloadLength <= 0)
                        {
                            L.LogWarning($"[LakakaSpeaker] Chunk vide reçu pour clip {clipId}, index {chunkIndex}");
                            return;
                        }
                        // Stocker chunk
                        if (chunkIndex < 0 || chunkIndex >= buffer.TotalChunks)
                        {
                            L.LogWarning($"[LakakaSpeaker] Chunk index hors limites: {chunkIndex} (totalChunks={buffer.TotalChunks}) pour clip {clipId}");
                            return;
                        }
                        if (buffer.Chunks[chunkIndex] == null)
                        {
                            byte[] chunkData = new byte[payloadLength];
                            Buffer.BlockCopy(data, payloadOffset, chunkData, 0, payloadLength);
                            buffer.Chunks[chunkIndex] = chunkData;
                            buffer.ReceivedCount++;
                            // Optionnel: log réception partielle
                            // L.LogInfo($"[LakakaSpeaker] Reçu chunk {chunkIndex}/{buffer.TotalChunks} pour clip {clipId}");
                        }
                        // Vérifier si on a tout reçu
                        if (buffer.ReceivedCount >= buffer.TotalChunks)
                        {
                            // Reconstituer le byte[] complet
                            byte[] allData = new byte[buffer.TotalBytes];
                            for (int i = 0; i < buffer.TotalChunks; i++)
                            {
                                int copyOffset = i * CHUNK_SIZE;
                                byte[] chunkBytes = buffer.Chunks[i];
                                int len = chunkBytes.Length;
                                Buffer.BlockCopy(chunkBytes, 0, allData, copyOffset, len);
                            }
                            // Convertir byte[] en float[] (suppose float 32-bit little-endian envoyé)
                            int sampleCount = allData.Length / sizeof(float);
                            float[] floatData = new float[sampleCount];
                            Buffer.BlockCopy(allData, 0, floatData, 0, allData.Length);

                            // Créer l’AudioClip et jouer
                            AudioClip clip = AudioClip.Create($"received_clip_{clipId}", sampleCount / buffer.Channels, buffer.Channels, buffer.SampleRate, false);
                            clip.SetData(floatData, 0);
                            PlayReceivedClip(clip);

                            receiveBuffers.Remove(clipId);
                            L.LogInfo($"[LakakaSpeaker] Clip {clipId} reconstitué et joué.");
                        }
                    }
                    break;

                case NCS_URL:
                    {
                        // URL à partir de data[1..]
                        string url = System.Text.Encoding.UTF8.GetString(data, 1, data.Length - 1);
                        L.LogInfo($"[LakakaSpeaker] URL NCS reçue: {url}");
                        // Démarrer téléchargement et lecture sur ce client
                        StartCoroutine(PlayNcsUrlOnClient(url));
                    }
                    break;

                default:
                    L.LogWarning($"[LakakaSpeaker] Type de message P2P inconnu: {messageType}");
                    break;
            }
        }


        private void PlayReceivedClip(AudioClip clip)
        {
            var src = GetOrCreateAudioSource();
            src.clip = clip;
            src.loop = false;
            src.volume = 0.8f;
            src.Play();
            // Gérer looper si voulu
        }

        private IEnumerator P2PPacket()
        {
            while (SteamNetworking.IsP2PPacketAvailable(channel: 0))
            {
                var packetOpt = SteamNetworking.ReadP2PPacket();
                if (packetOpt.HasValue)
                {
                    var steamIdSender = packetOpt.Value.SteamId;
                    var data = packetOpt.Value.Data;
                    HandleIncomingPacket(steamIdSender, data);
                }
                yield return null;
            }

        }

        #endregion
    }
}


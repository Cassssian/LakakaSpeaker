using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MenuLib;               // pour MenuAPI, REPOPopupPage, REPOButton, REPOInputField, etc.
using MenuLib.MonoBehaviors; // pour accéder à MenuManager.instance.StartCoroutine(...)
using MenuLib.Structs;      // pour la structure Padding
using REPOLib.Modules;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TMPro;                // pour TMP_InputField, TMP_Text
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Networking;

[assembly: CompilationRelaxations(8)]
[assembly: RuntimeCompatibility(WrapNonExceptionThrows = true)]
[assembly: Debuggable(DebuggableAttribute.DebuggingModes.Default | DebuggableAttribute.DebuggingModes.DisableOptimizations | DebuggableAttribute.DebuggingModes.IgnoreSymbolStoreSequencePoints | DebuggableAttribute.DebuggingModes.EnableEditAndContinue)]
[assembly: IgnoresAccessChecksTo("Assembly-CSharp-firstpass")]
[assembly: IgnoresAccessChecksTo("Assembly-CSharp")]
[assembly: IgnoresAccessChecksTo("Autodesk.Fbx")]
[assembly: IgnoresAccessChecksTo("Facepunch.Steamworks.Win64")]
[assembly: IgnoresAccessChecksTo("FbxBuildTestAssets")]
[assembly: IgnoresAccessChecksTo("Klattersynth")]
[assembly: IgnoresAccessChecksTo("Photon3Unity3D")]
[assembly: IgnoresAccessChecksTo("PhotonChat")]
[assembly: IgnoresAccessChecksTo("PhotonRealtime")]
[assembly: IgnoresAccessChecksTo("PhotonUnityNetworking")]
[assembly: IgnoresAccessChecksTo("PhotonUnityNetworking.Utilities")]
[assembly: IgnoresAccessChecksTo("PhotonVoice.API")]
[assembly: IgnoresAccessChecksTo("PhotonVoice")]
[assembly: IgnoresAccessChecksTo("PhotonVoice.PUN")]
[assembly: IgnoresAccessChecksTo("SingularityGroup.HotReload.Runtime")]
[assembly: IgnoresAccessChecksTo("SingularityGroup.HotReload.Runtime.Public")]
[assembly: IgnoresAccessChecksTo("Sirenix.OdinInspector.Attributes")]
[assembly: IgnoresAccessChecksTo("Sirenix.Serialization.Config")]
[assembly: IgnoresAccessChecksTo("Sirenix.Serialization")]
[assembly: IgnoresAccessChecksTo("Sirenix.Utilities")]
[assembly: IgnoresAccessChecksTo("Unity.AI.Navigation")]
[assembly: IgnoresAccessChecksTo("Unity.Formats.Fbx.Runtime")]
[assembly: IgnoresAccessChecksTo("Unity.InputSystem")]
[assembly: IgnoresAccessChecksTo("Unity.InputSystem.ForUI")]
[assembly: IgnoresAccessChecksTo("Unity.Postprocessing.Runtime")]
[assembly: IgnoresAccessChecksTo("Unity.RenderPipelines.Core.Runtime")]
[assembly: IgnoresAccessChecksTo("Unity.RenderPipelines.Core.ShaderLibrary")]
[assembly: IgnoresAccessChecksTo("Unity.RenderPipelines.ShaderGraph.ShaderGraphLibrary")]
[assembly: IgnoresAccessChecksTo("Unity.TextMeshPro")]
[assembly: IgnoresAccessChecksTo("Unity.Timeline")]
[assembly: IgnoresAccessChecksTo("Unity.VisualScripting.Antlr3.Runtime")]
[assembly: IgnoresAccessChecksTo("Unity.VisualScripting.Core")]
[assembly: IgnoresAccessChecksTo("Unity.VisualScripting.Flow")]
[assembly: IgnoresAccessChecksTo("Unity.VisualScripting.State")]
[assembly: IgnoresAccessChecksTo("UnityEngine.ARModule")]
[assembly: IgnoresAccessChecksTo("UnityEngine.NVIDIAModule")]
[assembly: IgnoresAccessChecksTo("UnityEngine.UI")]
[assembly: IgnoresAccessChecksTo("websocket-sharp")]

namespace Microsoft.CodeAnalysis
{
    [CompilerGenerated]
    [Microsoft.CodeAnalysis.Embedded]
    internal sealed class EmbeddedAttribute : Attribute
    {
    }
}
namespace System.Runtime.CompilerServices
{
    [CompilerGenerated]
    [Microsoft.CodeAnalysis.Embedded]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event | AttributeTargets.Parameter | AttributeTargets.ReturnValue | AttributeTargets.GenericParameter, AllowMultiple = false, Inherited = false)]
    internal sealed class NullableAttribute : Attribute
    {
        public readonly byte[] NullableFlags;

        public NullableAttribute(byte P_0)
        {
            NullableFlags = new byte[1] { P_0 };
        }

        public NullableAttribute(byte[] P_0)
        {
            NullableFlags = P_0;
        }
    }
    [CompilerGenerated]
    [Microsoft.CodeAnalysis.Embedded]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Interface | AttributeTargets.Delegate, AllowMultiple = false, Inherited = false)]
    internal sealed class NullableContextAttribute : Attribute
    {
        public readonly byte Flag;

        public NullableContextAttribute(byte P_0)
        {
            Flag = P_0;
        }
    }
    [CompilerGenerated]
    [Microsoft.CodeAnalysis.Embedded]
    [AttributeUsage(AttributeTargets.Module, AllowMultiple = false, Inherited = false)]
    internal sealed class RefSafetyRulesAttribute : Attribute
    {
        public readonly int Version;

        public RefSafetyRulesAttribute(int P_0)
        {
            Version = P_0;
        }
    }
}

namespace LakakaSpeaker
{
    [BepInPlugin("cassian.LakakaSpeaker", "LakakaSpeaker", "1.0.0")]
    [BepInDependency("PaintedThornStudios.PaintedUtils", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("REPOLib", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("nickklmao.menulib", BepInDependency.DependencyFlags.HardDependency)]
    public class LakakaSpeaker : BaseUnityPlugin
    {

        public ManualLogSource L => Logger;


        /// <summary>
        /// Instance singleton exposée pour que MusicManagerUI et le menu R.E.P.O. y accèdent.
        /// </summary>
        internal static LakakaSpeaker Instance { get; private set; } = null;


        private bool _initialized = false;
        private string pluginDir;
        private string musicDir;


        /// <summary>
        /// Ensemble des noms de fichiers (sans extension) qui sont “inclus” (chargés).
        /// Par défaut, on inclut tout.
        /// </summary>
        private HashSet<string> includedMusic = new HashSet<string>();


        private static readonly string BundleName = GetModName();
        private Dictionary<string, ConfigEntry<bool>> musicToggles = new Dictionary<string, ConfigEntry<bool>>();
        private static readonly Dictionary<ConfigEntryBase, object> changedEntryValues = new Dictionary<ConfigEntryBase, object>();

        private ConfigEntry<bool> reloadEntry;


        // With the following line to ensure compatibility with C# 8.0:
        private List<AudioClip> clips = new List<AudioClip>();


        private AssetBundle? _assetBundle;


        public string MusicDirectory => musicDir;


        private ConfigEntry<string> musicFoldersEntry;
        private List<string> musicFolders = new List<string>();
        internal static REPOButton lastClickedfolderButton;
        private static bool hasPopupMenuOpened;
        private static readonly Dictionary<ConfigEntryBase, object> originalEntryValues = new Dictionary<ConfigEntryBase, object>();




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
            return ((val != null) ? val.Name : null) ?? "LakakaSpeakerf";
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

            // 3) Charge dès à présent tous les clips (mais UI ne sera pas encore visible)
            StartCoroutine(LoadAllClips());

            // ─── NOUVEAU POUR REPO “Lakaka” : on ajoute notre bouton “Lakaka” au menu principal ──────────────────
            try
            {
                // Dans le Main Menu
                MenuAPI.AddElementToMainMenu(parent =>
                {
                    MenuAPI.CreateREPOButton("Lakaka", CreateLakakaFolderMenu, parent, new Vector2(120f, 55.5f));
                });
                // Dans le Lobby Menu
                MenuAPI.AddElementToLobbyMenu(parent =>
                {
                    MenuAPI.CreateREPOButton("Lakaka", CreateLakakaFolderMenu, parent, new Vector2(186f, 60f));
                });
                // Dans le Escape Menu
                MenuAPI.AddElementToEscapeMenu(parent =>
                {
                    MenuAPI.CreateREPOButton("Lakaka", CreateLakakaFolderMenu, parent, new Vector2(160f, 86f));
                });
            }
            catch (Exception ex)
            {
                L.LogError($"[LakakaSpeaker] Impossible d'ajouter le bouton REPO “Lakaka” : {ex}");
            }
            // ─────────────────────────────────────────────────────────────────────────────────────────────────
        }



        private IEnumerator InitRoutine()
        {
            // 1) Charge les clips audio
            yield return StartCoroutine(LoadAllClips());
            L.LogInfo($"🔊 Loaded {clips.Count} audio clips.");

            // 2) Patch Harmony maintenant que clips est prêt
            ((Component)this).gameObject.transform.parent = null;
            ((UnityEngine.Object)((Component)this).gameObject).hideFlags = (HideFlags)61;
            var harmony = new Harmony(Info.Metadata.GUID);
            harmony.PatchAll();
            L.LogInfo("Harmony patches applied.");

            // 3) Charge le bundle et enregistre les Valuables
            LoadAssetBundle();
            LoadValuablesFromResources();
            L.LogInfo($"LakakaSpeaker v{Info.Metadata.Version} has loaded!");
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

                // Si le fichier n’est pas “inclus”, on passe
                if (!includedMusic.Contains(fileName))
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
                L.LogWarning("No audio clips loaded (maybe none are included?).");
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
            if (!_initialized && clips.Count > 0)
            {
                _initialized = true;

                // Détach & cache le GameObject-plugin
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

            // Liste locale pour les boutons de dossiers
            var localFolderButtons = new List<REPOButton>();

            folderPage.AddElement(delegate (Transform parent)
            {
                MenuAPI.CreateREPOInputField("Folder Search", delegate (string s)
                {
                    string text = (string.IsNullOrEmpty(s) ? null : s.ToLower().Trim());
                    foreach (REPOButton btn in localFolderButtons)
                    {
                        btn.repoScrollViewElement.visibility =
                            (text == null || btn.labelTMP.text.ToLower().Contains(text));
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


        private static void CreateFolderList(REPOPopupPage folderPage, List<REPOButton> folderButtons)
        {
            // On vide la liste des boutons de dossier
            folderButtons.Clear();
            // On crée un bouton pour chaque dossier dans musicFolders
            foreach (KeyValuePair<string, ConfigEntryBase[]> folderConfigEntry in GetFolderEntries())
            {
                string folderName = folderConfigEntry.Key;
                ConfigEntryBase[] configEntryBases = folderConfigEntry.Value;
                folderPage.AddElementToScrollView(delegate (Transform parent)
                {
                    REPOButton folderButton = MenuAPI.CreateREPOButton(folderName, null, parent);
                    folderButton.labelTMP.fontStyle = FontStyles.Normal;
                    if (folderName.Length > 24)
                    {
                        REPOButton rEPOButton = folderButton;
                        Vector2 labelSize = folderButton.GetLabelSize();
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
                        MenuAPI.CloseAllPagesAddedOnTop();
                        string simplifiedFolderName = Path.GetFileName(folderName);
                        REPOPopupPage folderPage = MenuAPI.CreateREPOPopupPage(folderName, REPOPopupPage.PresetSide.Right, shouldCachePage: false, pageDimmerVisibility: false, 5f);
                        folderPage.scrollView.scrollSpeed = 3f;
                        folderPage.onEscapePressed = () => !hasPopupMenuOpened && changedEntryValues.Count == 0;
                        folderPage.AddElement(delegate (Transform mainPageParent)
                        {
                            MenuAPI.CreateREPOButton("Save Changes", delegate
                            {
                                KeyValuePair<ConfigEntryBase, object>[] array2 = changedEntryValues.ToArray();
                                changedEntryValues.Clear();
                                KeyValuePair<ConfigEntryBase, object>[] array3 = array2;
                                for (int i = 0; i < array3.Length; i++)
                                {
                                    KeyValuePair<ConfigEntryBase, object> keyValuePair2 = array3[i];
                                    ConfigEntryBase configEntryBase2 = keyValuePair2.Key;
                                    object obj2 = keyValuePair2.Value;
                                    object value = (configEntryBase2.BoxedValue = obj2);
                                    originalEntryValues[configEntryBase2] = value;
                                }
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
                                MenuAPI.OpenPopup("Reset " + folderName + "'" + (folderName.ToLower().EndsWith('s') ? string.Empty : "s") + " settings?", Color.red, "Are you sure you want to reset all settings back to default?", ResetToDefault);
                            }, scrollView);
                            rEPOButton2.rectTransform.localPosition = new Vector2((folderPage.maskRectTransform.rect.width - rEPOButton2.GetLabelSize().x) * 0.5f, 0f);
                            return rEPOButton2.rectTransform;
                        });
                        folderPage.AddElementToScrollView(delegate (Transform scrollView)
                        {
                            Vector2 size = new Vector2(0f, 10f);
                            return MenuAPI.CreateREPOSpacer(scrollView, default(Vector2), size).rectTransform;
                        });
                        CreateFolderEntries(folderPage, configEntryBases);
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

        private static void CreateFolderEntries(REPOPopupPage folderPage, ConfigEntryBase[] configEntryBases)
        {
            foreach (IGrouping<string, ConfigEntryBase> group in from configEntryBase2 in configEntryBases
                                                                 group configEntryBase2 by configEntryBase2.Definition.Section)
            {
                folderPage.AddElementToScrollView(delegate (Transform scrollView)
                {
                    REPOLabel rEPOLabel = MenuAPI.CreateREPOLabel((group.Key), scrollView);
                    rEPOLabel.labelTMP.fontStyle = FontStyles.Bold;
                    return rEPOLabel.rectTransform;
                });
                foreach (ConfigEntryBase entry in group)
                {
                    string folderName = (entry.Definition.Key);
                    originalEntryValues.Remove(entry);
                    originalEntryValues.Add(entry, entry.BoxedValue);
                    ConfigEntryBase configEntryBase = entry;
                    if (!(configEntryBase is ConfigEntry<bool>))
                    {
                        if (!(configEntryBase is ConfigEntry<float>))
                        {
                            if (!(configEntryBase is ConfigEntry<int>))
                            {
                                if (!(configEntryBase is ConfigEntry<string>))
                                {
                                    if (configEntryBase == null || !entry.SettingType.IsSubclassOf(typeof(Enum)))
                                    {
                                        continue;
                                    }
                                    Type enumType = entry.SettingType;
                                    string[] values = Enum.GetNames(enumType);
                                    folderPage.AddElementToScrollView(delegate (Transform scrollView)
                                    {
                                        REPOSlider rEPOSlider = MenuAPI.CreateREPOSlider(folderName, string.Empty, delegate (int i)
                                        {
                                            object obj = Enum.Parse(enumType, values[i]);
                                            if (originalEntryValues.TryGetValue(entry, out var value) && obj == value)
                                            {
                                                changedEntryValues.Remove(entry);
                                            }
                                            else
                                            {
                                                changedEntryValues[entry] = obj;
                                            }
                                        }, scrollView, values, entry.BoxedValue.ToString());
                                        TextMeshProUGUI descriptionTMP = rEPOSlider.descriptionTMP;
                                        FontStyles fontStyle = (rEPOSlider.labelTMP.fontStyle = FontStyles.Normal);
                                        descriptionTMP.fontStyle = fontStyle;
                                        return rEPOSlider.rectTransform;
                                    });
                                    continue;
                                }
                                AcceptableValueBase acceptableValues = entry.Description.AcceptableValues;
                                AcceptableValueList<string> acceptableValueList = acceptableValues as AcceptableValueList<string>;
                                if (acceptableValueList != null)
                                {
                                    folderPage.AddElementToScrollView(delegate (Transform scrollView)
                                    {
                                        REPOSlider rEPOSlider = MenuAPI.CreateREPOSlider(folderName, string.Empty, delegate (string s)
                                        {
                                            if (originalEntryValues.TryGetValue(entry, out var value) && s == (string)value)
                                            {
                                                changedEntryValues.Remove(entry);
                                            }
                                            else
                                            {
                                                changedEntryValues[entry] = s;
                                            }
                                        }, scrollView, acceptableValueList.AcceptableValues, (string)entry.BoxedValue);
                                        TextMeshProUGUI descriptionTMP = rEPOSlider.descriptionTMP;
                                        FontStyles fontStyle = (rEPOSlider.labelTMP.fontStyle = FontStyles.Normal);
                                        descriptionTMP.fontStyle = fontStyle;
                                        return rEPOSlider.rectTransform;
                                    });
                                    continue;
                                }
                                folderPage.AddElementToScrollView(delegate (Transform scrollView)
                                {
                                    string text = (string)entry.DefaultValue;
                                    REPOInputField rEPOInputField = MenuAPI.CreateREPOInputField(folderName, delegate (string s)
                                    {
                                        if (originalEntryValues.TryGetValue(entry, out var value) && s == (string)value)
                                        {
                                            changedEntryValues.Remove(entry);
                                        }
                                        else
                                        {
                                            changedEntryValues[entry] = s;
                                        }
                                    }, scrollView, Vector2.zero, onlyNotifyOnSubmit: false, (!string.IsNullOrEmpty(text)) ? text : "<NONE>", (string)entry.BoxedValue);
                                    TextMeshProUGUI labelTMP = rEPOInputField.labelTMP;
                                    FontStyles fontStyle = (rEPOInputField.inputStringSystem.inputTMP.fontStyle = FontStyles.Normal);
                                    labelTMP.fontStyle = fontStyle;
                                    return rEPOInputField.rectTransform;
                                });
                                continue;
                            }
                            folderPage.AddElementToScrollView(delegate (Transform scrollView)
                            {
                                int num;
                                int num2;
                                int num4;
                                if (entry.Description.AcceptableValues is AcceptableValueRange<int> acceptableValueRange)
                                {
                                    num = acceptableValueRange.MinValue;
                                    num2 = acceptableValueRange.MaxValue;
                                }
                                else
                                {
                                    int num3 = Math.Abs((int)entry.BoxedValue);
                                    num4 = ((num3 > 100) ? (-(num2 = num3 * 2)) : ((num3 != 0) ? (-(num2 = num3 * 3)) : (-(num2 = 100))));
                                    num = num4;
                                }
                                string text = folderName;
                                string empty = string.Empty;
                                Action<int> onValueChanged = delegate (int i)
                                {
                                    if (originalEntryValues.TryGetValue(entry, out var value) && i == (int)value)
                                    {
                                        changedEntryValues.Remove(entry);
                                    }
                                    else
                                    {
                                        changedEntryValues[entry] = i;
                                    }
                                };
                                num4 = (int)entry.BoxedValue;
                                int min = num;
                                int max = num2;
                                REPOSlider rEPOSlider = MenuAPI.CreateREPOSlider(text, empty, onValueChanged, scrollView, default(Vector2), min, max, num4);
                                TextMeshProUGUI descriptionTMP = rEPOSlider.descriptionTMP;
                                FontStyles fontStyle = (rEPOSlider.labelTMP.fontStyle = FontStyles.Normal);
                                descriptionTMP.fontStyle = fontStyle;
                                return rEPOSlider.rectTransform;
                            });
                            continue;
                        }
                        folderPage.AddElementToScrollView(delegate (Transform scrollView)
                        {
                            int num = 2;
                            float num2;
                            float num3;
                            if (entry.Description.AcceptableValues is AcceptableValueRange<float> acceptableValueRange)
                            {
                                num2 = acceptableValueRange.MinValue;
                                num3 = acceptableValueRange.MaxValue;
                                num = 1;
                            }
                            else
                            {
                                float num4 = Math.Abs((float)entry.BoxedValue);
                                num2 = ((num4 == 0f) ? (0f - (num3 = 100f)) : (((double)num4 <= 0.001) ? (0f - (num3 = 10f)) : (((double)num4 <= 0.01) ? (0f - (num3 = 50f)) : ((!(num4 <= 100f)) ? (0f - (num3 = num4 * 2f)) : (0f - (num3 = num4 * 3f))))));
                            }
                            string text = folderName;
                            string empty = string.Empty;
                            Action<float> onValueChanged = delegate (float f)
                            {
                                if (originalEntryValues.TryGetValue(entry, out var value) && Math.Abs(f - (float)value) < float.Epsilon)
                                {
                                    changedEntryValues.Remove(entry);
                                }
                                else
                                {
                                    changedEntryValues[entry] = f;
                                }
                            };
                            float defaultValue = (float)entry.BoxedValue;
                            float min = num2;
                            float max = num3;
                            int precision = num;
                            REPOSlider rEPOSlider = MenuAPI.CreateREPOSlider(text, empty, onValueChanged, scrollView, default(Vector2), min, max, precision, defaultValue);
                            TextMeshProUGUI descriptionTMP = rEPOSlider.descriptionTMP;
                            FontStyles fontStyle = (rEPOSlider.labelTMP.fontStyle = FontStyles.Normal);
                            descriptionTMP.fontStyle = fontStyle;
                            return rEPOSlider.rectTransform;
                        });
                        continue;
                    }
                    folderPage.AddElementToScrollView(delegate (Transform scrollView)
                    {
                        string text = folderName;
                        Action<bool> onToggle = delegate (bool b)
                        {
                            if (originalEntryValues.TryGetValue(entry, out var value) && b == (bool)value)
                            {
                                changedEntryValues.Remove(entry);
                            }
                            else
                            {
                                changedEntryValues[entry] = b;
                            }
                        };
                        bool defaultValue = (bool)entry.BoxedValue;
                        REPOToggle rEPOToggle = MenuAPI.CreateREPOToggle(text, onToggle, scrollView, default(Vector2), "Enabled", "Disabled", defaultValue);
                        rEPOToggle.labelTMP.fontStyle = FontStyles.Normal;
                        return rEPOToggle.rectTransform;
                    });
                }
                folderPage.AddElementToScrollView(delegate (Transform scrollView)
                {
                    Vector2 size = new Vector2(0f, 20f);
                    return MenuAPI.CreateREPOSpacer(scrollView, default(Vector2), size).rectTransform;
                });
            }
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

    }
}


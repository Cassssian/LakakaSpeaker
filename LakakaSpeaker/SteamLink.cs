using Steamworks;
using Steamworks.Data;
using UnityEngine;
using System;
using System.Text.RegularExpressions;

public class LobbyLogListener : MonoBehaviour
{
    // Regex pour extraire un entier (ulong) après "Lobby created with ID:"
    // On peut être plus permissif si le format varie.
    private static readonly Regex LobbyCreatedRegex = new Regex(@"Lobby created with ID:\s*(\d+)", RegexOptions.Compiled);

    void OnEnable()
    {
        Application.logMessageReceived += HandleLog;
    }

    void OnDisable()
    {
        Application.logMessageReceived -= HandleLog;
    }

    private void HandleLog(string logString, string stackTrace, LogType type)
    {
        // Vérifier si la ligne de log contient la création/jointure du lobby
        // Par exemple : "Steam: Lobby created with ID: 109775240926689900"
        var match = LobbyCreatedRegex.Match(logString);
        if (match.Success)
        {
            string idStr = match.Groups[1].Value;
            if (ulong.TryParse(idStr, out ulong lobbyId))
            {
                Debug.Log($"[LobbyLogListener] Détecté Lobby ID dans log: {lobbyId}");
                SteamLobbyHelper.CurrentLobbyId = lobbyId;
                // Tenter de rejoindre ou récupérer l’objet Lobby Facepunch
                TryObtainLobbyObject(lobbyId);
            }
        }
    }

    private async void TryObtainLobbyObject(ulong lobbyId)
    {
        try
        {
            SteamId sid = lobbyId;
            Debug.Log($"[LobbyLogListener] Appel JoinLobbyAsync pour ID {lobbyId}");
            Lobby? lobbyOpt = await SteamMatchmaking.JoinLobbyAsync(sid);
            if (lobbyOpt.HasValue)
            {
                Lobby lobbyObj = lobbyOpt.Value;

                SteamLobbyHelper.SetCurrentLobby(lobbyObj);
                Debug.Log($"[LobbyLogListener] CurrentLobby enregistré: {lobbyObj.Id.Value}");
            }
            else
            {
                Debug.LogWarning("[LobbyLogListener] JoinLobbyAsync a renvoyé un Lobby invalide.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[LobbyLogListener] Exception JoinLobbyAsync: {ex}");
        }
    }

}


public static class HostDetector
{
    public static ulong GetLocalSteamId() => SteamClient.SteamId;

    public static ulong GetLobbyOwnerSteamId()
    {
        var lobbyOpt = SteamLobbyHelper.CurrentLobby;
        if (lobbyOpt.HasValue && lobbyOpt.Value.Id.IsValid)
        {
            return lobbyOpt.Value.Owner.Id.Value;
        }
        return 0UL;
    }

    public static bool IsLocalPlayerHost()
    {
        ulong local = GetLocalSteamId();
        ulong owner = GetLobbyOwnerSteamId();
        return local != 0UL && owner != 0UL && local == owner;
    }
}

public static class SteamLobbyHelper
{
    // Stocke le Lobby Facepunch.Steamworks courant
    public static Lobby? CurrentLobby { get; set; }

    // (Optionnel) stocke l’ID brut si vous voulez y accéder directement
    public static ulong CurrentLobbyId { get; set; }

    public static void SetCurrentLobby(Lobby? lobby)
    {
        CurrentLobby = lobby;
        if (lobby.HasValue && lobby.Value.Id.IsValid)
        {
            CurrentLobbyId = lobby.Value.Id.Value;
        }
    }

    public static void SetCurrentLobbyId(ulong lobbyId)
    {
        CurrentLobbyId = lobbyId;
    }
}

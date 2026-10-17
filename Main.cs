using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace WhiteKnuckle_Re_Zero
{
    [BepInPlugin("com.cr-i.whiteknuckle_rezero", "WhiteKnuckle Re_Zero", "1.0.0")]
    public class Main : BaseUnityPlugin
    {
        private const float SoundCooldown = 0.5f;
        private static readonly string[] SupportedAudioFiles = { "revive.ogg", "revive.wav", "revive.mp3" };

        new static ManualLogSource logger;

        private static AudioClip _customReviveClip;
        private static AudioSource _audioSource;
        private static float _lastSoundPlayTime = -10f;

        private void Awake()
        {
            logger = Logger;
            var harmony = new Harmony("com.cr-i.whiteknuckle_rezero");

            EnsureAudioSource();

            string foundPath = null;

            foreach (var file in SupportedAudioFiles)
            {
                var rootPath = Path.Combine(Paths.PluginPath, file);
                var modPath = Path.Combine(Paths.PluginPath, "WhiteKnuckle_Re_Zero", file);

                if (File.Exists(rootPath))
                {
                    foundPath = rootPath;
                    break;
                }

                if (File.Exists(modPath))
                {
                    foundPath = modPath;
                    break;
                }
            }

            if (!string.IsNullOrEmpty(foundPath))
                StartCoroutine(LoadCustomAudio(foundPath));
            else
                logger.LogWarning("The revive audio file was not found in the BepInEx/plugins directory.");

            harmony.PatchAll();
            logger.LogInfo("The mod loaded and all patches were applied.");
        }

        private static IEnumerator LoadCustomAudio(string path)
        {
            var fileUri = new Uri(path).AbsoluteUri;

            var audioType = AudioType.OGGVORBIS;
            if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                audioType = AudioType.WAV;
            else if (path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                audioType = AudioType.MPEG;

            using (var www = UnityWebRequestMultimedia.GetAudioClip(fileUri, audioType))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    _customReviveClip = DownloadHandlerAudioClip.GetContent(www);

                    if (_customReviveClip != null)
                        logger.LogInfo(
                            $"Loaded audio file '{Path.GetFileName(path)}' ({_customReviveClip.length:F2}s).");
                    else
                        logger.LogError(
                            "The audio file was loaded but could not be converted to an AudioClip. Check the codec format.");
                }
                else
                {
                    logger.LogError($"Failed to load the audio file: {www.error}");
                }
            }
        }

        private static void EnsureAudioSource()
        {
            if (_audioSource != null)
                return;

            var soundHost = new GameObject("WhiteKnuckle_AudioHost");
            DontDestroyOnLoad(soundHost);
            _audioSource = soundHost.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f;
        }

        // ReSharper disable Unity.PerformanceAnalysis
        internal static void PlayCustomSound()
        {
            if (_customReviveClip == null)
            {
                logger.LogWarning("The revive sound could not be played because the AudioClip is not loaded.");
                return;
            }

            if (Time.unscaledTime - _lastSoundPlayTime < SoundCooldown)
                return;

            EnsureAudioSource();

            _lastSoundPlayTime = Time.unscaledTime;
            _audioSource.volume = 1f;
            _audioSource.PlayOneShot(_customReviveClip);
            logger.LogInfo("The revive sound was played.");
        }

        private static class GameAccess
        {
            private static readonly FieldInfo FGmDead = AccessTools.Field(typeof(CL_GameManager), "dead");

            private static readonly FieldInfo FOnPlayerDeath =
                AccessTools.Field(typeof(CL_GameManager), "_onPlayerDeath");

            private static readonly FieldInfo FReviving = AccessTools.Field(typeof(CL_GameManager), "reviving");
            private static readonly FieldInfo FLockPlayer = AccessTools.Field(typeof(CL_GameManager), "lockPlayer");
            private static readonly FieldInfo FLocalPlayer = AccessTools.Field(typeof(CL_GameManager), "localPlayer");
            private static readonly FieldInfo FUiMan = AccessTools.Field(typeof(CL_GameManager), "uiMan");
            private static readonly FieldInfo FPlayerDead = AccessTools.Field(typeof(ENT_Player), "dead");

            private static readonly MethodInfo MResetVignettes =
                AccessTools.Method(typeof(CL_UIManager), "ResetVignettes");

            public static bool GmDead
            {
                get => (bool)FGmDead.GetValue(null);
                set => FGmDead.SetValue(null, value);
            }

            public static Action OnPlayerDeath => (Action)FOnPlayerDeath.GetValue(null);

            public static void SetReviving(CL_GameManager gm, bool val)
            {
                FReviving.SetValue(gm, val);
            }

            public static void SetLockPlayer(CL_GameManager gm, bool val)
            {
                FLockPlayer.SetValue(gm, val);
            }

            public static ENT_Player GetLocalPlayer(CL_GameManager gm)
            {
                return (ENT_Player)FLocalPlayer.GetValue(gm);
            }

            public static CL_UIManager GetUiMan(CL_GameManager gm)
            {
                return (CL_UIManager)FUiMan.GetValue(gm);
            }

            public static void SetPlayerDead(ENT_Player player, bool val)
            {
                FPlayerDead.SetValue(player, val);
            }

            public static void ResetVignettes(CL_UIManager ui)
            {
                MResetVignettes.Invoke(ui, null);
            }
        }

        [HarmonyPatch(typeof(CL_GameManager), "Die")]
        public static class PatchClGameManagerDie
        {
            [HarmonyPrefix]
            public static bool Prefix(CL_GameManager __instance, string type = "")
            {
                if (GameAccess.GmDead)
                    return false;

                var save = CL_SaveManager.GetMostRecentSaveStateByType(CL_SaveManager.SaveState.SaveType.disk);

                // Kayıt yoksa hiç müdahale etme; return true ile oyunun kendi orijinal Die ve EndGame ekranını çalıştır.
                if (save == null)
                    return true;

                GameAccess.GmDead = true;
                var onPlayerDeath = GameAccess.OnPlayerDeath;
                if (onPlayerDeath != null)
                    onPlayerDeath();

                StatManager.sessionStats.UpdateStatistic("deaths", 1, StatManager.Statistic.DataType.Int,
                    StatManager.Statistic.ModType.Add);

                __instance.StartCoroutine(ReviveSequence());

                IEnumerator ReviveSequence()
                {
                    var localPlayer = GameAccess.GetLocalPlayer(__instance);
                    var uiMan = GameAccess.GetUiMan(__instance);

                    if (DEN_DeathFloor.instance != null)
                        DEN_DeathFloor.instance.StopAllCoroutines();

                    CL_EventManager.StopAllEvents();
                    GameAccess.SetReviving(__instance, true);
                    CL_UIManager.instance.SetVignetteTarget("bloodied", 0.0f);
                    uiMan.reviveGroup.gameObject.SetActive(false);
                    AudioManager.FadeOutMusic();

                    if (WorldLoader.instance != null)
                        WorldLoader.instance.ClearCurrentGenerator();

                    localPlayer.SetGravityMult(1f);
                    localPlayer.SetGravity(true);
                    localPlayer.Lock();

                    __instance.GetComponent<AudioSource>().Play();
                    save.UpdatePlayerData(localPlayer);
                    CL_SaveManager.LoadSave(save);

                    localPlayer.SetGravityMult(1f);
                    localPlayer.SetGravity(true);
                    GameAccess.GmDead = false;
                    CL_GameManager.SetTimescale(1f);
                    localPlayer.UnLock();
                    localPlayer.AddGripStrength(20f);

                    if (WorldLoader.initialized)
                        while (!WorldLoader.isLoaded)
                            yield return null;

                    GameAccess.SetReviving(__instance, false);

                    uiMan.reviveGroup.alpha = 0.0f;
                    GameAccess.SetReviving(__instance, false);
                    GameAccess.SetLockPlayer(__instance, false);
                    localPlayer.SetGravityMult(1f);
                    localPlayer.SetGravity(true);
                    CL_UIManager.instance.SetVignetteTarget("bloodied", 0.0f);
                    GameAccess.ResetVignettes(uiMan);
                    CL_AchievementManager.SetAchievementValue("ACH_REVIVE", true);
                    StatManager.sessionStats.UpdateStatistic("revives", 1, StatManager.Statistic.DataType.Int,
                        StatManager.Statistic.ModType.Add);

                    localPlayer.SetGravity(true);
                    if (localPlayer.GetTagger() != null)
                        localPlayer.GetTagger().RemoveTag("Dead");

                    GameAccess.SetPlayerDead(localPlayer, false);
                    PlayCustomSound();
                    CL_UIManager.instance.SetVignetteTarget("bloodied", 0.0f);
                    uiMan.reviveGroup.alpha = 0.0f;
                    uiMan.reviveGroup.gameObject.SetActive(false);
                }

                return false;
            }
        }
    }
}
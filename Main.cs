using System;
using System.Collections;
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

        internal static new ManualLogSource Logger;
        internal static Main Instance;

        private static bool isLoadingSave;
        internal static AudioClip customReviveClip;
        internal static AudioSource audioSource;
        private static float lastSoundPlayTime = -10f;

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;

            EnsureAudioSource();

            string foundPath = null;

            foreach (string file in SupportedAudioFiles)
            {
                string rootPath = Path.Combine(Paths.PluginPath, file);
                string modPath = Path.Combine(Paths.PluginPath, "WhiteKnuckle_Re_Zero", file);

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
            {
                StartCoroutine(LoadCustomAudio(foundPath));
            }
            else
            {
                Logger.LogWarning("The revive audio file was not found in the BepInEx/plugins directory.");
            }

            Harmony.CreateAndPatchAll(typeof(LoadSaveHookPatch));
            Harmony.CreateAndPatchAll(typeof(ReviveHookPatch));

            Logger.LogInfo("The mod loaded and all patches were applied.");
        }

        private static IEnumerator LoadCustomAudio(string path)
        {
            string fileUri = new System.Uri(path).AbsoluteUri;

            AudioType audioType = AudioType.OGGVORBIS;
            if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                audioType = AudioType.WAV;
            else if (path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                audioType = AudioType.MPEG;

            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(fileUri, audioType))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    customReviveClip = DownloadHandlerAudioClip.GetContent(www);

                    if (customReviveClip != null)
                    {
                        Logger.LogInfo($"Loaded audio file '{Path.GetFileName(path)}' ({customReviveClip.length:F2}s).");
                    }
                    else
                    {
                        Logger.LogError("The audio file was loaded but could not be converted to an AudioClip. Check the codec format.");
                    }
                }
                else
                {
                    Logger.LogError($"Failed to load the audio file: {www.error}");
                }
            }
        }

        private static void EnsureAudioSource()
        {
            if (audioSource != null)
                return;

            GameObject soundHost = new GameObject("WhiteKnuckle_AudioHost");
            DontDestroyOnLoad(soundHost);
            audioSource = soundHost.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
        }

        internal static void PlayCustomSound()
        {
            if (customReviveClip == null)
            {
                Logger.LogWarning("The revive sound could not be played because the AudioClip is not loaded.");
                return;
            }

            if (Time.unscaledTime - lastSoundPlayTime < SoundCooldown)
                return;

            EnsureAudioSource();

            lastSoundPlayTime = Time.unscaledTime;
            audioSource.volume = 1f;
            audioSource.PlayOneShot(customReviveClip);
            Logger.LogInfo("The revive sound was played.");
        }

        private static IEnumerator EmptyDelayRoutine()
        {
            yield break;
        }

        [HarmonyPatch]
        public static class LoadSaveHookPatch
        {
            [HarmonyPatch(typeof(CL_SaveManager), "LoadSave")]
            [HarmonyPrefix]
            public static void BeforeLoadSave()
            {
                isLoadingSave = true;
                Main.Logger.LogInfo("Save loading started; transition effects are disabled.");
            }

            [HarmonyPatch(typeof(CL_UIManager), "FadeOut")]
            [HarmonyPrefix]
            public static bool SkipFadeOut()
            {
                return !isLoadingSave;
            }

            [HarmonyPatch(typeof(CL_UIManager), "FadeIn")]
            [HarmonyPrefix]
            public static bool SkipFadeIn()
            {
                return !isLoadingSave;
            }

            [HarmonyPatch(typeof(CL_SaveManager.SaveState), "LoadDelay")]
            [HarmonyPrefix]
            public static bool SkipLoadDelay(ref IEnumerator __result)
            {
                __result = EmptyDelayRoutine();
                return false;
            }

            [HarmonyPatch(typeof(CL_GameManager), "ResetPlayerDistanceCheckPosition")]
            [HarmonyPostfix]
            public static void OnWorldLoadedInstant()
            {
                if (!isLoadingSave)
                    return;

                isLoadingSave = false;
                Main.Logger.LogInfo("The scene loaded and opened without transition effects.");
                PlayCustomSound();
            }
        }

        [HarmonyPatch]
        public static class ReviveHookPatch
        {
            [HarmonyTargetMethod]
            public static MethodBase TargetMethod()
            {
                foreach (var method in AccessTools.GetDeclaredMethods(typeof(CL_GameManager)))
                {
                    if (method.Name.Contains("ReviveSequence"))
                        return method;
                }

                foreach (var nestedType in typeof(CL_GameManager).GetNestedTypes(AccessTools.all))
                {
                    foreach (var method in AccessTools.GetDeclaredMethods(nestedType))
                    {
                        if (method.Name.Contains("ReviveSequence"))
                            return method;
                    }
                }

                Main.Logger.LogError("The ReviveSequence method could not be found.");
                return null;
            }

            [HarmonyPrefix]
            public static bool Prefix(object __instance, ref IEnumerator __result)
            {
                CL_GameManager gm = __instance as CL_GameManager;
                
                if (gm == null && __instance != null)
                {
                    var thisField = Traverse.Create(__instance).Field("<>4__this");
                    if (thisField.FieldExists())
                        gm = thisField.GetValue<CL_GameManager>();
                }

                if (gm == null)
                    gm = CL_GameManager.gMan;

                CL_SaveManager.SaveState saveObj = null;
                if (__instance != null)
                {
                    var saveTraverse = Traverse.Create(__instance).Field("save");
                    if (saveTraverse.FieldExists())
                    {
                        saveObj = saveTraverse.GetValue<CL_SaveManager.SaveState>();
                    }
                    else
                    {
                        foreach (var field in AccessTools.GetDeclaredFields(__instance.GetType()))
                        {
                            if (field.FieldType == typeof(CL_SaveManager.SaveState))
                            {
                                saveObj = field.GetValue(__instance) as CL_SaveManager.SaveState;
                                if (saveObj != null) break;
                            }
                        }
                    }
                }

                if (gm != null)
                {
                    __result = InstantReviveRoutine(gm, saveObj);
                    return false;
                }

                return true;
            }

            private static IEnumerator InstantReviveRoutine(CL_GameManager gm, CL_SaveManager.SaveState save)
            {
                if ((UnityEngine.Object) DEN_DeathFloor.instance != (UnityEngine.Object) null)
                    DEN_DeathFloor.instance.StopAllCoroutines();

                CL_EventManager.StopAllEvents();

                Traverse.Create(gm).Field("reviving").SetValue(true);
                CL_GameManager.SetTimescale(1f);

                if ((UnityEngine.Object) WorldLoader.instance != (UnityEngine.Object) null)
                    WorldLoader.instance.ClearCurrentGenerator();

                if (gm.localPlayer != null)
                {
                    gm.localPlayer.SetGravityMult(1f);
                    gm.localPlayer.SetGravity(true);
                    gm.localPlayer.Lock();
                }

                if (gm.uiMan != null && gm.uiMan.reviveGroup != null)
                {
                    gm.uiMan.reviveGroup.alpha = 0f;
                    gm.uiMan.reviveGroup.gameObject.SetActive(false);
                }

                if (save != null && gm.localPlayer != null)
                {
                    save.UpdatePlayerData(gm.localPlayer);
                    CL_SaveManager.LoadSave(save);
                }

                if (WorldLoader.initialized)
                {
                    while (!WorldLoader.isLoaded)
                        yield return null;
                }

                if (gm.localPlayer != null)
                {
                    gm.localPlayer.SetGravityMult(1f);
                    gm.localPlayer.SetGravity(true);
                    gm.localPlayer.UnLock();
                    gm.localPlayer.AddGripStrength(20f);
                    Traverse.Create(gm.localPlayer).Field("dead").SetValue(false);

                    if ((UnityEngine.Object) gm.localPlayer.GetTagger() != (UnityEngine.Object) null)
                        gm.localPlayer.GetTagger().RemoveTag("Dead");
                }

                Traverse.Create(typeof(CL_GameManager)).Field("dead").SetValue(false);
                CL_GameManager.SetTimescale(1f);

                Traverse.Create(gm).Field("reviving").SetValue(false);
                Traverse.Create(gm).Field("lockPlayer").SetValue(false);

                if (gm.uiMan != null)
                {
                    Traverse.Create(gm.uiMan).Method("ResetVignettes").GetValue();
                    if (gm.uiMan.reviveGroup != null)
                    {
                        gm.uiMan.reviveGroup.alpha = 0f;
                        gm.uiMan.reviveGroup.gameObject.SetActive(false);
                    }
                }

                CL_AchievementManager.SetAchievementValue("ACH_REVIVE", true);
                StatManager.sessionStats.UpdateStatistic("revives", (object) 1, StatManager.Statistic.DataType.Int, StatManager.Statistic.ModType.Add);

                Main.Logger.LogInfo("Revive completed.");

                PlayCustomSound();
            }
        }
    }
}
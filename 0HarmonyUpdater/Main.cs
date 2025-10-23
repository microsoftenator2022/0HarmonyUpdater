using System.Collections;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;

using Kingmaker;
using Kingmaker.Code.UI.MVVM;
using Kingmaker.GameModes;
using Kingmaker.Modding;
using Kingmaker.PubSubSystem;
using Kingmaker.PubSubSystem.Core;
using Kingmaker.UI.Legacy.MainMenuUI;
using Kingmaker.Utility;

using Owlcat.Runtime.Core.Logging;
using Owlcat.Runtime.UniRx;

using UnityEngine;
using UnityEngine.Networking;

namespace _0HarmonyUpdater;

public static class Main
{
    static async Task WaitForCallback(Action<Action> receiver) => await WaitForCallback<int>(action => receiver(() => action(0)));

    static async Task<T> WaitForCallback<T>(Action<Action<T>> receiver)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        receiver(tcs.SetResult);

        return await tcs.Task;
    }

    static Version ParseFileVersion(string path) => Version.Parse(FileVersionInfo.GetVersionInfo(path).FileVersion);

    static bool UpdateFromGithub = true;
    
    static readonly Regex ReleaseRegex = new(@"\/pardeike\/Harmony\/releases\/tag\/v(\d+\.\d+\.\d+\.\d+)");
    
    static void ShowMessageBox(string message) => EventBus.RaiseEvent<IDialogMessageBoxUIHandler>(w => w.HandleOpen(message));

    static async Task<byte[]> TryDownloadHarmonyRelease(Version currentVersion)
    {
        static string[] GetLines(string s) =>
            s.Split('\n')
                .Select(static l => l.Trim())
                .Where(static l => !string.IsNullOrEmpty(l))
                .ToArray();

        var uriBase = @"https://github.com";
        var releasesUri = $@"{uriBase}/pardeike/Harmony/releases";

        var requestOp = UnityWebRequest.Get(releasesUri).SendWebRequest();
        var webRequest = requestOp.webRequest;

        _ = await WaitForCallback<AsyncOperation>(action => requestOp.completed += action);

        if (webRequest.result is not UnityWebRequest.Result.Success)
            return [];

        var releasePage = webRequest.downloadHandler.text;

        var (releasePath, releaseVersionString) = GetLines(releasePage)
            .Select(l => ReleaseRegex.Match(l))
            .Where(m => m.Success)
            .Select(m => (path: m.Value, version: m.Groups[1].Value))
            .OrderByDescending(r => Version.Parse(r.version))
            .First();

        var releaseVersion = Version.Parse(releaseVersionString);

        Log.Log($"Found Harmony version {releaseVersion}");

        if (releaseVersion <= currentVersion)
            return [];

        var expandedReleaseAssetsUri = $@"{uriBase}{releasePath.Replace("tag", "expanded_assets")}";

        requestOp = UnityWebRequest.Get(expandedReleaseAssetsUri).SendWebRequest();
        webRequest = requestOp.webRequest;

        _ = await WaitForCallback<AsyncOperation>(action => requestOp.completed += action);

        if (webRequest.result is not UnityWebRequest.Result.Success)
            return [];

        var expandedReleaseAssets = webRequest.downloadHandler.text;

        var zipPathRegex = new Regex ($@"/pardeike/Harmony/releases/download/v{releaseVersionString}/Harmony[\w\-]*\.{releaseVersionString}.zip");

        var zips = GetLines(expandedReleaseAssets)
            .Select(l => zipPathRegex.Match(l))
            .Where(l => l.Success)
            .Select(l => l.Value)
            .ToArray();

        var zipPath = zips.FirstOrDefault(p => p.Contains("Fat")) ?? zips.First();

        var zipUri = $@"{uriBase}{zipPath}";

        Log.Log($"Downloading {zipUri}");

        requestOp = UnityWebRequest.Get(zipUri).SendWebRequest();
        webRequest = requestOp.webRequest;

        _ = await WaitForCallback<AsyncOperation>(action => requestOp.completed += action);

        if (webRequest.result is not UnityWebRequest.Result.Success)
            return [];

        using var archive = new ZipArchive(new MemoryStream(webRequest.downloadHandler.data));

        var entry = archive.Entries.First(entry => entry.FullName == @"net48/0Harmony.dll");
        var buffer = new byte[entry.Length];
        using var s = entry.Open();
        _ = s.Read(buffer);

        return buffer;
    }

    static LogChannel Log = null!;

    static void HarmonyUpdate()
    {
        if (File.Exists(Path.Join(Path.GetDirectoryName(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)), "DisableWebUpdate.txt")))
            UpdateFromGithub = false;

        var managedDir = Path.Join(Application.dataPath, "Managed");
        var harmonyPath = Path.Join(managedDir, "0Harmony.dll");

        var includedHarmonyPath = Path.Join(
            Path.GetDirectoryName(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)),
            "0Harmony.dll");

        var harmonyAss = AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(ass => ass.GetName().Name == "0Harmony");

        if (harmonyAss is not null)
            Log.Log($"Harmony version {harmonyAss.GetName().Version} is loaded already");
        else
            Log.Log("Harmony is not loaded (yet)");

        var currentVersion = ParseFileVersion(harmonyPath);

        Log.Log($"Current Harmony version = {currentVersion}");

        var includedVersion = ParseFileVersion(includedHarmonyPath);

        Log.Log($"Bundled Harmony version = {includedVersion}");

        byte[] newHarmony = [];

        void doUpdate()
        {
            if (newHarmony.Length == 0 && includedVersion > currentVersion)
                newHarmony = File.ReadAllBytes(includedHarmonyPath);

            if (newHarmony.Length == 0)
                return;

            File.Move(harmonyPath, $"{harmonyPath}.{currentVersion}");
            File.WriteAllBytes(harmonyPath, newHarmony);
            var newHarmonyVersion = ParseFileVersion(harmonyPath);
            Log.Log($"Harmony version is now {newHarmonyVersion}");

            if (harmonyAss is not null)
            {
                Log.Log("Restart for changes to take effect");

                var message = $"Harmony updated ({currentVersion} -> {newHarmonyVersion})\nPlease restart for the change to take effect.";
                
                DelayedInvoker.InvokeWhenTrue(() => ShowMessageBox(message), () => RootUIContext.Instance?.IsMainMenu is true);
            }
        }

        if (UpdateFromGithub)
        {
            try
            {
                TryDownloadHarmonyRelease(currentVersion)
                    .ContinueWith(t =>
                    {
                        newHarmony = t.Result;
                        doUpdate();
                    });
            }
            catch(Exception ex)
            {
                Log.Exception(ex);
            }
        }
        else doUpdate();
    }

    [OwlcatModificationEnterPoint]
    public static void Load(OwlcatModification mod)
    {
        Log = mod.Logger;
        
        HarmonyUpdate();
    }
}
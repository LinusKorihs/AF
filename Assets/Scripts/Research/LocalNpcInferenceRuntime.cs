using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

public enum LocalNpcBackend { Cpu, VulkanGpu }

public sealed class LocalNpcInferenceRuntime : IDisposable
{
    [Serializable]
    private sealed class ModelsResponse { public ModelEntry[] data; }

    [Serializable]
    private sealed class ModelEntry { public string id; }

    private readonly LocalNpcDialogueSettings settings;
    private Process process;

    public bool IsReady { get; private set; }
    public string Status { get; private set; } = "No model loaded.";

    public LocalNpcInferenceRuntime(LocalNpcDialogueSettings settings)
    {
        this.settings = settings;
    }

    public static bool DetectVulkanGpu()
    {
        if (Application.platform != RuntimePlatform.WindowsEditor
            && Application.platform != RuntimePlatform.WindowsPlayer)
            return false;

        string executable = Path.Combine(Application.streamingAssetsPath,
            "LocalInference", "windows-x64-vulkan", "llama-server.exe");
        if (!File.Exists(executable)) return false;

        try
        {
            var start = new ProcessStartInfo(executable, "--list-devices")
            {
                WorkingDirectory = Path.GetDirectoryName(executable),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var probe = Process.Start(start))
            {
                if (probe == null || !probe.WaitForExit(5000))
                {
                    if (probe != null) probe.Kill();
                    return false;
                }
                string devices = probe.StandardOutput.ReadToEnd() + probe.StandardError.ReadToEnd();
                return probe.ExitCode == 0
                    && Regex.IsMatch(devices, @"(?m)^\s*Vulkan\d+:");
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning("[NPC Pilot] Vulkan device detection failed: " + ex.Message);
            return false;
        }
    }

    public IEnumerator LoadModel(LocalNpcDialogueSettings.ModelOption model,
        LocalNpcBackend backend, string warmupBody)
    {
        Stop();
        IsReady = false;
        if (Application.platform != RuntimePlatform.WindowsEditor
            && Application.platform != RuntimePlatform.WindowsPlayer)
        {
            Status = "The bundled runtime currently supports Windows x64 only.";
            yield break;
        }
        if (model == null || string.IsNullOrWhiteSpace(model.id)
            || string.IsNullOrWhiteSpace(model.fileName))
        {
            Status = "The selected model configuration is incomplete.";
            yield break;
        }

        string runtimeFolder = backend == LocalNpcBackend.VulkanGpu
            ? "windows-x64-vulkan" : "windows-x64";
        string executable = Path.Combine(Application.streamingAssetsPath,
            "LocalInference", runtimeFolder, "llama-server.exe");
        string modelFile = Path.GetFullPath(Path.Combine(
            Application.dataPath, "..", "LocalModels", model.fileName));
        if (!File.Exists(executable))
        {
            Status = "The bundled llama-server.exe is missing from " + runtimeFolder + ".";
            yield break;
        }
        if (!File.Exists(modelFile))
        {
            Status = "The GGUF file is missing from LocalModels: " + model.fileName;
            yield break;
        }

        bool portBusy = false;
        try
        {
            using (var probe = new TcpClient("127.0.0.1", settings.Port))
                portBusy = true;
        }
        catch (SocketException) { }
        if (portBusy)
        {
            Status = "Port " + settings.Port + " is in use. Stop the other server first.";
            yield break;
        }

        try
        {
            var start = new ProcessStartInfo(executable,
                "-m \"" + modelFile + "\" --host 127.0.0.1 --port " + settings.Port
                + " -c " + settings.ContextTokens + " -a " + model.id
                + " --parallel " + settings.ParallelRequests
                + (backend == LocalNpcBackend.VulkanGpu ? " -ngl 99" : ""))
            {
                WorkingDirectory = Path.GetDirectoryName(executable),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            process = Process.Start(start);
        }
        catch (Exception ex)
        {
            Status = "llama.cpp could not start: " + ex.Message;
            yield break;
        }

        bool healthy = false;
        float deadline = Time.realtimeSinceStartup + 60f;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (process == null || process.HasExited)
            {
                Status = "llama.cpp exited before the model was ready.";
                Stop();
                yield break;
            }
            using (var health = UnityWebRequest.Get(settings.BaseUrl + "/health"))
            {
                health.timeout = 2;
                yield return health.SendWebRequest();
                if (health.result == UnityWebRequest.Result.Success)
                {
                    healthy = true;
                    break;
                }
            }
            yield return new WaitForSecondsRealtime(0.5f);
        }
        if (!healthy)
        {
            Status = "llama.cpp did not become healthy within 60 seconds.";
            Stop();
            yield break;
        }

        bool listed = false;
        using (var verify = UnityWebRequest.Get(settings.BaseUrl + "/v1/models"))
        {
            verify.timeout = 10;
            yield return verify.SendWebRequest();
            if (verify.result == UnityWebRequest.Result.Success)
            {
                var response = JsonUtility.FromJson<ModelsResponse>(verify.downloadHandler.text);
                if (response?.data != null)
                    foreach (var entry in response.data)
                        if (entry != null && entry.id == model.id)
                            listed = true;
            }
        }
        if (!listed)
        {
            Status = "llama.cpp did not confirm the loaded model: " + model.id;
            Stop();
            yield break;
        }

        Status = model.id + " loaded. Warming up the first dialogue request...";
        using (var warmup = UnityWebRequest.Post(settings.ChatEndpoint, warmupBody, "application/json"))
        {
            warmup.timeout = 120;
            yield return warmup.SendWebRequest();
            if (warmup.result != UnityWebRequest.Result.Success)
            {
                Status = "Model warmup failed: " + warmup.error;
                Stop();
                yield break;
            }
        }
        IsReady = true;
        Status = model.id + " is ready.";
    }

    public void Stop()
    {
        IsReady = false;
        if (process == null) return;
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning("[NPC Pilot] llama.cpp could not be stopped: " + ex.Message);
        }
        finally
        {
            process.Dispose();
            process = null;
        }
    }

    public void Dispose() => Stop();
}

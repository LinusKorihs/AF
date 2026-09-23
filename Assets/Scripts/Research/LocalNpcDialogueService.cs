using System;
using System.Collections;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Networking;

public sealed class LocalNpcDialogueService : MonoBehaviour
{
    private LocalNpcDialogueSettings settings;
    private TextAsset knowledgeJson;
    private LocalNpcInferenceRuntime runtime;
    private NpcDialogueCsvLogger csv;
    private NpcWorldFactSource[] worldSources = Array.Empty<NpcWorldFactSource>();
    private string activeModelId = "";

    public LocalNpcBackend Backend { get; private set; }
    public bool GpuAvailable { get; private set; }
    public bool IsBusy { get; private set; }
    public bool IsReady => runtime != null && runtime.IsReady;
    public string ActiveModelId => activeModelId;
    public string Status { get; private set; } = "Dialogue service is not configured.";
    public LocalNpcDialogueSettings Settings => settings;

    public void Configure(LocalNpcDialogueSettings configuredSettings, TextAsset configuredKnowledge)
    {
        if (runtime != null && settings == configuredSettings && knowledgeJson == configuredKnowledge)
            return;
        runtime?.Dispose();
        settings = configuredSettings;
        knowledgeJson = configuredKnowledge;
        if (settings == null)
        {
            Status = "Assign Local NPC Dialogue Settings in the prefab.";
            return;
        }
        runtime = new LocalNpcInferenceRuntime(settings);
        csv = new NpcDialogueCsvLogger(settings);
        RefreshWorldSources();
        GpuAvailable = LocalNpcInferenceRuntime.DetectVulkanGpu();
        Backend = GpuAvailable ? LocalNpcBackend.VulkanGpu : LocalNpcBackend.Cpu;
        Status = GpuAvailable ? "GPU detected. Load a model."
            : "No Vulkan GPU detected. CPU selected; load a model.";
    }

    private void OnDestroy() => runtime?.Dispose();

    public void RefreshWorldSources()
    {
        worldSources = NpcDialogueProtocol.FindWorldSources(this);
    }

    public bool SetBackend(LocalNpcBackend backend)
    {
        if (settings == null || IsBusy || backend == LocalNpcBackend.VulkanGpu && !GpuAvailable)
            return false;
        if (Backend == backend) return true;
        runtime.Stop();
        Backend = backend;
        activeModelId = "";
        Status = "Backend changed. Load a model.";
        return true;
    }

    public bool SetDeliveryActive(bool active)
    {
        RefreshWorldSources();
        foreach (var source in worldSources)
        {
            if (source == null || source.FactId != settings.DeliveryFactId) continue;
            source.gameObject.SetActive(active);
            return true;
        }
        return false;
    }

    public bool DeliveryArrived
    {
        get
        {
            RefreshWorldSources();
            return settings != null && NpcDialogueProtocol.IsFactActive(
                worldSources, settings.DeliveryFactId);
        }
    }

    public string DisplayName(string npcId)
    {
        return TryGetKnowledge(out var knowledge, out _)
            ? NpcDialogueProtocol.FindNpc(knowledge, npcId)?.displayName ?? npcId : npcId;
    }

    public bool TryGetKnowledge(out NpcKnowledgeFile knowledge, out string problem)
    {
        knowledge = null;
        problem = "Settings asset is missing.";
        if (settings == null) return false;
        RefreshWorldSources();
        return NpcDialogueProtocol.TryLoadKnowledge(knowledgeJson, worldSources,
            settings.DeliveryFactId, out knowledge, out problem);
    }

    public IEnumerator ActivateModel(int index, Action<bool> completed = null)
    {
        if (settings == null || runtime == null || IsBusy)
        {
            completed?.Invoke(false);
            yield break;
        }
        var model = settings.GetModel(index);
        if (model == null)
        {
            Status = "Select a configured model in the prefab.";
            completed?.Invoke(false);
            yield break;
        }
        if (!TryGetKnowledge(out var knowledge, out var problem))
        {
            Status = "Knowledge file: " + problem;
            UnityEngine.Debug.LogWarning("[NPC Pilot] " + Status);
            completed?.Invoke(false);
            yield break;
        }

        IsBusy = true;
        runtime.Stop();
        activeModelId = "";
        Status = model.id + " is loading...";
        bool delivered = NpcDialogueProtocol.IsFactActive(worldSources, settings.DeliveryFactId);
        string warmup = NpcDialogueProtocol.BuildRequest(settings, model, knowledge.npcs[0],
            delivered, settings.WarmupQuestion, knowledge, worldSources,
            settings.WarmupOutputTokens, false);
        var timer = Stopwatch.StartNew();
        yield return runtime.LoadModel(model, Backend, warmup);
        timer.Stop();
        if (runtime.IsReady)
        {
            activeModelId = model.id;
            Status = model.id + " is ready via " + BackendLabel
                + " (startup: " + timer.Elapsed.TotalSeconds.ToString("F1") + " s).";
            UnityEngine.Debug.Log("[NPC Pilot] " + Status);
        }
        else
        {
            Status = runtime.Status;
            UnityEngine.Debug.LogWarning("[NPC Pilot] " + Status);
        }
        IsBusy = false;
        completed?.Invoke(runtime.IsReady);
    }

    public IEnumerator Send(NpcDialogueRequest input, Action<NpcDialogueResult> completed)
    {
        var result = CreateBaseResult(input);
        if (input == null || settings == null || runtime == null)
        {
            CompleteFailure(result, NpcDialogueErrorCode.Configuration,
                "Dialogue request or settings are missing.", completed);
            yield break;
        }
        if (IsBusy || !runtime.IsReady)
        {
            CompleteFailure(result, NpcDialogueErrorCode.NotReady,
                "The local model is not ready.", completed);
            yield break;
        }
        if (!TryGetKnowledge(out var knowledge, out var problem))
        {
            CompleteFailure(result, NpcDialogueErrorCode.Configuration,
                "Knowledge file: " + problem, completed);
            yield break;
        }
        var model = FindActiveModel();
        var npc = NpcDialogueProtocol.FindNpc(knowledge, input.NpcId);
        if (model == null || npc == null || string.IsNullOrWhiteSpace(input.Question))
        {
            CompleteFailure(result, NpcDialogueErrorCode.Configuration,
                "Model, NPC, or question is not configured.", completed);
            yield break;
        }

        IsBusy = true;
        bool delivered = NpcDialogueProtocol.IsFactActive(worldSources, settings.DeliveryFactId);
        input.DeliveryArrived = delivered;
        if (string.IsNullOrWhiteSpace(input.ExpectedState))
            input.ExpectedState = NpcDialogueProtocol.ExpectedState(delivered);
        result = CreateBaseResult(input);
        result.Model = model.id;
        result.KnowledgeHash = NpcDialogueProtocol.KnowledgeHash(knowledgeJson.text);
        result.WorldObjects = NpcDialogueProtocol.WorldStateSnapshot(worldSources);

        string body = NpcDialogueProtocol.BuildRequest(settings, model, npc, delivered,
            input.Question, knowledge, worldSources, settings.MaxOutputTokens, true);
        var timer = Stopwatch.StartNew();
        using (var request = UnityWebRequest.Post(settings.ChatEndpoint, body, "application/json"))
        {
            var stream = new LocalNpcSseResponseHandler(timer);
            request.downloadHandler = stream;
            request.timeout = settings.RequestTimeoutSeconds;
            yield return request.SendWebRequest();
            result.RawResponse = stream.Content.Trim();
            result.FirstTextMilliseconds = stream.FirstContentMilliseconds;
            result.LastTextMilliseconds = stream.LastContentMilliseconds;
            result.OutputTokens = stream.OutputTokens;
            result.ClientOutputTokensPerSecond = OutputRate(stream);

            if (request.result != UnityWebRequest.Result.Success)
            {
                var code = request.error != null
                    && request.error.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0
                    ? NpcDialogueErrorCode.Timeout : NpcDialogueErrorCode.Network;
                result.Validation = Failure(code, "Request failed: " + request.error);
            }
            else if (!string.IsNullOrEmpty(stream.ParseError))
            {
                result.Validation = Failure(NpcDialogueErrorCode.SseParse,
                    "SSE response could not be parsed: " + stream.ParseError);
            }
            else result.Validation = NpcDialogueProtocol.Validate(result.RawResponse, delivered);
        }
        timer.Stop();
        result.ValidatedResponseMilliseconds = timer.Elapsed.TotalMilliseconds;
        csv.Write(result);
        Status = result.IsUsable ? "Response received and validated."
            : result.Validation.Error;
        LogResult(result);
        IsBusy = false;
        completed?.Invoke(result);
    }

    private NpcDialogueResult CreateBaseResult(NpcDialogueRequest input) => new NpcDialogueResult {
        Request = input,
        Backend = Backend.ToString(),
        Model = activeModelId,
        RawResponse = ""
    };

    private void CompleteFailure(NpcDialogueResult result, NpcDialogueErrorCode code,
        string message, Action<NpcDialogueResult> completed)
    {
        result.Validation = Failure(code, message);
        Status = message;
        if (result.Request != null && csv != null) csv.Write(result);
        UnityEngine.Debug.LogWarning("[NPC Pilot] " + message);
        completed?.Invoke(result);
    }

    private static NpcDialogueValidation Failure(NpcDialogueErrorCode code, string message)
    {
        return new NpcDialogueValidation { ErrorCode = code, Error = message };
    }

    private static double? OutputRate(LocalNpcSseResponseHandler stream)
    {
        return stream.OutputTokens > 1 && stream.FirstContentMilliseconds.HasValue
            && stream.LastContentMilliseconds.HasValue
            && stream.LastContentMilliseconds > stream.FirstContentMilliseconds
            ? (stream.OutputTokens - 1) * 1000.0
                / (stream.LastContentMilliseconds.Value - stream.FirstContentMilliseconds.Value)
            : (double?)null;
    }

    private void LogResult(NpcDialogueResult result)
    {
        string measurements = "First text: "
            + (result.FirstTextMilliseconds.HasValue
                ? result.FirstTextMilliseconds.Value.ToString("F0") + " ms" : "not captured")
            + " | Validated response: " + result.ValidatedResponseMilliseconds.ToString("F0")
            + " ms | Output tokens: " + result.OutputTokens
            + " | JSON/fields/state: " + result.Validation.SyntaxValid + "/"
            + result.Validation.StructureValid + "/" + result.Validation.StateValid;
        if (result.IsUsable) UnityEngine.Debug.Log("[NPC Pilot] " + measurements);
        else UnityEngine.Debug.LogWarning("[NPC Pilot] " + result.Validation.Error);
    }

    private LocalNpcDialogueSettings.ModelOption FindActiveModel()
    {
        if (settings?.Models == null) return null;
        foreach (var option in settings.Models)
            if (option != null && option.id == activeModelId) return option;
        return null;
    }

    private string BackendLabel => Backend == LocalNpcBackend.VulkanGpu
        ? "llama.cpp Vulkan (GPU)" : "llama.cpp CPU";
}

using System;
using System.Collections;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Networking;

// Authored prefab used to operate the local NPC dialogue experiment.
public sealed class LocalNpcDialoguePilot : MonoBehaviour
{
    [SerializeField, Tooltip("Settings asset containing model choices, inference parameters, and logging options.")]
    private LocalNpcDialogueSettings settings;
    [SerializeField, Tooltip("Model loaded at scene start when automatic loading is enabled.")]
    private int startupModel;
    [SerializeField, Tooltip("Question prefilled in the dialogue panel. It can be edited during Play Mode.")]
    private string question = "How is the village's food supply?";
    [SerializeField, Tooltip("JSON file containing world facts, NPC roles, knowledge boundaries, and response instructions.")]
    private TextAsset knowledgeJson;
    [SerializeField, Tooltip("Load and warm up the selected startup model when the scene starts.")]
    private bool loadModelOnSceneStart;

    private LocalNpcBackend backend = LocalNpcBackend.Cpu;
    private LocalNpcInferenceRuntime runtime;
    private NpcDialogueCsvLogger csv;
    private NpcWorldFactSource[] worldSources = Array.Empty<NpcWorldFactSource>();
    private int selectedNpc;
    private string activeModelId = "";
    private string status = "Choose a backend and model above.";
    private string reply = "";
    private string measurements = "";
    private bool requestRunning;
    private bool gpuAvailable;

    private void Awake()
    {
        worldSources = NpcDialogueProtocol.FindWorldSources(this);
        if (settings == null)
        {
            status = "Assign Local NPC Dialogue Settings in the prefab.";
            UnityEngine.Debug.LogError("[NPC Pilot] " + status);
            return;
        }
        runtime = new LocalNpcInferenceRuntime(settings);
        csv = new NpcDialogueCsvLogger(settings);
        gpuAvailable = LocalNpcInferenceRuntime.DetectVulkanGpu();
        backend = gpuAvailable ? LocalNpcBackend.VulkanGpu : LocalNpcBackend.Cpu;
        status = gpuAvailable ? "GPU detected. Load a model."
            : "No Vulkan GPU detected. CPU selected; load a model.";
    }

    private void Start()
    {
        if (loadModelOnSceneStart && settings != null)
            StartCoroutine(ActivateModel(startupModel));
    }

    private void OnDestroy() => runtime?.Dispose();

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(12, 12, 620, 510), GUI.skin.box);
        GUILayout.Label("Local NPC Dialogue: Pilot Test");
        GUILayout.Label("Local runtime:");
        GUILayout.BeginHorizontal();
        GUI.enabled = !requestRunning && runtime != null;
        if (gpuAvailable && GUILayout.Button(backend == LocalNpcBackend.VulkanGpu
                ? "● GPU (Vulkan)" : "GPU (Vulkan)"))
            ChangeBackend(LocalNpcBackend.VulkanGpu);
        if (GUILayout.Button(backend == LocalNpcBackend.Cpu ? "● CPU" : "CPU"))
            ChangeBackend(LocalNpcBackend.Cpu);
        GUILayout.EndHorizontal();

        GUILayout.Label("Load / switch model:");
        GUILayout.BeginHorizontal();
        if (settings?.Models != null)
            for (int i = 0; i < settings.Models.Length; i++)
            {
                int index = i;
                var option = settings.Models[i];
                if (option != null && GUILayout.Button(option.label))
                    StartCoroutine(ActivateModel(index));
            }
        GUILayout.EndHorizontal();
        GUI.enabled = true;
        GUILayout.Label("Active: " + (runtime != null && runtime.IsReady
            ? activeModelId : "no confirmed model"));

        if (TryGetKnowledge(out var knowledge, out _, false))
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < knowledge.npcs.Length; i++)
            {
                int index = i;
                string name = string.IsNullOrWhiteSpace(knowledge.npcs[i].displayName)
                    ? knowledge.npcs[i].id : knowledge.npcs[i].displayName;
                if (GUILayout.Toggle(selectedNpc == index, name, "Button")) selectedNpc = index;
            }
            GUILayout.EndHorizontal();
        }

        GUILayout.Label("World objects (authored in the scene):");
        foreach (var source in worldSources)
        {
            if (source == null) continue;
            bool active = source.gameObject.activeSelf;
            bool requested = GUILayout.Toggle(active,
                source.DisplayName + (source.gameObject.activeInHierarchy ? " [visible]" : " [hidden]"),
                "Button");
            if (requested != active) source.gameObject.SetActive(requested);
        }
        bool delivered = settings != null && NpcDialogueProtocol.IsFactActive(
            worldSources, settings.DeliveryFactId);
        GUILayout.Label("Supply: " + (delivered ? "temporarily improved" : "critical"));
        GUILayout.Label("Player question:");
        question = GUILayout.TextField(question);
        GUI.enabled = !requestRunning && runtime != null && runtime.IsReady
            && !string.IsNullOrWhiteSpace(question);
        if (GUILayout.Button("Ask NPC")) StartCoroutine(SendQuestion());
        GUI.enabled = true;

        GUILayout.Label(status);
        if (!string.IsNullOrEmpty(reply)) GUILayout.TextArea(reply, GUILayout.Height(85));
        if (!string.IsNullOrEmpty(measurements)) GUILayout.Label(measurements);
        GUILayout.EndArea();
    }

    private bool TryGetKnowledge(out NpcKnowledgeFile knowledge, out string problem,
        bool refreshSources = true)
    {
        knowledge = null;
        problem = "Settings asset is missing.";
        if (settings == null) return false;
        if (refreshSources) worldSources = NpcDialogueProtocol.FindWorldSources(this);
        return NpcDialogueProtocol.TryLoadKnowledge(knowledgeJson, worldSources,
            settings.DeliveryFactId, out knowledge, out problem);
    }

    private void ChangeBackend(LocalNpcBackend next)
    {
        if (backend == next || next == LocalNpcBackend.VulkanGpu && !gpuAvailable) return;
        runtime.Stop();
        backend = next;
        activeModelId = "";
        reply = measurements = "";
        status = "Backend changed. Load a model.";
    }

    private IEnumerator ActivateModel(int index)
    {
        var model = settings.GetModel(index);
        if (model == null)
        {
            status = "Select a configured model in the prefab.";
            yield break;
        }
        if (!TryGetKnowledge(out var knowledge, out var problem))
        {
            status = "Knowledge file: " + problem;
            UnityEngine.Debug.LogWarning("[NPC Pilot] " + status);
            yield break;
        }
        requestRunning = true;
        runtime.Stop();
        activeModelId = reply = measurements = "";
        status = model.id + " is loading...";
        bool delivered = NpcDialogueProtocol.IsFactActive(worldSources, settings.DeliveryFactId);
        string warmup = NpcDialogueProtocol.BuildRequest(settings, model, knowledge.npcs[0],
            delivered, settings.WarmupQuestion, knowledge, worldSources,
            settings.WarmupOutputTokens, false);
        var timer = Stopwatch.StartNew();
        yield return StartCoroutine(runtime.LoadModel(model, backend, warmup));
        timer.Stop();
        if (runtime.IsReady)
        {
            activeModelId = model.id;
            status = model.id + " is ready via " + (backend == LocalNpcBackend.VulkanGpu
                ? "llama.cpp Vulkan (GPU)" : "llama.cpp CPU")
                + " (startup: " + timer.Elapsed.TotalSeconds.ToString("F1") + " s).";
            UnityEngine.Debug.Log("[NPC Pilot] " + status);
        }
        else
        {
            status = runtime.Status;
            UnityEngine.Debug.LogWarning("[NPC Pilot] " + status);
        }
        requestRunning = false;
    }

    private IEnumerator SendQuestion()
    {
        if (!TryGetKnowledge(out var knowledge, out var problem))
        {
            status = "Knowledge file: " + problem;
            UnityEngine.Debug.LogWarning("[NPC Pilot] " + status);
            yield break;
        }
        var model = FindActiveModel();
        if (model == null)
        {
            status = "The active model is not configured.";
            yield break;
        }
        requestRunning = true;
        reply = measurements = "";
        status = "Local request running...";
        selectedNpc = Mathf.Clamp(selectedNpc, 0, knowledge.npcs.Length - 1);
        var npc = knowledge.npcs[selectedNpc];
        bool delivered = NpcDialogueProtocol.IsFactActive(worldSources, settings.DeliveryFactId);
        string snapshot = NpcDialogueProtocol.WorldStateSnapshot(worldSources);
        string hash = NpcDialogueProtocol.KnowledgeHash(knowledgeJson.text);
        string asked = question;
        string body = NpcDialogueProtocol.BuildRequest(settings, model, npc, delivered,
            asked, knowledge, worldSources, settings.MaxOutputTokens, true);
        var timer = Stopwatch.StartNew();
        using (var request = UnityWebRequest.Post(settings.ChatEndpoint, body, "application/json"))
        {
            var stream = new LocalNpcSseResponseHandler(timer);
            request.downloadHandler = stream;
            request.timeout = 120;
            yield return request.SendWebRequest();
            timer.Stop();
            var validation = request.result == UnityWebRequest.Result.Success
                ? NpcDialogueProtocol.Validate(stream.Content.Trim(), delivered)
                : new NpcDialogueValidation();
            string error = request.result == UnityWebRequest.Result.Success
                ? validation.Error : "Request failed: " + request.error;
            if (!string.IsNullOrEmpty(stream.ParseError))
                error = (error ?? "") + " SSE parse: " + stream.ParseError;
            reply = validation.Dialogue ?? "";
            status = !string.IsNullOrWhiteSpace(error) ? error
                : validation.StructureValid ? "Response received." : "Response format incomplete.";
            double? rate = stream.OutputTokens > 1 && stream.FirstContentMilliseconds.HasValue
                && stream.LastContentMilliseconds > stream.FirstContentMilliseconds
                ? (stream.OutputTokens - 1) * 1000.0
                    / (stream.LastContentMilliseconds.Value - stream.FirstContentMilliseconds.Value)
                : (double?)null;
            measurements = "First text: "
                + (stream.FirstContentMilliseconds.HasValue
                    ? stream.FirstContentMilliseconds.Value.ToString("F0") + " ms"
                    : "not captured")
                + " | Complete response: " + timer.Elapsed.TotalMilliseconds.ToString("F0")
                + " ms | Output tokens: " + stream.OutputTokens
                + " | approx. " + (rate?.ToString("F1") ?? "?") + " tokens/s"
                + " | JSON/fields/state: " + validation.SyntaxValid + "/"
                + validation.StructureValid + "/" + validation.StateValid;
            csv.Write(new NpcDialogueCsvLogger.Entry {
                Backend = backend.ToString(), KnowledgeHash = hash, Model = model.id,
                Npc = npc.id, WorldObjects = snapshot, Question = asked,
                DeliveryArrived = delivered, SyntaxValid = validation.SyntaxValid,
                StructureValid = validation.StructureValid, StateValid = validation.StateValid,
                TotalMilliseconds = timer.Elapsed.TotalMilliseconds, Stream = stream, Error = error
            });
            if (string.IsNullOrWhiteSpace(error))
                UnityEngine.Debug.Log("[NPC Pilot] " + measurements + " | " + stream.Content.Trim());
            else UnityEngine.Debug.LogWarning("[NPC Pilot] " + error);
        }
        requestRunning = false;
    }

    private LocalNpcDialogueSettings.ModelOption FindActiveModel()
    {
        if (settings.Models == null) return null;
        foreach (var option in settings.Models)
            if (option != null && option.id == activeModelId) return option;
        return null;
    }
}

using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

// Entry point for Player Mode and Research Mode.
public sealed class LocalNpcDialoguePilot : MonoBehaviour
{
    [SerializeField, Tooltip("Settings asset containing models, inference parameters, and logging options.")]
    private LocalNpcDialogueSettings settings;
    [SerializeField, Tooltip("Model loaded at scene start when automatic loading is enabled.")]
    private int startupModel;
    [SerializeField, Tooltip("Question prefilled in the Research Mode panel.")]
    private string question = "How is the village's food supply?";
    [SerializeField, Tooltip("JSON file containing world facts, NPC roles, and response instructions.")]
    private TextAsset knowledgeJson;
    [SerializeField, Tooltip("Versioned test catalog used by the automated Research Mode run.")]
    private NpcDialogueTestCatalogAsset testCatalog;
    [SerializeField, Tooltip("Load and warm up the selected startup model when the scene starts.")]
    private bool loadModelOnSceneStart = true;
    [SerializeField, Tooltip("Player shows the game-facing dialogue UI; Research shows technical controls.")]
    private LocalNpcUiMode mode = LocalNpcUiMode.Player;
    [SerializeField, Tooltip("Keyboard bindings used to open a dialogue with an NPC in Player Mode.")]
    private NpcHotkeyBinding[] npcKeys = {
        new NpcHotkeyBinding { npcId = "Farmer", key = Key.Q },
        new NpcHotkeyBinding { npcId = "Knight", key = Key.E }
    };
    [SerializeField, Tooltip("One-way Player Mode key that makes the supply delivery arrive.")]
    private Key deliveryKey = Key.R;
    [SerializeField, Min(1), Tooltip("Measured repeats per test case and model in the Research Mode suite.")]
    private int batchRepeats = 10;
    [SerializeField, Tooltip("Run the full measured suite on CPU after all Vulkan combinations finish.")]
    private bool includeCpuInBatch = true;

    private LocalNpcDialogueService service;
    private NpcDialoguePlayerController playerController;
    private NpcDialogueBatchRunner batchRunner;
    private int selectedNpc;
    private string reply = "";
    private string measurements = "";
    private NpcDialogueTestCatalog runtimeCatalog;
    private Vector2 catalogScroll;
    private string repeatInput = "10";
    private string catalogStatus = "";

    private void Awake()
    {
        service = GetComponent<LocalNpcDialogueService>();
        if (service == null) service = gameObject.AddComponent<LocalNpcDialogueService>();
        playerController = GetComponent<NpcDialoguePlayerController>();
        if (playerController == null) playerController = gameObject.AddComponent<NpcDialoguePlayerController>();
        batchRunner = GetComponent<NpcDialogueBatchRunner>();
        if (batchRunner == null) batchRunner = gameObject.AddComponent<NpcDialogueBatchRunner>();

        service.Configure(settings, knowledgeJson);
        playerController.Configure(service, npcKeys, deliveryKey);
        batchRunner.Configure(service, batchRepeats, includeCpuInBatch);
        ResetRuntimeCatalog();
        repeatInput = batchRepeats.ToString();
        playerController.SetPlayerModeEnabled(mode == LocalNpcUiMode.Player);
    }

    private void Start()
    {
        if (HasCommandLineArgument("--npc-batch"))
        {
            mode = LocalNpcUiMode.Research;
            playerController.SetPlayerModeEnabled(false);
            int repeats = ReadIntArgument("--repeat", batchRepeats);
            string output = ReadStringArgument("--output");
            string caseId = ReadStringArgument("--case");
            bool includeCpu = includeCpuInBatch && !HasCommandLineArgument("--gpu-only");
            batchRunner.StartSuite(runtimeCatalog, repeats, output, true, caseId, includeCpu);
        }
        else if (loadModelOnSceneStart && settings != null)
            StartCoroutine(service.ActivateModel(startupModel));
    }

    private void OnGUI()
    {
        if (mode != LocalNpcUiMode.Research || settings == null || service == null) return;
        GUILayout.BeginArea(new Rect(12, 12, 760, Mathf.Max(620, Screen.height - 24)), GUI.skin.box);
        GUILayout.Label("Local NPC Dialogue: Research Mode");
        GUILayout.Label("Local runtime:");
        GUILayout.BeginHorizontal();
        GUI.enabled = !service.IsBusy && !batchRunner.IsRunning;
        if (service.GpuAvailable && GUILayout.Button(
                service.Backend == LocalNpcBackend.VulkanGpu ? "● GPU (Vulkan)" : "GPU (Vulkan)"))
            service.SetBackend(LocalNpcBackend.VulkanGpu);
        if (GUILayout.Button(service.Backend == LocalNpcBackend.Cpu ? "● CPU" : "CPU"))
            service.SetBackend(LocalNpcBackend.Cpu);
        GUILayout.EndHorizontal();

        GUILayout.Label("Load / switch model:");
        GUILayout.BeginHorizontal();
        if (settings.Models != null)
            for (int i = 0; i < settings.Models.Length; i++)
            {
                int index = i;
                var option = settings.Models[i];
                if (option != null && GUILayout.Button(option.label))
                    StartCoroutine(service.ActivateModel(index));
            }
        GUILayout.EndHorizontal();
        GUI.enabled = true;
        GUILayout.Label("Active: " + (service.IsReady ? service.ActiveModelId : "no confirmed model"));

        if (service.TryGetKnowledge(out var knowledge, out _))
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

        GUILayout.Label("Authoritative supply state:");
        GUILayout.BeginHorizontal();
        GUI.enabled = !service.IsBusy && !batchRunner.IsRunning;
        if (GUILayout.Toggle(!service.DeliveryArrived, "Critical / delivery absent", "Button"))
            service.SetDeliveryActive(false);
        if (GUILayout.Toggle(service.DeliveryArrived, "Temporarily improved / delivery present", "Button"))
            service.SetDeliveryActive(true);
        GUILayout.EndHorizontal();

        GUILayout.Label("Player question:");
        question = GUILayout.TextField(question);
        GUI.enabled = !service.IsBusy && !batchRunner.IsRunning && service.IsReady
            && !string.IsNullOrWhiteSpace(question);
        if (GUILayout.Button("Ask NPC")) StartCoroutine(SendManualQuestion());
        GUI.enabled = true;

        GUILayout.Space(8);
        GUILayout.Label("Automated comparison", GUI.skin.box);
        bool catalogEditable = !batchRunner.IsRunning;
        GUI.enabled = catalogEditable;
        GUILayout.BeginHorizontal();
        GUILayout.Label("Repeats", GUILayout.Width(60));
        repeatInput = GUILayout.TextField(repeatInput, GUILayout.Width(70));
        if (int.TryParse(repeatInput, out int enteredRepeats) && enteredRepeats > 0)
            batchRepeats = enteredRepeats;
        includeCpuInBatch = GUILayout.Toggle(includeCpuInBatch,
            "Include CPU after Vulkan", GUILayout.Width(190));
        if (GUILayout.Button("Reset to Prefab", GUILayout.Width(130))) ResetRuntimeCatalog();
        if (GUILayout.Button("Export JSON", GUILayout.Width(110))) ExportRuntimeCatalog();
        GUILayout.EndHorizontal();

        int caseCount = runtimeCatalog?.cases?.Length ?? 0;
        int modelCount = settings.Models?.Length ?? 0;
        int backendCount = NpcDialogueBatchRunner.BuildBackendSequence(includeCpuInBatch).Length;
        int total = NpcDialogueBatchRunner.CalculateTotalRequests(
            caseCount, batchRepeats, modelCount, includeCpuInBatch);
        GUILayout.Label(caseCount + " cases × " + batchRepeats + " repeats × "
            + modelCount + " models × " + backendCount + " backend(s) = "
            + total + " responses");
        if (batchRepeats > 10)
            GUILayout.Label("Note: values above 10 increase runtime and manual review work.");
        if (!string.IsNullOrEmpty(catalogStatus)) GUILayout.Label(catalogStatus);

        GUILayout.Label("Temporary test questions (metadata is read-only here):");
        catalogScroll = GUILayout.BeginScrollView(catalogScroll, GUILayout.Height(250));
        if (runtimeCatalog?.cases != null)
            foreach (var test in runtimeCatalog.cases)
            {
                GUILayout.Label(test.id + " | " + test.npcId + " | "
                    + test.expectedState + " | " + test.expectation);
                test.question = GUILayout.TextField(test.question ?? "");
            }
        GUILayout.EndScrollView();
        GUI.enabled = true;

        if (!batchRunner.IsRunning)
        {
            GUI.enabled = !service.IsBusy && batchRepeats > 0 && caseCount > 0;
            string button = includeCpuInBatch
                ? "Run GPU + CPU Test Suite" : "Run GPU Test Suite";
            if (GUILayout.Button(button))
                batchRunner.StartSuite(runtimeCatalog, batchRepeats,
                    includeCpu: includeCpuInBatch);
        }
        else if (GUILayout.Button("Stop after current request")) batchRunner.StopAfterCurrent();
        GUI.enabled = true;
        GUILayout.Label(batchRunner.Status + (batchRunner.TotalRequests > 0
            ? " [" + batchRunner.CompletedRequests + "/" + batchRunner.TotalRequests + "]" : ""));

        GUILayout.Space(8);
        GUILayout.Label(service.Status);
        if (!string.IsNullOrEmpty(reply)) GUILayout.TextArea(reply, GUILayout.Height(85));
        if (!string.IsNullOrEmpty(measurements)) GUILayout.Label(measurements);
        GUILayout.EndArea();
    }

    private void ResetRuntimeCatalog()
    {
        runtimeCatalog = testCatalog != null ? testCatalog.CreateSnapshot() : null;
        catalogStatus = runtimeCatalog == null
            ? "No test catalog asset is assigned." : "Questions reset to the prefab catalog.";
    }

    private void ExportRuntimeCatalog()
    {
        if (runtimeCatalog == null)
        {
            catalogStatus = "No runtime catalog is available to export.";
            return;
        }
        try
        {
            string safeId = string.IsNullOrWhiteSpace(runtimeCatalog.catalogId)
                ? "npc_test_catalog" : runtimeCatalog.catalogId;
            string filename = safeId + "_" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ") + ".json";
            string path = Path.Combine(Application.persistentDataPath, filename);
            File.WriteAllText(path, runtimeCatalog.ToJson(true), new UTF8Encoding(true));
            catalogStatus = "Exported: " + path;
            Debug.Log("[NPC Pilot] Test catalog exported: " + path);
        }
        catch (Exception ex)
        {
            catalogStatus = "Export failed: " + ex.Message;
        }
    }

    private IEnumerator SendManualQuestion()
    {
        if (!service.TryGetKnowledge(out var knowledge, out var problem))
        {
            reply = problem;
            yield break;
        }
        selectedNpc = Mathf.Clamp(selectedNpc, 0, knowledge.npcs.Length - 1);
        var request = new NpcDialogueRequest {
            RunId = "manual",
            Phase = "manual",
            NpcId = knowledge.npcs[selectedNpc].id,
            Question = question,
            DeliveryArrived = service.DeliveryArrived,
            ExpectedState = NpcDialogueProtocol.ExpectedState(service.DeliveryArrived)
        };
        NpcDialogueResult result = null;
        yield return service.Send(request, value => result = value);
        if (result == null) yield break;
        reply = result.IsUsable ? result.Validation.Dialogue : result.Validation.Error;
        measurements = "First text: "
            + (result.FirstTextMilliseconds.HasValue
                ? result.FirstTextMilliseconds.Value.ToString("F0") + " ms" : "not captured")
            + " | Validated response: " + result.ValidatedResponseMilliseconds.ToString("F0")
            + " ms | Output tokens: " + result.OutputTokens
            + " | JSON/fields/state field: " + result.Validation.SyntaxValid + "/"
            + result.Validation.StructureValid + "/" + result.Validation.StateValid;
    }

    private static bool HasCommandLineArgument(string name)
    {
        foreach (string argument in Environment.GetCommandLineArgs())
            if (string.Equals(argument, name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string ReadStringArgument(string name)
    {
        string[] arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < arguments.Length; i++)
            if (string.Equals(arguments[i], name, StringComparison.OrdinalIgnoreCase))
                return arguments[i + 1];
        return null;
    }

    private static int ReadIntArgument(string name, int fallback)
    {
        return int.TryParse(ReadStringArgument(name), out int value) && value > 0
            ? value : fallback;
    }
}

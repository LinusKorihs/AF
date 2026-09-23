using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

// Authored prefab entry point for the playable and research dialogue modes.
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
    private TextAsset testCatalog;
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
    private int batchRepeats = 5;

    private LocalNpcDialogueService service;
    private NpcDialoguePlayerController playerController;
    private NpcDialogueBatchRunner batchRunner;
    private int selectedNpc;
    private string reply = "";
    private string measurements = "";

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
        batchRunner.Configure(service, testCatalog, batchRepeats);
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
            batchRunner.StartSuite(repeats, output, true, caseId);
        }
        else if (loadModelOnSceneStart && settings != null)
            StartCoroutine(service.ActivateModel(startupModel));
    }

    private void OnGUI()
    {
        if (mode != LocalNpcUiMode.Research || settings == null || service == null) return;
        GUILayout.BeginArea(new Rect(12, 12, 650, 620), GUI.skin.box);
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
        GUILayout.Label("Automated comparison: 12 cases × " + batchRepeats
            + " repeats × 2 models = " + (12 * batchRepeats * 2) + " responses");
        if (!batchRunner.IsRunning)
        {
            GUI.enabled = !service.IsBusy;
            if (GUILayout.Button("Run Test Suite (Vulkan)"))
                batchRunner.StartSuite(batchRepeats);
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
            + " | JSON/fields/state: " + result.Validation.SyntaxValid + "/"
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

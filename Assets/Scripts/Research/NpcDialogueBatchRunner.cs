using System;
using System.Collections;
using UnityEngine;

public sealed class NpcDialogueBatchRunner : MonoBehaviour
{
    private LocalNpcDialogueService service;
    private TextAsset catalogAsset;
    private int defaultRepeats = 5;
    private bool stopRequested;

    public bool IsRunning { get; private set; }
    public string Status { get; private set; } = "No batch run active.";
    public int CompletedRequests { get; private set; }
    public int TotalRequests { get; private set; }

    public void Configure(LocalNpcDialogueService configuredService,
        TextAsset configuredCatalog, int repeats)
    {
        service = configuredService;
        catalogAsset = configuredCatalog;
        defaultRepeats = Mathf.Max(1, repeats);
    }

    public void StartSuite(int repeats = 0, string outputPath = null,
        bool quitWhenFinished = false, string caseId = null)
    {
        if (IsRunning) return;
        StartCoroutine(RunSuite(repeats > 0 ? repeats : defaultRepeats,
            outputPath, quitWhenFinished, caseId));
    }

    public void StopAfterCurrent()
    {
        if (!IsRunning) return;
        stopRequested = true;
        Status = "Stopping after the current request...";
    }

    private IEnumerator RunSuite(int repeats, string outputPath, bool quitWhenFinished,
        string caseId)
    {
        IsRunning = true;
        stopRequested = false;
        CompletedRequests = 0;
        TotalRequests = 0;
        int exitCode = 0;

        if (service == null)
        {
            Status = "Dialogue service is missing.";
            exitCode = 1;
            Finish(quitWhenFinished, exitCode);
            yield break;
        }
        if (!service.TryGetKnowledge(out var knowledge, out var problem))
        {
            Status = problem;
            exitCode = 1;
            Finish(quitWhenFinished, exitCode);
            yield break;
        }
        if (!NpcDialogueTestCatalog.TryLoad(catalogAsset, knowledge,
                out var catalog, out var catalogProblem))
        {
            Status = catalogProblem;
            exitCode = 1;
            Finish(quitWhenFinished, exitCode);
            yield break;
        }
        NpcDialogueTestCase[] selectedCases = catalog.cases;
        if (!string.IsNullOrWhiteSpace(caseId))
        {
            NpcDialogueTestCase selected = Array.Find(catalog.cases,
                test => string.Equals(test.id, caseId, StringComparison.Ordinal));
            if (selected == null)
            {
                Status = "Unknown batch case: " + caseId;
                exitCode = 1;
                Finish(quitWhenFinished, exitCode);
                yield break;
            }
            selectedCases = new[] { selected };
        }
        if (!service.GpuAvailable || !service.SetBackend(LocalNpcBackend.VulkanGpu))
        {
            Status = "Batch run requires the Vulkan backend; no usable Vulkan GPU was detected.";
            exitCode = 1;
            Finish(quitWhenFinished, exitCode);
            yield break;
        }

        repeats = Mathf.Max(1, repeats);
        int modelCount = service.Settings.Models?.Length ?? 0;
        TotalRequests = modelCount * selectedCases.Length * repeats;
        string runId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ") + "_" + catalog.catalogId;

        for (int modelIndex = 0; modelIndex < modelCount && !stopRequested; modelIndex++)
        {
            bool loaded = false;
            Status = "Loading model " + (modelIndex + 1) + "/" + modelCount + "...";
            yield return service.ActivateModel(modelIndex, success => loaded = success);
            if (!loaded)
            {
                Status = "Batch stopped: " + service.Status;
                exitCode = 1;
                break;
            }

            for (int repeat = 1; repeat <= repeats && !stopRequested; repeat++)
            {
                foreach (var test in selectedCases)
                {
                    if (stopRequested) break;
                    service.SetDeliveryActive(test.deliveryArrived);
                    Status = "Running " + (CompletedRequests + 1) + "/" + TotalRequests
                        + ": " + test.id;
                    var request = new NpcDialogueRequest {
                        RunId = runId,
                        Phase = "measured",
                        CaseId = test.id,
                        Repeat = repeat,
                        ModelOrder = modelIndex + 1,
                        NpcId = test.npcId,
                        Question = test.question,
                        DeliveryArrived = test.deliveryArrived,
                        ExpectedState = test.expectedState,
                        OutputPath = outputPath
                    };
                    yield return service.Send(request, _ => { });
                    CompletedRequests++;
                }
            }
        }

        if (stopRequested) Status = "Batch stopped after " + CompletedRequests + " requests.";
        else if (exitCode == 0) Status = "Batch complete: " + CompletedRequests + " measured responses.";
        Finish(quitWhenFinished, exitCode);
    }

    private void Finish(bool quitWhenFinished, int exitCode)
    {
        IsRunning = false;
        Debug.Log("[NPC Pilot] " + Status);
        if (quitWhenFinished) Application.Quit(exitCode);
    }
}

using System;
using System.Collections;
using UnityEngine;

public sealed class NpcDialogueBatchRunner : MonoBehaviour
{
    private LocalNpcDialogueService service;
    private NpcDialogueSetupCsvLogger setupCsv;
    private int defaultRepeats = 10;
    private bool defaultIncludeCpu = true;
    private bool stopRequested;

    public bool IsRunning { get; private set; }
    public string Status { get; private set; } = "No batch run active.";
    public int CompletedRequests { get; private set; }
    public int TotalRequests { get; private set; }

    public void Configure(LocalNpcDialogueService configuredService,
        int repeats, bool includeCpu)
    {
        service = configuredService;
        defaultRepeats = Mathf.Max(1, repeats);
        defaultIncludeCpu = includeCpu;
        setupCsv = service?.Settings != null
            ? new NpcDialogueSetupCsvLogger(service.Settings) : null;
    }

    public void StartSuite(NpcDialogueTestCatalog catalog, int repeats = 0,
        string outputPath = null, bool quitWhenFinished = false,
        string caseId = null, bool? includeCpu = null)
    {
        if (IsRunning) return;
        NpcDialogueTestCatalog snapshot = catalog?.Clone();
        StartCoroutine(RunSuite(snapshot,
            repeats > 0 ? repeats : defaultRepeats, outputPath,
            quitWhenFinished, caseId, includeCpu ?? defaultIncludeCpu));
    }

    public void StopAfterCurrent()
    {
        if (!IsRunning) return;
        stopRequested = true;
        Status = "Stopping after the current request...";
    }

    public static LocalNpcBackend[] BuildBackendSequence(bool includeCpu)
    {
        return includeCpu
            ? new[] { LocalNpcBackend.VulkanGpu, LocalNpcBackend.Cpu }
            : new[] { LocalNpcBackend.VulkanGpu };
    }

    public static int ComputeSamplingSeed(int seedBase, int repeat, int caseIndex)
    {
        return seedBase + Mathf.Max(1, repeat) * 1000 + Mathf.Max(0, caseIndex);
    }

    public static int CalculateTotalRequests(int caseCount, int repeats,
        int modelCount, bool includeCpu)
    {
        return Mathf.Max(0, caseCount) * Mathf.Max(1, repeats)
            * Mathf.Max(0, modelCount) * BuildBackendSequence(includeCpu).Length;
    }

    private IEnumerator RunSuite(NpcDialogueTestCatalog catalog, int repeats,
        string outputPath, bool quitWhenFinished, string caseId, bool includeCpu)
    {
        IsRunning = true;
        stopRequested = false;
        CompletedRequests = 0;
        TotalRequests = 0;
        int exitCode = 0;
        int setupFailures = 0;

        if (service == null)
        {
            Status = "Dialogue service is missing.";
            Finish(quitWhenFinished, 1);
            yield break;
        }
        if (!service.TryGetKnowledge(out var knowledge, out var problem))
        {
            Status = problem;
            Finish(quitWhenFinished, 1);
            yield break;
        }
        if (!NpcDialogueTestCatalog.TryValidate(catalog, knowledge, out var catalogProblem))
        {
            Status = catalogProblem;
            Finish(quitWhenFinished, 1);
            yield break;
        }
        if (!service.GpuAvailable)
        {
            Status = "Batch run requires the Vulkan backend; no usable Vulkan GPU was detected.";
            Finish(quitWhenFinished, 1);
            yield break;
        }

        string catalogHash = catalog.Hash;
        NpcDialogueTestCase[] selectedCases = catalog.cases;
        if (!string.IsNullOrWhiteSpace(caseId))
        {
            NpcDialogueTestCase selected = Array.Find(catalog.cases,
                test => string.Equals(test.id, caseId, StringComparison.Ordinal));
            if (selected == null)
            {
                Status = "Unknown batch case: " + caseId;
                Finish(quitWhenFinished, 1);
                yield break;
            }
            selectedCases = new[] { selected };
        }

        repeats = Mathf.Max(1, repeats);
        int modelCount = service.Settings.Models?.Length ?? 0;
        LocalNpcBackend[] backends = BuildBackendSequence(includeCpu);
        TotalRequests = CalculateTotalRequests(
            selectedCases.Length, repeats, modelCount, includeCpu);
        string runId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ") + "_" + catalog.catalogId;

        for (int backendIndex = 0;
             backendIndex < backends.Length && !stopRequested; backendIndex++)
        {
            LocalNpcBackend backend = backends[backendIndex];
            if (!service.SetBackend(backend))
            {
                Status = "Could not select backend " + backend + ".";
                exitCode = 1;
                setupFailures++;
                continue;
            }

            for (int modelIndex = 0; modelIndex < modelCount && !stopRequested; modelIndex++)
            {
                bool loaded = false;
                Status = "Loading " + backend + " model " + (modelIndex + 1)
                    + "/" + modelCount + "...";
                yield return service.ActivateModel(modelIndex, success => loaded = success);
                WriteSetupResult(runId, outputPath, backendIndex, modelIndex);
                if (!loaded)
                {
                    exitCode = 1;
                    setupFailures++;
                    continue;
                }

                for (int repeat = 1; repeat <= repeats && !stopRequested; repeat++)
                {
                    foreach (var test in selectedCases)
                    {
                        if (stopRequested) break;
                        service.SetDeliveryActive(test.deliveryArrived);
                        Status = "Running " + (CompletedRequests + 1) + "/"
                            + TotalRequests + ": " + backend + " / " + test.id;
                        int catalogCaseIndex = Array.IndexOf(catalog.cases, test);
                        var request = new NpcDialogueRequest {
                            RunId = runId,
                            Phase = "measured",
                            CaseId = test.id,
                            Repeat = repeat,
                            BackendOrder = backendIndex + 1,
                            ModelOrder = modelIndex + 1,
                            CatalogId = catalog.catalogId,
                            CatalogHash = catalogHash,
                            Expectation = test.expectation,
                            ReviewNotes = test.reviewNotes,
                            SamplingSeed = ComputeSamplingSeed(
                                service.Settings.BatchSeedBase, repeat, catalogCaseIndex),
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
        }

        if (stopRequested)
            Status = "Batch stopped after " + CompletedRequests + "/" + TotalRequests
                + " requests.";
        else if (exitCode == 0)
            Status = "Batch complete: " + CompletedRequests + " measured responses.";
        else
            Status = "Batch finished with " + setupFailures + " setup failure(s): "
                + CompletedRequests + "/" + TotalRequests + " responses.";
        Finish(quitWhenFinished, exitCode);
    }

    private void WriteSetupResult(string runId, string outputPath,
        int backendIndex, int modelIndex)
    {
        NpcModelSetupResult setup = service.LastSetupResult ?? new NpcModelSetupResult {
            Backend = service.Backend.ToString(),
            Model = service.Settings.GetModel(modelIndex)?.id ?? "",
            Success = false,
            Error = service.Status
        };
        setup.RunId = runId;
        setup.BackendOrder = backendIndex + 1;
        setup.ModelOrder = modelIndex + 1;
        setup.OutputPath = outputPath;
        setupCsv?.Write(setup);
    }

    private void Finish(bool quitWhenFinished, int exitCode)
    {
        IsRunning = false;
        Debug.Log("[NPC Pilot] " + Status);
        if (quitWhenFinished) Application.Quit(exitCode);
    }
}

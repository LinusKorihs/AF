# Local NPC dialogue

The prototype in `Assets/Scenes/NPCs-With-SLMs.unity` uses the bundled Windows x64 CPU or Vulkan build of llama.cpp. The dialogue prefab starts and stops `llama-server` itself. No separate inference application is required.

## Model files

Place these GGUF files in `LocalModels` next to the Unity `Assets` folder. For a Windows game build, place `LocalModels` next to the executable. Model weights are excluded from Git.

| Model | File | Download |
| --- | --- | --- |
| Qwen 3 4B | `Qwen3-4B-Instruct-2507-Q4_K_M.gguf` | https://huggingface.co/lmstudio-community/Qwen3-4B-Instruct-2507-GGUF/blob/main/Qwen3-4B-Instruct-2507-Q4_K_M.gguf |
| Gemma 3 4B | `gemma-3-4b-it-Q4_K_M.gguf` | https://huggingface.co/ggml-org/gemma-3-4b-it-GGUF/blob/main/gemma-3-4b-it-Q4_K_M.gguf |

SHA-256 of the files used for the earlier pilot: Qwen `8cdb57cbb880d313736a9bc4e3d3d2485f145b5e19cf33783746e753e82641fc`, Gemma `882e8d2db44dc554fb0ea5077cb7e4bc49e7342a1f0da57901c0802ea21a0863`.

## Player Mode

1. Open `Assets/Scenes/NPCs-With-SLMs.unity` and enter Play Mode.
2. The prefab defaults to `Player` mode and loads Qwen. Vulkan is selected when available; Player Mode falls back to CPU when it is not.
3. Press `Q` for the Farmer or `E` for the Knight. Enter an English question and press `Enter`.
4. The selected NPC shows a `...` bubble while the complete JSON response is received and checked. `Enter` advances through at most two dialogue pages and closes the last page. `Escape` closes question input or a completed response.
5. Press `R` while no dialogue is open to make the food delivery arrive. This Player Mode action is intentionally one-way. Unity remains authoritative; model output never changes the horse or supply state.

The keys are serialized on `Assets/Prefab/Scripts/LocalNpcDialoguePilot.prefab` and can be changed in the Inspector. A technically invalid answer uses an authored in-character fallback (`Sorry, I cannot answer that right now.` for the Farmer and `I cannot give you a reliable answer right now.` for the Knight). This fallback is presentation only: the original failure and raw response remain logged and do not count as a valid model answer. There is no automatic retry.

## Research Mode

Set the prefab's `Mode` to `Research` to show the technical panel. It retains manual CPU/Vulkan and Qwen/Gemma selection, manual NPC questions, explicit world-state controls, metrics, and validation flags. The panel also edits a temporary copy of all test questions and the repeat count. `Reset to Prefab` restores the versioned catalog and `Export JSON` writes the current runtime copy to `Application.persistentDataPath`. These controls are locked while a suite is running. `Stop after current request` preserves every row already written.

The versioned Inspector catalog is `Assets/Scripts/ScriptableObjects/NpcDialogueTestCatalog.asset`; `npc_test_cases.json` is its human-readable JSON baseline. It contains twenty Farmer/Knight cases covering supply, harvest, exclusive role knowledge, false premises, missing personal-biography knowledge, delivery ownership, absent visual perception, greetings, insults, rumors, and unclear input. The default protocol runs ten serial repetitions with a fixed seed schedule. It first tests Qwen and Gemma on Vulkan and then both models on CPU: 800 measured responses. Each model/backend combination is loaded and warmed once before its measured cases.

For a built Windows player, the same C# pipeline can run without the visible dialogue UI:

```powershell
AF.exe -batchmode -nographics --npc-batch --repeat 10 --output npc_pilot_v9_final.csv
```

Use `--repeat 1 --case CASE_ID` for a one-case, two-model, two-backend smoke test. Pass `--gpu-only` to omit the CPU phase. Without `--case`, every catalog case is executed. Relative output paths are resolved below `Application.persistentDataPath`; an absolute path can also be supplied. Request failures are logged without retrying. Setup failures are written to the setup CSV, the remaining startable combinations continue, and the process exits with code 1 when the resulting matrix is incomplete.

The runtime uses `127.0.0.1:8080`. If the port is already occupied, the panel shows an error. The bundled runtimes are CPU and Vulkan builds from [llama.cpp b11074](https://github.com/ggml-org/llama.cpp/releases/tag/b11074), together with their DLLs and license files under `Assets/StreamingAssets/LocalInference`.

## Editable configuration and measurements

- `Assets/Scripts/ScriptableObjects/LocalNpcDialogueSettings.asset` configures models, runtime, generation parameters, the base seed, warmup, delivery fact ID, and CSV protocol ID. It is referenced by the dialogue prefab.
- `Assets/Scripts/Research/Data/npc_knowledge.json` contains the English world facts, exclusive NPC knowledge, eight ordered dialogue rules, and authored fallbacks. The request places role, knowledge, authoritative state, dialogue rules, and output format in separate sections, followed by four neutral examples whose wording is not copied from the benchmark. A short reminder immediately before the delimited current player message repeats only the highest-priority relevance, unknown-information, clarification, and perception constraints.
- `Assets/Scripts/ScriptableObjects/NpcDialogueTestCatalog.asset` is the permanent Inspector-editable catalog. The prefab Inspector displays its contents inline.
- Unity writes new answers to `Application.persistentDataPath/npc_pilot_v9.csv` with protocol `scene-json-v9`. Each row records catalog/hash, case metadata, repeat, backend/model order, reproducible sampling seed, generation settings, authoritative state, client timing, token output, strict validation, fallback status, shown text, raw output, errors, and five pending manual review fields.
- `state_valid` checks only whether the structured `supply_state` field matches Unity. `dialogue_state_review` separately records whether the visible dialogue semantically agrees with that state; it is intentionally manual, starts as `pending`, and can be reviewed as `pass`, `fail`, or `unclear`.
- Model startup and warmup are written separately to `npc_pilot_v9_setup.csv`. When `--output` supplies a custom response filename, `_setup.csv` is appended to its stem.
- Earlier v1-v8 results use different prompts, catalogs, or validation and must be analysed separately. In particular, `npc_pilot_v6.csv`, `npc_pilot_v7.csv`, and `npc_pilot_v8.csv` remain preserved prompt-refinement pilots and are not merged with v9. Startup and warmup remain excluded from dialogue latency.
- Both models receive the same OpenAI-compatible message list. Qwen uses its native system-message template. The bundled Gemma GGUF template merges a leading system message into the first user turn, matching Gemma's documented `user`/`model` prompt structure. The active model template can be inspected through llama.cpp's `/props` or `/apply-template` endpoints. See [Gemma prompt formatting](https://ai.google.dev/gemma/docs/core/prompt-structure) and the [llama.cpp server documentation](https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md).

## Runtime flow

`LocalNpcDialoguePilot` selects Player or Research Mode. Both modes and the batch runner call `LocalNpcDialogueService`, which builds the prompt from `npc_knowledge.json` and the active `NpcWorldFactSource`, streams the local llama.cpp response, validates it with the strict v9 protocol, and finally appends the result to CSV. A valid generated dialogue or an explicitly marked authored fallback reaches the Player Mode text box; only the former counts as usable model output. Semantic dialogue quality remains a separate review and never silently becomes a technical fallback.

The previous single-question Play Mode measurements verify operation on the development laptop only; they are not a general performance benchmark.

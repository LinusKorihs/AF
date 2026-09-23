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

The keys are serialized on `Assets/Prefab/Scripts/LocalNpcDialoguePilot.prefab` and can be changed in the Inspector. Invalid JSON, unexpected fields, missing fields, invalid state values, and values that conflict with Unity are logged but shown to the player only as `The NPC could not answer reliably.` There is no automatic retry.

## Research Mode

Set the prefab's `Mode` to `Research` to show the technical panel. It retains manual CPU/Vulkan and Qwen/Gemma selection, manual NPC questions, explicit world-state controls, metrics, and validation flags. `Run Test Suite (Vulkan)` starts the fixed automated comparison. `Stop after current request` preserves every row already written.

The versioned catalog is `Assets/Scripts/Research/npc_test_cases.json`. It contains twelve Farmer/Knight cases for supply, harvest, knowledge boundaries, false premises, an unknown quantity, and an off-topic question. The default final protocol runs five serial repetitions per case for Qwen and then Gemma: 120 measured responses. Each model is loaded and warmed once before its measured cases. Vulkan is mandatory for this comparison; the runner does not silently mix CPU results.

For a built Windows player, the same C# pipeline can run without the visible dialogue UI:

```powershell
AF.exe -batchmode -nographics --npc-batch --repeat 5 --output npc_pilot_v5.csv
```

Use `--repeat 1 --case CASE_ID` for a one-case, two-model smoke test. Without `--case`, every catalog case is executed. Relative output paths are resolved below `Application.persistentDataPath`; an absolute path can also be supplied. The process exits with code 1 when the Vulkan environment or model setup cannot start.

The runtime uses `127.0.0.1:8080`. If the port is already occupied, the panel shows an error. The bundled runtimes are CPU and Vulkan builds from [llama.cpp b11074](https://github.com/ggml-org/llama.cpp/releases/tag/b11074), together with their DLLs and license files under `Assets/StreamingAssets/LocalInference`.

## Editable configuration and measurements

- `Assets/Scripts/Research/LocalNpcDialogueSettings.asset` configures models, runtime, generation parameters, warmup, delivery fact ID, and CSV protocol ID. It is referenced by the dialogue prefab.
- `Assets/Scripts/Research/npc_knowledge.json` contains the English world facts, NPC roles and knowledge, unknown-answer rule, and response instruction.
- Unity writes new runs to `Application.persistentDataPath/npc_pilot_v5.csv` with protocol `scene-json-v5`. Each row records run/case/repeat/model order, backend and generation settings, the authoritative expected state, client timing, token output, strict syntax/structure/state validation, raw output, error category, and pending manual facts/role review.
- Earlier v1-v4 results use different prompts or validation and must be analysed separately. Startup and warmup time are excluded from the recorded dialogue latency.

## Runtime flow

`LocalNpcDialoguePilot` selects Player or Research Mode. Both modes and the batch runner call `LocalNpcDialogueService`, which builds the prompt from `npc_knowledge.json` and the active `NpcWorldFactSource`, streams the local llama.cpp response, validates it with the strict v5 protocol, and finally appends the result to CSV. Only the validated dialogue string reaches the Player Mode text box.

The previous single-question Play Mode measurements verify operation on the development laptop only; they are not a general performance benchmark.

# Local NPC dialogue

The prototype in `Assets/Scenes/NPCs-With-SLMs.unity` uses the bundled Windows x64 CPU or Vulkan build of llama.cpp. The dialogue prefab starts and stops `llama-server` itself. No separate inference application is required.

## Model files

Place these GGUF files in `LocalModels` next to the Unity `Assets` folder. For a Windows game build, place `LocalModels` next to the executable. Model weights are excluded from Git.

| Model | File | Download |
| --- | --- | --- |
| Qwen 3 4B | `Qwen3-4B-Instruct-2507-Q4_K_M.gguf` | https://huggingface.co/lmstudio-community/Qwen3-4B-Instruct-2507-GGUF/blob/main/Qwen3-4B-Instruct-2507-Q4_K_M.gguf |
| Gemma 3 4B | `gemma-3-4b-it-Q4_K_M.gguf` | https://huggingface.co/ggml-org/gemma-3-4b-it-GGUF/blob/main/gemma-3-4b-it-Q4_K_M.gguf |

SHA-256 of the files used for the earlier pilot: Qwen `8cdb57cbb880d313736a9bc4e3d3d2485f145b5e19cf33783746e753e82641fc`, Gemma `882e8d2db44dc554fb0ea5077cb7e4bc49e7342a1f0da57901c0802ea21a0863`.

## Unity test

1. Open `Assets/Scenes/NPCs-With-SLMs.unity` and enter Play Mode.
2. The GPU option appears if the bundled Vulkan runtime reports a Vulkan device. Otherwise CPU is selected. A GPU with limited memory may still fail to load the model; select CPU in that case.
3. In `Assets/Prefab/Scripts/LocalNpcDialoguePilot.prefab`, `Load Model On Scene Start` is enabled by default and `Startup Model` is Qwen. The selected model loads and warms up in a coroutine while the scene remains interactive. You can disable this option and load a model manually. Model startup time is displayed separately from the latency of a player question.
4. Select Farmer or Knight, enter an English question, and choose `Ask NPC`. The authored horse object can be switched between inactive and active to change the world facts. Selecting another model stops the previous runtime and loads the new one.

The runtime uses `127.0.0.1:8080`. If the port is already occupied, the panel shows an error. The bundled runtimes are CPU and Vulkan builds from [llama.cpp b11074](https://github.com/ggml-org/llama.cpp/releases/tag/b11074), together with their DLLs and license files under `Assets/StreamingAssets/LocalInference`.

## Editable configuration and measurements

- `Assets/Scripts/Research/LocalNpcDialogueSettings.asset` configures models, runtime, generation parameters, warmup, delivery fact ID, and CSV protocol ID. It is referenced by the dialogue prefab.
- `Assets/Scripts/Research/npc_knowledge.json` contains the English world facts, NPC roles and knowledge, unknown-answer rule, and response instruction.
- Unity writes new runs to `Application.persistentDataPath/npc_pilot_v4.csv` with protocol `scene-json-v4`. Earlier German pilot results use a different protocol and must be analysed separately. Startup and warmup time are excluded from the recorded dialogue latency.

The previous single-question Play Mode measurements verify operation on the development laptop only; they are not a general performance benchmark.

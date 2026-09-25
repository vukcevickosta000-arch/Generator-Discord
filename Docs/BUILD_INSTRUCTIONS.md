# Build Instructions

## Prerequisites

| Tool | Version | Used for |
|---|---|---|
| .NET SDK | 8.0 | Backend, game server, tests, tools, compile checks |
| Unity | **6000.0.40f1** (Unity 6) with *Windows Build Support (Mono)* | Client |
| Python | 3.10+ with `numpy`, `pillow`, `scipy` | Map and art generators (`pip install numpy pillow scipy`) |
| Blender | 4.x/5.x or the `bpy` Python module | Model pipeline (T-001) |
| PostgreSQL | 15+ (production only) | Database (development uses SQLite automatically) |

## Server components

```bash
dotnet build Server/Bloodfall.sln                       # everything
dotnet test Server/tests/Bloodfall.Tests                # unit/simulation tests
Tools/dev/run-local.sh                                  # backend :5080 + 2 game servers (UDP 27015-27016)
Tools/dev/run-e2e.sh                                    # end-to-end test on a fresh database
Tools/dev/stop-local.sh

# Release artefacts
dotnet publish Server/src/Bloodfall.Backend -c Release -o out/backend
dotnet publish Server/src/Bloodfall.GameServer -c Release -r linux-x64 --self-contained -o out/gameserver
dotnet publish Server/src/Bloodfall.GameServer -c Release -r win-x64 --self-contained -o out/gameserver-win
```

On Windows use `Tools/dev/run-local.ps1`.

## Unity client

1. Install Unity **6000.0.40f1** from Unity Hub and add the project folder `Client/`.
2. Open the project. Packages resolve from `Packages/manifest.json`:
   - URP 17.0.3 and Input System 1.11.2
   - the local package `com.bloodfall.shared` → `../../Shared`
3. `ProjectSetup` runs automatically when no render pipeline is set. You can also run it from
   **Bloodfall ▸ Setup Project**. It:
   - creates `Assets/Settings/BloodfallURP.asset` and its renderer, and assigns them
   - sets linear colour space, player settings and input handling (both backends)
   - creates `Assets/Scenes/Boot.unity` and puts it in the build settings
4. If Unity asks to enable the new Input System backend, choose **Yes**. The client supports both backends.
5. Press **Play** in any scene. The client boots itself from code (`Bootstrap`).
   - Backend URL: `Assets/Resources/Config/client_config.json` (`backendUrl`), overridable with
     `-bloodfall-backend <url>` or the `BLOODFALL_BACKEND` environment variable.
   - Without a backend, use **Play Offline** for practice vs bots.
6. Validation: **Bloodfall ▸ Validate Game Data** prints data errors and the art coverage report.

### Windows player build

- **Menu:** Bloodfall ▸ Build ▸ Windows Client (Development / Release) → `Builds/Windows/Bloodfall.exe`.
- **Command line:**
  ```
  Unity.exe -batchmode -quit -projectPath Client -bloodfallSetup ^
    -executeMethod Bloodfall.Client.Editor.BloodfallTools.BuildWindowsCli [-development]
  ```

## Regenerating content

```bash
python3 Tools/mapgen/generate_velmoragh.py     # map JSON + heightfield/splat/dressing + Docs/Images/velmoragh_layout.png
python3 Tools/art/generate_ui.py               # UI kit, cursors, crests, ranks, logo
python3 Tools/art/generate_backdrop.py         # menu backdrop layers
python3 Tools/art/generate_vfx.py              # particle / decal sprites
python3 Tools/art/generate_terrain.py          # terrain layer textures
python3 Tools/art/generate_icons.py            # ability / item / status icons + hero portraits (from game data)
python3 Tools/audio/generate_audio.py          # UI, SFX, ambience, music, announcer (needs soundfile; espeak-ng for the announcer)
python3 Tools/dev/gen_gamedata_index.py        # after adding/renaming game data files
python3 Tools/dev/gen_item_docs.py             # refresh the item table in Docs/ITEM_DATABASE.md
pip install bpy==5.0.1                         # once (Python 3.11), or run the script with `blender -b -P`
python3 Blender/scripts/build_models.py        # all 56 unit/structure models -> Client/Assets/Resources/Models/*.fbx
python3 Blender/scripts/build_models.py hero_vorak --preview   # one model + Cycles previews in Blender/previews/
```

**Warning:** regenerating the map changes the game data content hash. Clients and servers must ship the same data;
mismatches are rejected with an update message.

## Headless compile checks (no Unity needed)

```bash
dotnet build Tools/UnityCompileCheck            # client scripts vs UnityEngine 2021.3 reference assemblies
dotnet build Tools/UnityCompileCheck/Editor     # editor scripts vs Unity3D.SDK
```

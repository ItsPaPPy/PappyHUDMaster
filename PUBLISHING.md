# Publishing Pappy HUD Master

## Recommended public release

Version: `1.0.0`

Hexium package name: `PappyHUDMaster`

Install scope: **Client only**

Mod manager: **No specific manager required**

Recommended Hexium tags:

- User Interface
- Quality of Life
- Config
- Open Source
- Valheim 1.0

Short description:

> Move, scale, rotate, and adjust transparency for Valheim 1.0 HUD elements with an in-game visual editor.

## Before creating the release

1. Create the GitHub repository, recommended name: `PappyHUDMaster`.
2. Update `manifest.json`:
   - Set `website_url` to the final GitHub repository URL.
3. Replace the placeholder sentence in README.md under **Source and issues** with the actual GitHub URL if desired.
4. Add a 256x256 `icon.png`.
5. Run `build.bat`.
6. Test the generated `build\PappyHUDMaster.dll` in a clean Valheim/BepInEx profile.
7. Test:
   - F7 open/close
   - mouse input blocking
   - hotbar movement
   - inventory/crafting movement and clickability
   - build menu movement
   - minimap transparency
   - Enter-to-type chat movement
   - config persistence after restart

## Hexium ZIP

Create a ZIP with these files at the ROOT of the archive:

```text
PappyHUDMaster.dll
manifest.json
README.md
CHANGELOG.md
icon.png
```

Do not put those files inside another folder in the ZIP.

Do not package:

- `BepInEx.dll`
- `0Harmony.dll`
- `assembly_valheim.dll`
- Unity DLLs
- any other game's or dependency's DLLs

Hexium assumes BepInExPack Valheim, so `dependencies` can remain empty.

## GitHub repository

Recommended repository contents:

```text
PappyHUDMaster.cs
build.bat
manifest.json
README.md
CHANGELOG.md
LICENSE
.gitignore
icon.png
```

Do not commit Valheim, Unity, BepInEx, or Harmony DLLs.

## GitHub repository About text

> Visual HUD editor for Valheim 1.0 — move, scale, rotate, and adjust transparency for vanilla UI elements.

Recommended topics:

```text
valheim
valheim-mod
bepinex
harmony
unity
hud
ui
modding
```

## GitHub release

Tag:

```text
v1.0.0
```

Release title:

```text
Pappy HUD Master v1.0.0
```

Attach the same ready-to-install ZIP that you upload to Hexium.

Suggested release notes:

> Initial public release of Pappy HUD Master, a client-side visual HUD editor for Valheim 1.0.
>
> Move, scale, rotate, and adjust transparency for supported vanilla HUD elements directly in-game. Includes inventory, crafting, build menu, minimap, chat, boss health, status effects, and more.
>
> Tested with Valheim 1.0.7 and BepInExPack Valheim 5.4.2350.

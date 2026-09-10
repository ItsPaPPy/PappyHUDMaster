# Pappy HUD Master

A client-side visual HUD editor for **Valheim 1.0**. Move, scale, rotate, and adjust transparency for vanilla HUD elements in real time without changing gameplay mechanics.

Built and tested against **Valheim 1.0.7** with **BepInEx 5.4.23.5 / BepInExPack Valheim 5.4.2350**.

The mod does not require a specific mod manager.

## Features

- In-game visual editor opened with **F7**
- Drag supported HUD elements directly on screen
- Exact numeric X/Y positioning
- Scale and rotation controls
- Saved settings through BepInEx config
- Game mouse-look, camera zoom, and gameplay mouse actions are blocked while the editor is open
- Small **X** button to close the editor
- Optimized one-time UI discovery instead of continuous full-scene scanning
- Client-side only; no server installation required

### Transparency controls

Transparency is available for:

- Minimap terrain
- Player Inventory
- Inventory Info
- Crafting Window
- Build Menu

The minimap uses a dedicated compositor so the terrain can be faded while map markers and text remain readable.

## Supported UI elements

- Hotbar
- Stamina
- Adrenaline
- Eitr
- Health + Food
- Crosshair
- Status Effects
- Minimap
- Guardian Power
- Event Bar
- Action Progress
- Player Inventory
- Inventory Info
- Crafting Window
- Crafting Recipe List
- Crafting Item Details
- Build Menu
- Boss Health
- Center Message
- Top Left Message
- Chat Window (Enter)
- Chat Input Field
- World Chat Bubble
- NPC Dialog
- Large NPC Dialog

Some elements only become active when Valheim is displaying that part of the UI.

## Controls

| Control | Action |
|---|---|
| `F7` | Open / close the HUD editor |
| Mouse drag | Drag the selected HUD element using its **DRAG** handle |
| Arrow keys | Move selected element by 5 units |
| `Shift` + Arrow keys | Move selected element by 25 units |
| `+` / `-` | Increase / decrease scale |
| `Q` / `E` | Rotate selected element |
| `R` | Reset selected element |

You can also type exact values for X offset, Y offset, scale, rotation, and supported transparency settings.

## Installation

### Mod manager

Install Pappy HUD Master through your preferred Valheim-compatible mod manager.

### Manual installation

1. Install **BepInExPack Valheim**.
2. Download the Pappy HUD Master release ZIP.
3. Extract `PappyHUDMaster.dll` into:

```text
Valheim/BepInEx/plugins/PappyHUDMaster/
```

4. Start Valheim.
5. Press **F7** while in-game.

## Configuration

The configuration file is created automatically at:

```text
BepInEx/config/pappy.valheim.hudmaster.cfg
```

Changes made in the visual editor are saved to this file.

## Chat window

`Chat Window (Enter)` controls the normal chat interface that appears when you press **Enter** to type a message.

`World Chat Bubble`, `NPC Dialog`, and `Large NPC Dialog` are separate Valheim UI systems.

For UI objects that are dynamically spawned, use **REFRESH / FIND ACTIVE COPY** while the desired UI is visible.

## Compatibility

- Designed for **Valheim 1.0**
- Tested on **Valheim 1.0.7**
- Client-side only
- Does not require Jötunn
- Does not require Configuration Manager
- Uses the Harmony library included with BepInEx for editor input suppression
- Does not modify server-side gameplay

Mods that replace or heavily restructure Valheim's vanilla UI may require additional compatibility work.

## Screenshots

<!-- Add your screenshots here. GitHub-hosted images can also be referenced from the Hexium README. -->

## Source and issues

Source code and issue tracking are available on GitHub.

> Before publishing, replace the repository URL in `manifest.json` and this README with your final GitHub repository URL.

## Credits

Pappy HUD Master is an independent clean-room implementation inspired by the idea of allowing players to reposition Valheim's HUD through an in-game visual editor.

Thanks to the Valheim modding community, the BepInEx project, and Harmony.

## License

MIT License. See [LICENSE](LICENSE).

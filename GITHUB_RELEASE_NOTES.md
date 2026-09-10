# Pappy HUD Master v1.0.1

Bug-fix release.

## Fixed

Pappy HUD Master now detects when Valheim destroys and recreates the in-game HUD, such as after:

- a server disconnect/reconnect
- changing worlds
- returning to the main menu and joining again
- other scene/UI recreation

The mod clears only its cached UI references and performs one fresh UI discovery pass for the new HUD instance. It does **not** bring back the old periodic full-scene scan that caused gameplay hitches.

Existing saved positions, scale, rotation, and transparency settings are retained.

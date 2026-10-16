# WhiteKnuckle Re-Zero

A BepInEx plugin for White Knuckle that removes save-load transition delays and replaces the standard revive sequence with an instant revive flow.

## Features

- Skips fade-in and fade-out effects while loading a save.
- Removes the save-load delay coroutine.
- Restores the player immediately during the revive sequence.
- Clears the dead state and unlocks player movement.
- Updates the revive achievement and session statistic.
- Plays a custom revive sound after a successful save load or revive.
- Supports OGG, WAV, and MP3 audio files.

## Requirements

- White Knuckle
- BepInEx
- .NET Framework 4.8 for development

## Installation

1. Install BepInEx for White Knuckle.
2. Build or obtain `WhiteKnuckle_Re_Zero.dll`.
3. Copy the DLL to:

   ```text
   White Knuckle/BepInEx/plugins/
   ```

4. Optionally add a custom revive sound using one of the supported file names:

   ```text
   White Knuckle/BepInEx/plugins/revive.ogg
   White Knuckle/BepInEx/plugins/revive.wav
   White Knuckle/BepInEx/plugins/revive.mp3
   ```

   The mod also checks the following subdirectory:

   ```text
   White Knuckle/BepInEx/plugins/WhiteKnuckle_Re_Zero/
   ```

The search order is `revive.ogg`, `revive.wav`, then `revive.mp3`. The first matching file is loaded.

## Behavior

When a save is loaded, the plugin disables the UI fade effects and skips the loading delay. Once the world is ready, the transition state is cleared and the custom sound is played.

During a revive, the plugin stops the death-floor coroutine and active events, restores the player's movement state, clears the dead flags, resets the revive UI, updates the revive statistic, and plays the custom sound.

The revive sound has a 0.5-second cooldown to prevent duplicate playback.

## Development

The project targets .NET Framework 4.8 and references the White Knuckle and BepInEx assemblies from the local game installation. Update the assembly paths in `WhiteKnuckle_Re-Zero.csproj` if the game is installed in a different location.

## License

No license has been specified for this project.

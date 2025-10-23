# 0HarmonyUpdater

A mod for Warhammer 40K: Rogue Trader to auto-update Harmony. Useful in particular for macOS where the bundled version of Harmony (2.2.0 at the time or writing) does not work on ARM64 macOS.

## Installation
1. Navigate to the game's persistent data folder. On windows this is `%LocalAppData%Low\Owlcat Games\Warhammer 40000 Rogue Trader`. On macOS it should be `~/Library/Application Support/Owlcat Games/Warhammer 40000 Rogue Trader`
2. (Optional) If you want the mod to download new Harmony versions automatically, delete the `DisableWebUpdate.txt` file
3. Extract the zip into the `Modifications` folder
4. Edit `OwlcatModificationsManagerSettings.json` and add `"0HarmonyUpdater"` to the `"EnabledModifications"` array.<img width="1257" height="903" alt="image" src="https://github.com/user-attachments/assets/c7cda574-f5cd-4381-a934-f0e3271de670" />

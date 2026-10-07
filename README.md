# Eagler Java Launcher

A lightweight Windows launcher for **Minecraft: Java Edition**: Microsoft account sign-in, official game files from Mojang, optional Fabric performance mods (Sodium, Lithium, FerriteCore…), server list and resource pack management.

- Website: https://avenoxmc.github.io/avenox-launcher/
- Privacy policy: https://avenoxmc.github.io/avenox-launcher/privacy.html
- Download: [Releases](https://github.com/AvenoxMC/avenox-launcher/releases)

## Repository layout

| Path | Content |
|---|---|
| `index.html`, `privacy.html`, `style.css` | Website (GitHub Pages) |
| `launcher/src/` | Launcher source code (C#, .NET Framework 4.8, WinForms) |
| `launcher/mod/` | Small Fabric mod used by the optional WebSocket relay mode |
| `launcher/LISEZMOI.md` | User guide (French) |

## Build

On Windows 10/11, no SDK needed: run `launcher/compiler.bat`. It uses the C# compiler shipped with Windows and produces `EaglerJavaLauncher.exe`.
The relay mod is rebuilt with `launcher/mod/build-mod.bat` (needs JDK 21+).

## Microsoft sign-in

Uses the OAuth device code flow with the public client ID `1b5191b4-8d39-4ce8-b8c0-6bb11c044842` (personal Microsoft accounts). Tokens are stored on the user's PC, encrypted with Windows DPAPI, and only sent to Microsoft, Xbox Live and Minecraft services.

An optional username-only mode exists for offline-mode servers; it cannot join online-mode servers and does not use Minecraft services.

---

Not an official Minecraft product. Not approved by or associated with Mojang or Microsoft.

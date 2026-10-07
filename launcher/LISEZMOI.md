# Eagler Java Launcher

Launcher Windows (un seul `.exe`, aucune installation) qui lance le **vrai Minecraft Java Edition 26.2**, optimisé, et qui sait se connecter aux serveurs Eaglercraft.

## Utilisation
1. Double-clique sur `EaglerJavaLauncher.exe`.
2. Onglet **Jouer** : choisis un pseudo, puis clique sur **JOUER**. La première fois, il télécharge environ 800 Mo depuis Mojang, Fabric et Modrinth (2 minutes environ). Les lancements suivants démarrent tout de suite.
3. Onglet **Serveurs** : ajoute tes serveurs. Ils apparaissent dans le menu Multijoueur du jeu, et « Rejoindre » permet de se connecter directement au lancement.

## Compte : avec ou sans Microsoft
Onglet **Jouer** → **Connexion**, tu choisis :
- **Sans compte (pseudo, hors ligne)** : tu tapes juste un pseudo. Ça marche sur les serveurs Eaglercraft et les serveurs en mode hors ligne.
- **Avec un compte Microsoft** : clique sur **Se connecter…**. Le launcher affiche un code et ouvre `microsoft.com/link`, où tu te connectes avec le compte qui possède Minecraft. Tu peux alors rejoindre aussi les serveurs premium, avec ton vrai skin. La session est retenue (chiffrée pour ta session Windows) et renouvelée toute seule.

Le choix est retenu d'un lancement à l'autre. L'identifiant de l'application Microsoft (`1b5191b4-8d39-4ce8-b8c0-6bb11c044842`) est intégré au code (`src/MicrosoftAuth.cs`). Tant que Mojang n'a pas approuvé cette application pour l'API Minecraft, la connexion s'arrête à la dernière étape avec un message qui l'explique ; le mode sans compte marche dans tous les cas.

## Connexion aux serveurs (relais, comme la version navigateur)
Onglet **Serveurs** → case **« Passer par le relais WebSocket »** (cochée par défaut).
Toutes les connexions passent alors par `wss://eagler-minecraft-relay.u2471966200.workers.dev/minecraft?target=serveur`, exactement comme le mode **Relay** de la version navigateur. Ça concerne aussi les serveurs ajoutés directement dans le jeu et la Connexion directe. Ça marche même quand le réseau bloque les ports Minecraft (25565, 25570…).
- Un petit mod (`eagler-relay`, inclus dans le launcher) redirige les connexions du jeu vers le launcher. Le launcher lit l'adresse du serveur dans le premier paquet Minecraft, puis ouvre le tunnel `wss://`.
- **Laisse le launcher ouvert pendant la partie** : c'est lui qui fait le tunnel.
- Décoche la case si ton réseau autorise Minecraft en direct (c'est un peu plus rapide).

Autres types de serveur possibles : **Wisp** (relais `wss://` + destination `host:port`) et **EaglerX** (adresse `wss://`, connexion Java au même serveur).

**Packs envoyés par les serveurs** : téléchargés par Minecraft. Ce relais n'accepte que le trafic Minecraft, donc un pack hébergé sur un port bloqué (ex. `http://serveur:25570/pack.zip`) ne peut pas passer, y compris dans la version navigateur. Dans ce cas, demande à l'admin d'héberger le pack en HTTPS, ou installe le .zip via l'onglet Packs.

## Optimisations
- Fabric + **Sodium**, **Lithium**, **FerriteCore**, **ImmediatelyFast**, **EntityCulling**, **Krypton** et Fabric API. Les mods sont installés automatiquement pour la version choisie, et tes propres mods dans `mods` ne sont jamais touchés.
- Java 25 officiel de Mojang, avec des réglages JVM adaptés au jeu (G1 à pauses courtes, en-têtes d'objets compacts).
- Mémoire réglée automatiquement (1/4 de la RAM, entre 2 et 6 Go), modifiable dans l'onglet Jouer.

## Packs de textures
Onglet **Packs de textures** : colle un lien (direct, Dropbox, Google Drive ou GitHub) ou ajoute un `.zip`. Les ZIP qui contiennent un sous-dossier sont réparés automatiquement. Active ensuite le pack en jeu (Options → Packs de ressources).

## À savoir
- Il faut posséder Minecraft Java Edition, même en mode sans compte.
- Données dans `%LOCALAPPDATA%\EaglerJavaLauncher` (le jeu est dans `jeu\` : mondes, mods, options).
- Recompiler après une modification du code : `compiler.bat` (compilateur C# fourni avec Windows). Le mod se reconstruit avec `mod\build-mod.bat` (JDK 21+).

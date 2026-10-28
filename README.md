# QuickQR 📱⚡

**QuickQR** est une application desktop Windows ultra-légère, 100 % hors-ligne (zéro requête réseau), conçue pour transformer instantanément du texte (depuis le presse-papiers ou saisi manuellement) en QR Code haute lisibilité pour un scan immédiat par smartphone.

![QuickQR Logo](branding/logo.png)

---

## ✨ Fonctionnalités Clés

- **⚡ Instantanéité (< 50 ms) :** Génération ultra-rapide basée sur la librairie `QRCoder` (`PngByteQRCode` sans dépendance GDI+ lourde).
- **🔄 Live Sync + Auto-Lock (0 % CPU au repos) :**
  - **Live Sync :** Écoute événementielle native du presse-papiers Windows (`AddClipboardFormatListener`). Chaque copie (`Ctrl + C`) actualise automatiquement le QR Code.
  - **Auto-Lock :** Dès que vous tapez ou modifiez du texte dans l'éditeur, la synchronisation se met en pause pour protéger votre travail contre tout écrasement externe. Un clic sur le bouton réactive la capture.
- **👁️ Ingénierie Optique Pixel-Perfect :**
  - Rendu obligatoire en **Noir Pur (`#000000`) sur Fond Blanc Pur (`#FFFFFF`)**.
  - Quiet Zone forcée d'au moins **4 modules** pour une reconnaissance optique instantanée.
  - Correction d'erreur adaptative (ECC M < 150 caractères, ECC L ≥ 150 caractères pour aérer la matrice).
  - Encodage UTF-8 garanti sans caractères BOM parasites.
- **📑 Carrousel Multi-QR Intelligent :**
  - Sélecteur de seuil de découpage (250, 400 ou 600 caractères).
  - Découpe chirurgicale sur les espaces ou sauts de ligne sans couper les mots.
  - **Intégrité absolue :** Zéro texte parasite (`[1/3]`), la concaténation restitue le texte original au caractère près.
- **⚡ Minify & Vider Compatibles `Ctrl + Z` :**
  - Minifie le JSON ou compacte les espaces/retours à la ligne superflus.
  - Préserve l'historique d'annulation natif : pressez `Ctrl + Z` pour restaurer immédiatement le texte original.
- **📌 Always on Top & System Tray :**
  - Toujours au premier plan par défaut (basculable en un clic avec l'épingle).
  - Icône officielle dans la zone de notification Windows (System Tray).

---

## ⌨️ Raccourcis Clavier

| Raccourci | Portée | Action |
| :--- | :--- | :--- |
| `Ctrl + Alt + Q` | **Global Système** | Affiche, restaure et ramène la fenêtre au premier plan avec capture immédiate du presse-papiers. |
| `Échap` ou `X` | Fenêtre | Masque discrètement l'application dans le System Tray (ne ferme pas le processus). |
| `Ctrl + Q` | Fenêtre | Quitte définitivement l'application en libérant tous les hooks système. |
| `Alt + Flèche Gauche` | Fenêtre | Page précédente du carrousel QR. |
| `Alt + Flèche Droite` | Fenêtre | Page suivante du carrousel QR. |
| `Ctrl + Z` / `Ctrl + Y` | Éditeur | Annuler / Rétablir les modifications de texte (y compris Minify et Vider). |

---

## 🛠️ Stack Technique

- **Cible :** .NET 8.0 (`net8.0-windows`)
- **UI :** WPF moderne en Dark Mode (`#121214` / `#1C1C1F`) + WinForms `NotifyIcon`
- **Moteur QR :** `QRCoder 1.6.0`
- **Architecture :** Structure plate à 6 fichiers sources à la racine, zéro sous-dossier de code superflu.

---

## 🚀 Compilation & Exécution

### Lancer en mode développement
```powershell
dotnet run -c Release
```

### Générer l'exécutable autonome final
```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ./publish
```

---

*Développé avec rigueur d'ingénierie native Windows et propulsé par Antigravity.*

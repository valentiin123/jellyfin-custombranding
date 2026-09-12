# jellyfin-custombranding

Plugin Jellyfin 12 pour remplacer les assets de branding natifs (`favicon`, `apple-touch-icon`, `icon-transparent`, `banner-light`, `banner-dark`).

## Fonctionnement

- Le plugin intercepte directement les requêtes d'assets de branding Jellyfin Web.
- Chaque asset peut être configuré avec :
  - une URL distante (`http/https`), ou
  - un fichier importé depuis l'interface (stocké en Data URL dans la configuration).
- Aucune transformation de `index.html` n'est nécessaire.

## Build

```bash
dotnet build Jellyfin.Plugin.CustomBranding/Jellyfin.Plugin.CustomBranding.csproj -c Release
```

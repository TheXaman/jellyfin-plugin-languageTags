# Jellyfin Language Tags Plugin

## About
Jellyfin Language Tags Plugin is a .NET-based plugin that adds language tags to media files based on their audio tracks. It extracts language information from the audio tracks using FFmpeg. These generated language tags help users manage libraries in multilingual households.

### Example Usage
Install the plugin and scan your library. Then, navigate to the parental controls of individual users and restrict content based on desired language tags. For example:
```
language_deu
language_ger
```
This setting will only display movies, TV shows, and collections that contain German audio tracks.

## Installation
Add the following link to your jellyfin instance under Plugins -> Catalog -> Add Repository:
```
https://github.com/TheXaman/jellyfin-plugin-languageTags/blob/main/manifest.json
```

## Build Process

1. Clone or download this repository.

2. Ensure that the .NET Core SDK is installed.

3. Build plugin with following command:

```sh
dotnet publish --configuration Release
```

4. Place the resulting file in the `plugins` folder.

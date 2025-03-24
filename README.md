# 🍿 Jellyfin Language Tags Plugin 🌍

## 📖 About
Jellyfin Language Tags Plugin is a .NET-based plugin that adds language tags to media files based on their audio tracks. It extracts language information from the audio tracks using FFmpeg. Language tags help users filter media in multilingual households, showing only content in their preferred language.

## ✨ Features

- 🔄 Automatically scan the library with a scheduled job (default 24 hrs)
- 🎧 Extracts languages from a files audio tracks via ffmpeg if no language tags are present
- 🏷️ Add the tags to the media for filtering
- 🎥 Works on movies, TV shows / series and collections
- ✅ Whitelist option for languages to avoid spam if files contain many different languages
- ⚡ Runs asynchronously for speed and takes about 2 mins for a full refresh with ~ 500 movies
- 🐢 Synchronous mode for low-end devices like a Raspberry Pi
- 🔄 Force refresh option and buttons if media files are replaced or in case of problems

## 📚 Example Usage
Install the plugin and scan your library. Then, navigate to the parental controls of individual users and restrict content based on desired language tags. For example:
```
language_deu
language_ger
```
This setting will only display movies, TV shows, and collections that contain German audio tracks.

### 📱 Settings example on mobile
<p align="center">
  <img src="Images/example_on_mobile_small.png" alt="Example" width="400">
</p>

## Installation 🛠️
Add the following link to your jellyfin instance under Plugins -> Catalog -> Add Repository:
```
https://raw.githubusercontent.com/TheXaman/jellyfin-plugin-languageTags/main/manifest.json
```

## 🏗️ Build Process

1. Clone or download this repository.

2. Ensure that the .NET Core SDK is installed.

3. Build plugin with following command:

```sh
dotnet publish --configuration Release
```

4. Place the resulting file in the `plugins` folder.

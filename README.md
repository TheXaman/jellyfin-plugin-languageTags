# Jellyfin Language Tags Plugin
<p align="center">
  <img src="Images/logo.png" alt="Example" width="400">
</p>

## About
Jellyfin Language Tags Plugin adds language tags to media based on audio and subtitle tracks. It uses Jellyfin’s MediaStreams API to read stream metadata directly (no FFmpeg), delivering much faster and more reliable scans. External subtitle files (.srt, .ass) are supported.

## WHY?
Language tags help users filter media in multilingual households, allowing them to show users only the content available in their preferred languages!

## Features

- Performance & Architecture
  - 10–100x faster scanning via MediaStreams API (no FFmpeg process spawn)
  - Direct extraction of audio/subtitle languages from metadata
  - External subtitle support for .srt and .ass

- Tagging & Display
  - Configurable tag prefixes for audio and subtitle tags (with validation)
  - Full language names in tags (e.g., language_German)
  - Visual language selector for easier configuration
  - Tagging for non-media items (e.g., actors, studios) with toggles

- Operations
  - Automatic scheduled scan (default: 24h)
  - Works with movies, series (seasons/episodes), and collections
  - Asynchronous mode for speed; synchronous mode for low-end devices
  - Force refresh options when files are replaced or for troubleshooting

## Example Usage
Restrict content via user parental controls using language tags. Depending on configuration:
```
language_German
subtitle_language_German
```
This shows only items that contain German audio tracks or German subtitles.

### Settings example on mobile
<p align="center">
  <img src="Images/example_on_mobile_small.png" alt="Example" width="400">
</p>

## Configuration
- Tag prefixes
  - Audio: default language_
  - Subtitles: default subtitle_language_
  - Validation ensures safe characters
- Non-media tagging
  - Enable tagging for actors, studios etc. if needed
- Scan mode
  - Asynchronous (default) or synchronous for low-end devices
- Schedule
  - Configure periodic scans (default every 24h)

## Installation
Add this repository in Jellyfin: Plugins -> Catalog -> Add Repository:
```
https://raw.githubusercontent.com/TheXaman/jellyfin-plugin-languageTags/main/manifest.json
```

## Build (only needed for development!)
1. Clone or download the repository
2. Install the .NET SDK >= 9.0
3. Build:
```sh
dotnet publish --configuration Release
```
4. Copy the resulting output to the Jellyfin plugins folder

## What’s New

### v0.4.4.8
- Performance & Architecture
  - Replaced FFmpeg with Jellyfin MediaStreams API (10–100x faster)
  - Removed FFmpeg process overhead, parsing code (~150 LOC), and IMediaEncoder dependency
  - Direct extraction of audio/subtitle languages from metadata
  - External subtitle support retained (.srt, .ass)
- Features
  - Configurable audio/subtitle tag prefixes with validation
  - Full language names in tags + visual language selector
  - Tagging for non-media items
  - Silent movies allowlist (whitelist) (#22)
- Improvements
  - Better ISO code conversion and language tag handling
  - Fallback handling for series when episodes reference seriesId directly
  - Improved logging for series/season queries
  - Less verbose LanguageTagsManager logs

### v0.5.1.0
- Features & Maintenance
  - Refactored core functionality into separate files
  - Enhanced series processing (split handling for seasons/episodes)
  - Improved logging and tracking of skipped items
  - Fewer database update calls for better performance
  - Code cleanup (removed dead code and unused external subtitle code)

---

Note on external subtitles: Language detection for external subtitles relies on filename language identifiers. If your files use long names instead of short ISO codes (e.g., video.english.srt instead of video.eng.srt), detection may fail. Rename files per Jellyfin specs:
- Jellyfin specs: https://jellyfin.org/docs/general/server/media/movies/#external-subtitles-and-audio-tracks
- Windows (PowerRename): https://learn.microsoft.com/windows/powertoys/powerrename
- Linux (rename): https://wiki.archlinux.org/title/rename

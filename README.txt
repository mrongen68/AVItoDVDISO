# ---------------------------------
# File: README.txt
# ---------------------------------
AVI to DVD ISO (Windows, portable)

What it does:
- Converts video files to DVD-Video compliant output
- Exports DVD folder (VIDEO_TS) and/or ISO image

Defaults:
- Mode: PAL
- Preset: Fit to DVD
- Output: DVD folder + ISO
- Chapters: Off

Tools required in tools\ folder:
- ffmpeg.exe
- ffprobe.exe
- dvdauthor.exe
- xorriso.exe

Notes:
- This repository is GPL-3.0.
- You must distribute third party license notices alongside the binaries.
- DVD-Video compliance depends on the bundled tools and the input content.

Quick use:
1) Put tools in tools\
2) Run AVItoDVDISO.exe
3) Add source files, choose settings, choose output, click Convert
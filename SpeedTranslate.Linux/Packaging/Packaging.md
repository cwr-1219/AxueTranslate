# Linux Packaging — `.deb`

This directory contains everything needed to produce a Debian package
(`axue-translate_<version>_<arch>.deb`) for the Linux X11 build of
AxueTranslate.

The output `.deb` is written to `../../ReleaseSetup/`, alongside the
existing Windows `AxueTranslate_Setup.exe`.

## Build

```sh
cd SpeedTranslate.Linux/Packaging
./build-deb.sh
```

That single command:

1. Runs `dotnet publish -c Release -r linux-x64 --self-contained true` for
   `SpeedTranslate.Linux.csproj`.
2. Lays out a Debian file tree (binaries under `/usr/lib/axue-translate/`,
   launcher at `/usr/bin/axue-translate`, desktop entry, hicolor icons in
   48/64/128/256 px).
3. Substitutes `{{VERSION}}`, `{{ARCH}}`, `{{SIZE}}` into `debian/control.in`.
4. Calls `fakeroot dpkg-deb --build` with xz compression.

## Build prerequisites (host machine)

- `dotnet` 8 SDK
- `dpkg-deb` (from `dpkg-dev`)
- `fakeroot`

```sh
sudo apt install dotnet-sdk-8.0 dpkg-dev fakeroot
```

## Runtime dependencies (target machine)

Declared in `debian/control.in`:

- `xclip`, `xdotool` — clipboard read/write and keystroke simulation
- `libx11-6`, `libxext6`, `libice6`, `libsm6` — X11 + XShape (cursor passthrough)
- `libfontconfig1` — Avalonia text rendering
- `libssl3 | libssl1.1` — HTTPS for LLM API calls

The .NET runtime itself is bundled in the package (`--self-contained true`),
so no system `dotnet` install is required.

ICU is **not** required — the project sets `<InvariantGlobalization>true</InvariantGlobalization>`
because the app does no culture-aware string handling.

## Cross-architecture build

```sh
ARCH=arm64 RID=linux-arm64 ./build-deb.sh
```

You will need the matching .NET runtime pack:

```sh
dotnet workload install ...      # if cross-compiling from x86_64
```

## Override the version

```sh
VERSION=1.1.0 ./build-deb.sh
```

## Install / uninstall

```sh
# Install (recommended — auto-resolves dependencies):
sudo apt install ./ReleaseSetup/axue-translate_1.0.1_amd64.deb

# Or:
sudo dpkg -i ./ReleaseSetup/axue-translate_1.0.1_amd64.deb
sudo apt-get install -f          # pull missing deps if dpkg flagged any

# Uninstall:
sudo apt remove axue-translate          # keep config in ~/.config
sudo apt purge axue-translate           # also clear /usr/share data
```

After install you can launch from the application menu (look for
"AxueTranslate") or from a terminal:

```sh
axue-translate
```

## Wayland note

Global hotkeys and keystroke injection require X11 (XTest extension).
On Wayland sessions the app prints a warning at startup and the hotkey will
not fire. Switch to the Xorg session at the login screen to use it.

## Layout produced by the script

```
/usr/bin/axue-translate                                    # launcher script
/usr/lib/axue-translate/                                   # self-contained .NET 8 publish
    AxueTranslate                                          #   main executable
    *.dll, *.so                                            #   runtime + Avalonia + SharpHook
/usr/share/applications/axue-translate.desktop             # menu entry
/usr/share/icons/hicolor/{48,64,128,256}x*/apps/*.png      # menu icons
```

User config and logs (created at first run, untouched by the package):

```
~/.config/AxueTranslate/config.json
~/.config/AxueTranslate/error.log
~/.config/AxueTranslate/debug.log
```

# Application branding

`app-icon.svg` is the editable source for an original gold pyramid/eye, crimson card backs, and cyan/navy frame. It evokes the game's Egyptian fantasy setting without copying its official logo. This original artwork is covered by the repository's MIT license.

`app-icon.png` is a 512px preview. `app-icon.ico` contains 16, 20, 24, 32, 40, 48, 64, 128 and 256px PNG frames for Windows. Both the executable and WPF window use the ICO.

Regenerate with `node tools/Create-AppIcon.cjs` using `sharp@0.34.5`. Release builds use committed files; Node is not required. Card artwork has separate attribution in `assets/card-artwork`.

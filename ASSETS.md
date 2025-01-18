Assets and external libraries used in the project. For main project license see `LICENSE` file.

# External code
- FastNoiseLite by Jordan Peck and other contributors under MIT license available in file `src\sandboxrust\Assets\shaders\common\fast_noise_lite.hlsl`
- MIConvexHull by David Sehnal, Matthew Campbell and other contributors under MIT license available in file `src\sandboxrust\Code\Meshes\MIConvexHull\license.txt`

# Assets

## Sounds
All released under CC0 1.0 Universal

- `clang.wav` recorded by me hitting a radiator with a pen
- `click.wav` recorded by me clicking my mouse button
- `click_reverse.wav`, which is a reverse of `click.wav`
- `water_spray.wav` generated using white noise effect in Audacity

I know that I could have used some higher quality assets but this way I can be sure that I can release the project freely.

## Models and materials

Most of the scene objects (e.g. crowbar, barrels, etc.) are provided by sbox.game asset delivery system.
According to the contest rules: 

```
You can use Facepunch content
You can use content from sbox.game
```

The objects included in the `main.scene` are dynamically loaded and this project only contains references to them (example: `facepunch.v_crowbar#81554`). If you want to redistribute the project, you need to get an up-to-date licensing information for the assets provided by Facepunch. See webpage https://sbox.game/about for more information.

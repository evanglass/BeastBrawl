# Arena 5: Enchanted Forest art pack

The PNG files directly in the parent folder are the final Unity textures. This source folder contains the three original production sheets, the Apollo color table, the layout used to construct the prefab, and renders for comparison. Unity ignores the source folder during import.

- Foreground: `Maple`, `Mushroom`, `Cottage`, `MossStone`, `LilyPad`, `StormCloud`, `TimberBridge`, `Branch`, and the cropped `CapWalkRim`.
- Depth layers: `Sky`, `DistantWaterfalls`, `ForestRuins`, `NearMaples`, and `ForegroundFerns`. The reflective `Pond` is separate from the scenery layers and the lily-pad colliders.
- Effects: `Spark` and `SporeCurtain` are original procedural textures. The sparkle texture is also used by the local particle emitters.

The supplied concept was used as a composition and art-direction reference. It is not shipped as a backdrop. The three production sheets are original outputs from the built-in image tool. No third-party texture pack, stock image, or paid assets were imported. [Apollo](https://lospec.com/palette-list/apollo) by AdamCYounis is the color reference. The imported textures were sliced, keyed where required, and mapped to the 46 Apollo swatches while retaining transparency.

The foreground sheet prompt requested six separate sprites: a Japanese maple, a crimson mushroom, a lit cottage, mossy stone, a lily pad, and a storm cloud. The depth sheet prompt requested isolated moonlit sky, mountains and waterfalls, forest ruins, and framing maples. The pond and structures sheet prompt requested reflective water, a timber bridge, foreground ferns, and mossy branch sprites.

The optional editor preview uses existing Eagle and Bear sprite frames from `Assets/Sprite Sheets` at the player prefab's scale. They are not copied into the arena art or saved in the runtime prefab. Use **Tools → Arena 5 → Show Forest with Eagle and Bear** to show them again. The arena scene is the network-loadable prefab instance. To play it through the normal match loader, start with the NGO setup scene only and select **Enchanted Forest** in the custom or simulation arena picker.

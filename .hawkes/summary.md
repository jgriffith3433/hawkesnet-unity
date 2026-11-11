Demo builds 44, 45 and 46 died with Unity exit 139 (segfault) during the shader-compile phase. The last green demo build (43) was at ecdb508; the first failure (44) was at 7b8f3fa (#31, nameplates), the only change in between that touches the build script: it added `IncludeShader("Sprites/Default")` and `IncludeShader("GUI/Text Shader")` to `HawkesNetDemoBuild.Build`. The log ends in the shader-compile list, so those are my suspect. #36 (link.xml) and #37 (scene audit) were innocent: 44 and 45 failed before them.

Changed:
- `Assets/HawkesNetDemo/Editor/HawkesNetDemoBuild.cs`: removed those two IncludeShader calls (a build-script edit kept to that; Standard stays).
- `HawkesNetNamePlate.cs`: the backing material falls back to Standard in fade mode when Sprites/Default is not in the build; the text uses the font's own material.
- package 0.14.5 and CHANGELOG.

Could not check: Unity compile and the WebGL build were not run here, so this is a reasoned cause, not a proven one. If 47 still segfaults, the next suspect is the baked humanoid's glTF shader ("glTF/PbrMetallicRoughness") or the avatar bake in `HawkesNetAvatarBake.Ensure`, both from #28 (which built fine in 41-43). Nameplates in WebGL may look slightly different if Sprites/Default is stripped. No merge conflicts.

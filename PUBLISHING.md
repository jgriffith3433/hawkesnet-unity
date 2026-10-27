# Publishing to the Asset Store

The Asset Store takes a `.unitypackage` of an `Assets/` folder (uploading a UPM package directly is early access only).
The package's source of truth stays `Packages/com.hawkes.net`; the upload is made from it.

1. **The draft** (J made it, 2026-10-26): **hawkes-net**, https://publisher.unity.com/packages/1534184/edit/upload
   (category Tools > Network).
2. **Build the upload:** `python DevTools/make_unitypackage.py HawkesNet.unitypackage` (the package as `Assets/HawkesNet`,
   GUIDs kept, the Avatars sample in `Assets/HawkesNet/Samples/Avatars`).
3. **Validate:** open this project in Unity 6000.3.7f1, import `HawkesNet.unitypackage` into it (Assets > Import Package),
   Tools > Asset Store > Validator on `Assets/HawkesNet` (category Tools). Fix anything red. Delete `Assets/HawkesNet` again.
4. **Try it in Play mode:** a scene with the Avatars sample's script; press Play: it joins your project's test room.
5. **Upload:** Tools > Asset Store > Uploader, sign in with the **Unity publisher account** (only J), pick the draft,
   **Pre-exported .unitypackage**, choose `HawkesNet.unitypackage`, Upload.
6. **Fill in the draft** from [LISTING.md](LISTING.md) (the account disclosure at the very top), the images, price Free,
   then **Submit for review**.

Updating: bump `Packages/com.hawkes.net/package.json` and the CHANGELOG, tag `v<version>`, rebuild, upload a new version.

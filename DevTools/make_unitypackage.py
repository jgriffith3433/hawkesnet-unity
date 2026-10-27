"""Builds the Asset Store .unitypackage from the UPM package, without Unity (it can stay open).

The package's files go in as Assets/HawkesNet/... with their own GUIDs (the .meta files in Packages/com.hawkes.net), the
Avatars sample as Assets/HawkesNet/Samples/Avatars (GUIDs made from its path, stable between builds). A .unitypackage is a
gzipped tar with one folder per asset, named by its GUID: asset (the file; none for folders), asset.meta and pathname.
    python DevTools/make_unitypackage.py [out.unitypackage]
"""
import hashlib
import io
import os
import re
import sys
import tarfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
PKG = os.path.join(HERE, '..', 'Packages', 'com.hawkes.net')
ROOT = 'Assets/HawkesNet'
out = sys.argv[1] if len(sys.argv) > 1 else 'HawkesNet.unitypackage'
SKIP = {'package.json', 'package.json.meta'}  # a UPM manifest means nothing in Assets/


def guid_for(path):
    return hashlib.md5(('hawkesnet-unity:' + path).encode()).hexdigest()


def folder_meta(guid):
    return f'fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'.encode()


def file_meta(guid, name):
    if name.endswith('.cs'):
        return f'fileFormatVersion: 2\nguid: {guid}\nMonoImporter:\n  externalObjects: {{}}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {{instanceID: 0}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'.encode()
    return f'fileFormatVersion: 2\nguid: {guid}\nTextScriptImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'.encode()


entries = []  # (pathname in Assets, meta bytes, file bytes or None)
entries.append((ROOT, folder_meta(guid_for(ROOT)), None))
for dirpath, dirnames, filenames in os.walk(PKG):
    rel_dir = os.path.relpath(dirpath, PKG).replace(os.sep, '/')
    sample = rel_dir.startswith('Samples~')
    dirnames.sort()
    for d in dirnames:
        rel = (d if rel_dir == '.' else f'{rel_dir}/{d}')
        target = f'{ROOT}/{rel.replace("Samples~", "Samples")}'
        meta_path = os.path.join(dirpath, d + '.meta')
        meta = open(meta_path, 'rb').read() if os.path.exists(meta_path) else folder_meta(guid_for(target))
        entries.append((target, meta, None))
    for f in sorted(filenames):
        if f.endswith('.meta') or (rel_dir == '.' and f in SKIP):
            continue
        rel = f if rel_dir == '.' else f'{rel_dir}/{f}'
        target = f'{ROOT}/{rel.replace("Samples~", "Samples")}'
        meta_path = os.path.join(dirpath, f + '.meta')
        if os.path.exists(meta_path) and not sample:
            meta = open(meta_path, 'rb').read()
        else:
            meta = file_meta(guid_for(target), f)
        entries.append((target, meta, open(os.path.join(dirpath, f), 'rb').read()))


def add(t, name, data):
    info = tarfile.TarInfo(name)
    info.size = len(data)
    info.mtime = int(time.time())
    info.mode = 0o644
    t.addfile(info, io.BytesIO(data))


with tarfile.open(out, 'w:gz') as t:
    for path, meta, data in entries:
        guid = re.search(rb'guid: ([0-9a-f]{32})', meta).group(1).decode()
        d = tarfile.TarInfo(guid)  # Unity's importer needs the folder entry too
        d.type, d.mode, d.mtime = tarfile.DIRTYPE, 0o755, int(time.time())
        t.addfile(d)
        add(t, f'{guid}/asset.meta', meta)
        add(t, f'{guid}/pathname', path.encode())
        if data is not None:
            add(t, f'{guid}/asset', data)
print(f'{out}: {len(entries)} entries')

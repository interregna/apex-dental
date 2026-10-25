#!/usr/bin/env python3
"""Restore pinned external build inputs. No credentials or Git checkout required."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import urllib.parse
import urllib.request
import sys
sys.path.insert(0,str(Path(__file__).resolve().parent / "publication"))
from check_boundary import SDK_PATTERN

ROOT = Path(__file__).resolve().parents[1]


def restore(check=False):
    manifest = json.loads((ROOT / 'dependencies.json').read_text())
    revision = manifest['revision']
    if len(revision) != 40 or any(c not in '0123456789abcdef' for c in revision):
        raise ValueError('Expected a full upstream Git revision')
    missing = []
    for entry in manifest['files']:
        relative = PurePosixPath(entry['path'])
        if (relative.is_absolute() or '..' in relative.parts
                or len(relative.parts) != 2 or relative.parts[0] != 'Required dlls'
                or relative.suffix != '.dll' or SDK_PATTERN.search(relative.name.encode())):
            raise ValueError(f'Unsupported dependency path: {relative}')
        target = ROOT / str(relative)
        expected = entry['sha256']
        if target.is_file() and hashlib.sha256(target.read_bytes()).hexdigest() == expected:
            continue
        if check:
            missing.append(str(relative))
            continue
        url = ('https://raw.githubusercontent.com/OpenDental/opendental/'
               + revision + '/' + urllib.parse.quote(str(relative)))
        with urllib.request.urlopen(url, timeout=60) as response:
            data = response.read()
        if hashlib.sha256(data).hexdigest() != expected:
            raise ValueError(f'Checksum mismatch: {relative}')
        target.parent.mkdir(parents=True, exist_ok=True)
        temporary = target.with_suffix('.download')
        temporary.write_bytes(data)
        temporary.replace(target)
    if missing:
        raise SystemExit('Missing or changed dependencies:\n' + '\n'.join(missing))
    print(f'Verified {len(manifest["files"])} pinned external dependencies.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true', help='Verify local files without downloading')
    restore(parser.parse_args().check)

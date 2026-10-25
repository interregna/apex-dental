#!/usr/bin/env python3
"""Record reviewed public source files. Run only after reviewing all proposed changes."""
import hashlib
import json
from pathlib import Path
import subprocess

ROOT=Path(__file__).resolve().parents[2]

def update():
    names=subprocess.check_output(
        ['git','ls-files','--cached','--others','--exclude-standard','-z'],cwd=ROOT
    ).decode().split('\0')
    files={}
    for name in sorted(set(filter(None,names))):
        path=ROOT/name
        if name=='public-files.json' or not path.exists():
            continue
        if path.is_symlink():
            raise ValueError('Symlink cannot enter the public inventory: '+name)
        files[name]=hashlib.sha256(path.read_bytes()).hexdigest()
    (ROOT/'public-files.json').write_text(json.dumps({'schema':1,'scope':'practice-software','files':files},indent=2)+'\n')
    print(f'Recorded {len(files)} reviewed public files. Stage and run the boundary check.')

if __name__=='__main__':
    update()

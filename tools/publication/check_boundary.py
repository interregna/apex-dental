#!/usr/bin/env python3
"""Check reviewed source in the index, working tree, or committed snapshot."""
from pathlib import Path, PurePosixPath
import argparse
import fnmatch
import hashlib
import json
import posixpath
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[2]
ALLOWED_ROOTS={'CodeBase','DataConnectionBase','OpenDentBusiness','OpenDentBusinessCore',
               'OpenDentalWebDemo','OpenDental','tools','docs','site','.github'}
ALLOWED_FILES={'.gitignore','.gitattributes','.env.example','LICENSE','NOTICE','README.md',
               'CONTRIBUTING.md','SECURITY.md','dependencies.json','public-files.json','global.json','Directory.Build.props','NuGet.Config'}
EXCLUDED_PARTS={'private','secrets','.aws','bin','obj','.venv','__pycache__'}
SECRET_PATTERNS={
    'private key':rb'-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----',
    'AWS access key':rb'\b(?:AKIA|ASIA)[A-Z0-9]{16}\b',
    'GitHub token':rb'\b(?:gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{40,})\b',
    'Google API key':rb'\bAIza[0-9A-Za-z_-]{35}\b',
    'Slack token':rb'\bxox[baprs]-[0-9A-Za-z-]{20,}\b',
    'Stripe live key':rb'\bsk_live_[0-9A-Za-z]{20,}\b',
}
SDK_PATTERN=re.compile(
    rb'AWSSDK\.|Azure\.Core|Google\.Apis|Dropbox\.Api|Intuit\.Ipp|IppDotNetSdk'
    rb'|Avalara\.|Microsoft\.(?:Azure|Graph|Identity)|Serilog\.Sinks\.Azure',re.I)


def inspect_inventory(contents):
    try:
        manifest=json.loads(contents['public-files.json'])
        if manifest.get('scope')!='practice-software' or manifest.get('schema')!=1:
            return ['Unsupported public inventory format']
        expected=manifest['files']
    except (KeyError,ValueError,TypeError):
        return ['A reviewed public-files.json inventory is required']
    actual=set(contents)-{'public-files.json'}
    errors=[f'{name}: not in reviewed public inventory' for name in sorted(actual-set(expected))]
    errors.extend(f'{name}: inventoried file is missing' for name in sorted(set(expected)-actual))
    for name in sorted(actual.intersection(expected)):
        if hashlib.sha256(contents[name]).hexdigest()!=expected[name]:
            errors.append(f'{name}: changed since public inventory review')
    return errors


def inspect(name,data):
    path=PurePosixPath(name)
    errors=[]
    if name not in ALLOWED_FILES and (len(path.parts)==1 or path.parts[0] not in ALLOWED_ROOTS):
        errors.append('path outside public source boundary')
    if EXCLUDED_PARTS.intersection(path.parts):
        errors.append('private configuration or generated output')
    if (any(fnmatch.fnmatch(path.name.lower(),pattern) for pattern in ('*credential*.json','*credential*.txt','*secret*.json','*secret*.txt','*login*.txt'))
        or (path.name.startswith('.env') and path.name!='.env.example')
        or path.suffix.lower() in {'.dll','.exe','.pdb','.pfx','.p12','.pem','.key','.sql','.db','.sqlite','.dump','.bak'}):
        errors.append('forbidden credential, database, or binary artifact')
    if path.parts[0]=='OpenDental' and not (name.startswith('OpenDental/Resources/') and path.suffix=='.txt'):
        errors.append('only upstream attribution text is allowed in OpenDental/')
    if len(data)>50*1024*1024:
        errors.append('file exceeds source publication size limit')
    if path.suffix in {'.csproj','.items','.props','.targets'} or name=='dependencies.json':
        if SDK_PATTERN.search(data):
            errors.append('vendor SDK is outside the public process boundary')
    if path.suffix=='.cs' and re.search(
        rb'(?m)^\s*(?:using\s+(?:[A-Za-z]+\s*=\s*)?|extern alias\s+)'
        rb'(?:Amazon|Azure|Google\.Apis|Dropbox\.Api|Intuit\.Ipp|Avalara|Microsoft\.(?:Azure|Graph|Identity))\b',data):
        errors.append('embedded vendor implementation')
    if path.suffix in {'.csproj','.items','.props','.targets'}:
        try:
            for element in ET.fromstring(data).iter():
                for attribute in ('Include','Project'):
                    value=element.get(attribute,'')
                    if value and ('/' in value or '\\' in value):
                        value=value.replace('\\','/')
                        joined=posixpath.normpath(str(path.parent)+'/'+value)
                        if value.startswith('/') or re.match(r'^[A-Za-z]:',value) or joined.startswith('../'):
                            errors.append('project reference escapes the public repository')
        except ET.ParseError:
            errors.append('invalid build XML')
    for label,pattern in SECRET_PATTERNS.items():
        if re.search(pattern,data):errors.append(f'possible {label} (value redacted)')
    return errors


def git(*arguments,input=None):
    return subprocess.check_output(['git',*arguments],cwd=ROOT,input=input)


def read_snapshot(source):
    indexed=[];errors=[]
    entries=git('ls-files','--stage','-z') if source=='index' else git('ls-tree','-rz','HEAD')
    for entry in filter(None,entries.split(b'\0')):
        metadata,name=entry.split(b'\t',1);fields=metadata.split();mode=fields[0]
        oid=fields[1] if source=='index' else fields[2]
        if mode not in {b'100644',b'100755'} or (source=='index' and fields[2]!=b'0'):
            errors.append(name.decode()+': symlink, submodule, or unresolved merge')
        else:indexed.append((name.decode(),oid))
    blobs=git('cat-file','--batch',input=b''.join(oid+b'\n' for _,oid in indexed))
    contents={};offset=0
    for name,_ in indexed:
        end=blobs.index(b'\n',offset);size=int(blobs[offset:end].split()[2])
        contents[name]=blobs[end+1:end+1+size];offset=end+size+2
    return contents,errors


def read_worktree():
    names=set(filter(None,git('ls-files','--cached','--others','--exclude-standard','-z').decode().split('\0')))
    contents={};errors=[]
    for name in sorted(names):
        path=ROOT/name
        if path.is_symlink():errors.append(name+': symlink is not permitted')
        elif path.is_file():contents[name]=path.read_bytes()
        elif path.exists():errors.append(name+': directory/submodule is not permitted')
    return contents,errors


def main(argv=None):
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source',choices=['all','index','worktree','head'],default='all',
                        help='Default: independently check both index and working tree, including untracked source')
    args=parser.parse_args(argv)
    sources=['index','worktree'] if args.source=='all' else [args.source]
    failures=[]
    for source in sources:
        contents,errors=read_worktree() if source=='worktree' else read_snapshot(source)
        for name,data in contents.items():errors.extend(f'{name}: {error}' for error in inspect(name,data))
        errors.extend(inspect_inventory(contents))
        if not contents:errors.append('No public source found')
        failures.extend(f'{source}: {error}' for error in errors)
        if not errors:print(f'Publication boundary passed for {len(contents)} {source} files.')
    if failures:
        print('Publication boundary FAILED:\n'+'\n'.join(failures));return 1
    return 0


if __name__=='__main__':sys.exit(main())

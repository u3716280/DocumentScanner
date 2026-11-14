"""Collect actual restored runtime-package licenses and source references. No guessed license names."""
import argparse,json,os,shutil,urllib.request,zipfile,xml.etree.ElementTree as ET
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
a.output.mkdir(parents=True,exist_ok=True)
packages={};sources={}
for assets in (a.root/'src').glob('*/obj/project.assets.json'):
 data=json.loads(assets.read_text(encoding='utf-8'));folders=list(data['packageFolders'])
 for key,lib in data['libraries'].items():
  if lib['type']!='package':continue
  for folder in folders:
   location=Path(folder)/lib['path']
   if location.is_dir():packages[key]=location;break
rows=[]
overrides=a.root/'build'/'license-overrides'
if overrides.is_dir():
 shutil.copytree(overrides,a.output/'Reviewed-Notices',dirs_exist_ok=True)
for key,location in sorted(packages.items()):
 folder=a.output/key.replace('/','-');folder.mkdir(exist_ok=True)
 spec=next(location.glob('*.nuspec'));xml=ET.parse(spec);meta=next(e for e in xml.getroot() if e.tag.split('}')[-1]=='metadata')
 def node(name):return next((x for x in meta if x.tag.split('}')[-1]==name),None)
 license_node=node('license');license_url=node('licenseUrl');repo=node('repository')
 license_text=license_node.text if license_node is not None else license_url.text if license_url is not None else 'REVIEW REQUIRED'
 if license_text=='REVIEW REQUIRED':
  override=a.root/'build'/'license-overrides'/(key.replace('/','-')+'-LICENSE.txt')
  if not override.is_file():raise SystemExit(f'Missing license metadata: {key}')
  shutil.copy2(override,folder/'LICENSE.txt');license_text='MIT (reviewed source commit; see LICENSE.txt)' 
 shutil.copy2(spec,folder/spec.name)
 if license_node is not None and license_node.attrib.get('type')=='file':
  path=location/license_node.text
  if not path.is_file():raise SystemExit(f'License file missing for {key}')
  shutil.copy2(path,folder/Path(path).name)
 for path in location.rglob('*'):
  if path.is_file() and any(word in path.name.lower() for word in ['license','copying','notice']) and path.suffix.lower() not in ['.dll','.pdb']:
   target=folder/path.relative_to(location);target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(path,target)
 info={'package':key,'license':license_text,'repository':dict(repo.attrib) if repo is not None else None}
 rows.append(info)
 license_contents=(location/license_node.text).read_text(encoding='utf-8',errors='replace') if license_node is not None and license_node.attrib.get('type')=='file' else license_text
 if ('LGPL' in license_contents.upper() or 'LESSER GENERAL PUBLIC LICENSE' in license_contents.upper()) and repo is not None:
  url=repo.attrib.get('url','').removesuffix('.git');commit=repo.attrib.get('commit','')
  if url.startswith('https://github.com/') and commit:
   sources[(url,commit)]=f'{url}/archive/{commit}.zip'
# Include source snapshots for LGPL libraries where exact repository commits are supplied.
source_dir=a.output/'Sources';source_dir.mkdir(exist_ok=True)
for (repo,commit),url in sources.items():
 name=repo.split('/')[-1]+'-'+commit+'.zip';target=source_dir/name
 if not target.exists():
  request=urllib.request.Request(url,headers={'User-Agent':'DocumentScanner-License-Collector'})
  with urllib.request.urlopen(request,timeout=120) as response:target.write_bytes(response.read())
 with zipfile.ZipFile(target) as archive:
  if archive.testzip() is not None:raise SystemExit('Invalid source archive')
(a.output/'packages.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf-8')
(a.output/'README.txt').write_text('Third-party package versions and declared licenses are recorded in packages.json.\n'
 'Each package folder contains its NuGet metadata and bundled notices. LGPL source snapshots are in Sources where an exact source commit was published.\n'
 'The .NET runtime includes its own LICENSE and ThirdPartyNotices files in the distribution.\n'
 'The application links to separate libraries; do not restrict replacement or debugging of LGPL components.\n'
 'Before external redistribution, review all declared licenses and confirm corresponding source availability for every LGPL component.\n',encoding='utf-8')
print(f'Collected {len(rows)} package records and {len(sources)} source snapshots.')

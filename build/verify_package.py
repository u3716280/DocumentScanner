"""Static portable-package checks. This does NOT launch Windows or scan hardware."""
import argparse,hashlib,json,struct
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('folder',type=Path);p.add_argument('--runtime',choices=['win-x64','win-x86'],required=True);p.add_argument('--output',type=Path);a=p.parse_args()
def machine(file):
 with file.open('rb') as f:
  assert f.read(2)==b'MZ',f'Not a PE file: {file.name}'
  f.seek(0x3c);offset=struct.unpack('<I',f.read(4))[0];f.seek(offset)
  assert f.read(4)==b'PE\0\0',f'Invalid PE header: {file.name}'
  return struct.unpack('<H',f.read(2))[0]
required=['DocumentScanner.exe','DocumentScanner.dll','DocumentScanner.deps.json','DocumentScanner.runtimeconfig.json',
 'coreclr.dll','hostfxr.dll','hostpolicy.dll','PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll',
 'NAPS2.Worker.exe','NAPS2.Sdk.dll','NAPS2.Images.Wpf.dll','DocumentScanner.Scanning.dll','DocumentScanner.Export.dll']
for name in required:assert (a.folder/name).is_file(),f'Missing dependency: {name}'
expected=0x8664 if a.runtime=='win-x64' else 0x14c
assert machine(a.folder/'DocumentScanner.exe')==expected,'Application architecture mismatch'
assert machine(a.folder/'coreclr.dll')==expected,'CoreCLR architecture mismatch'
assert machine(a.folder/'NAPS2.Worker.exe')==0x14c,'TWAIN worker must be x86'
native=a.folder/('_win64' if a.runtime=='win-x64' else '_win32')/'pdfium.dll'
assert native.exists() and machine(native)==expected,'PDFium native dependency mismatch'
config=json.loads((a.folder/'DocumentScanner.runtimeconfig.json').read_text())['runtimeOptions']
frameworks={f['name']:f['version'] for f in config.get('includedFrameworks',[])}
assert frameworks.get('Microsoft.NETCore.App')=='10.0.15','Incorrect self-contained runtime'
assert frameworks.get('Microsoft.WindowsDesktop.App')=='10.0.15','Incorrect WPF runtime'
report={'runtime':a.runtime,'checks':'passed (static only)','frameworks':frameworks,'worker_architecture':'x86',
 'hardware_tested':False,'windows_launch_tested':False,'files':[]}
for path in sorted(a.folder.rglob('*')):
 if path.is_file():report['files'].append({'path':path.relative_to(a.folder).as_posix(),'bytes':path.stat().st_size,'sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
if a.output:a.output.write_text(json.dumps(report,indent=2),encoding='utf-8')
print(f"{a.runtime}: PE architectures, self-contained .NET/WPF 10.0.15, TWAIN worker and PDFium files verified. {len(report['files'])} files.")

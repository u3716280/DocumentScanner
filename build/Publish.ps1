param([ValidateSet('win-x86','win-x64')][string]$Runtime='win-x64',[string]$Version='2.3.0')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $folder=Join-Path $root "publish/$Runtime/DocumentScanner"
    if(Test-Path $folder){Remove-Item $folder -Recurse -Force}
    dotnet publish src/DocumentScanner.App/DocumentScanner.App.csproj -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:PublishAot=false -p:PublishReadyToRun=true -p:Version=$Version -o $folder
    if($LASTEXITCODE -ne 0){throw 'Publish failed'}
    if(!(Test-Path (Join-Path $folder 'DocumentScanner.exe'))){throw 'Application executable is missing'}
    if(!(Get-ChildItem $folder -Recurse -Filter 'NAPS2.Worker.exe')){throw 'TWAIN Win32 worker is missing'}
    foreach($name in @('_linux','_linuxarm','_mac','_macarm','_winarm')){
        $unneeded=Join-Path $folder $name
        if(Test-Path $unneeded){Remove-Item $unneeded -Recurse -Force}
    }
    if($Runtime -eq 'win-x86'){
        $unneeded=Join-Path $folder '_win64'
        if(Test-Path $unneeded){Remove-Item $unneeded -Recurse -Force}
    }
    foreach($name in @('Config','Licenses')){New-Item -ItemType Directory -Force -Path (Join-Path $folder $name)|Out-Null}
    Copy-Item README.md (Join-Path $folder 'README.txt')
    Copy-Item docs/ERPNext-Scan-Link.md (Join-Path $folder 'ERPNext-Scan-Link.txt')
    Copy-Item erpnext/client-scripts (Join-Path $folder 'ERPNext-Client-Scripts') -Recurse
    Copy-Item START-HERE-TH.txt (Join-Path $folder 'START-HERE-TH.txt')
    Copy-Item docs/Validation-Report.md (Join-Path $folder 'Validation-Report.txt')
    Copy-Item docs/Hardware-Tests.md (Join-Path $folder 'Hardware-Tests.txt')
    Copy-Item docs/Known-Limitations.md (Join-Path $folder 'Known-Limitations.txt')
    python build/collect_licenses.py --root $root --output (Join-Path $folder 'Licenses')
    if($LASTEXITCODE -ne 0){throw 'License collection failed'}
    $artifacts=Join-Path $root 'artifacts';New-Item -ItemType Directory -Force -Path $artifacts|Out-Null
    python build/verify_package.py $folder --runtime $Runtime --output (Join-Path $artifacts "$Runtime-inventory.json")
    if($LASTEXITCODE -ne 0){throw 'Static package validation failed'}
    $zip=Join-Path $artifacts "DocumentScanner-v$Version-$Runtime.zip"
    if(Test-Path $zip){Remove-Item $zip -Force}
    Compress-Archive -Path $folder -DestinationPath $zip -CompressionLevel Optimal
    $hash=(Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path $zip -Leaf)"|Set-Content "$zip.sha256" -Encoding ascii
    Write-Output $zip
} finally {Pop-Location}


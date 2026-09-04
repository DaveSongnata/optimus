# build-all.ps1 — builds Optimus end to end and assembles the distributable installer.
#
#   1. Optimus.Resources (icon DLL)   2. Optimus.AddIn (plugin + deps)
#   3. dist/payload/  (everything the installer embeds into Addons\Optimus\)
#   4. Optimus.Installer (embeds the payload)
#   5. dist/Optimus_Setup/  (the folder to zip and hand to the client)
#
# Run from anywhere:  powershell -ExecutionPolicy Bypass -File scripts\build-all.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = 'dotnet'
$cfg = 'Release'; $tfm = 'net48'

function Step($m){ Write-Host "==> $m" -ForegroundColor Cyan }

# 0. Regenerate the icon + .res (idempotent; safe to re-run).
Step 'Gerando ícone e recurso Win32'
& "$root\src\Optimus.Resources\icons\gen_icons.ps1" "$root\src\Optimus.Resources\icons" | Out-Null
& "$root\src\Optimus.Resources\build_res.ps1" "$root\src\Optimus.Resources\icons" "$root\src\Optimus.Resources\optimus.res" | Out-Null

# 0b. Static-check the pages' JavaScript BEFORE anything is compiled or shipped.
#     A missing helper (esc) broke every render function in the add-in for weeks while all
#     414 unit tests stayed green — none of them can see JavaScript. This closes that gap.
# 0a. Regenerate the Tailwind utilities BEFORE the JS check and before anything is compiled.
#     Build time, never the browser: the in-browser JIT would mean shipping a CSS compiler into a
#     panel whose first rule is that it can never take CorelDRAW down. The CLI does the same job
#     here and ships bytes instead of a compiler. Output is injected into each page's
#     <style id="tw"> block, so the docker stays ONE offline file with no second way to load
#     half-styled.
Step 'Gerando CSS (Tailwind, tempo de build)'
& node "$root\scripts\build-css.js"
if ($LASTEXITCODE -ne 0) { throw "Geracao do CSS falhou - build abortado." }

Step 'Verificando o JavaScript das telas'
& node "$root\scripts\check-ui-js.js"
if ($LASTEXITCODE -ne 0) { throw "JavaScript das telas com erro - build abortado." }

# 1-2. Build the icon DLL and the plugin.
Step 'Compilando Optimus.Resources'
& $dotnet build "$root\src\Optimus.Resources\Optimus.Resources.csproj" -c $cfg -v quiet
Step 'Compilando Optimus.AddIn'
& $dotnet build "$root\src\Optimus.AddIn\Optimus.AddIn.csproj" -c $cfg --no-incremental -v quiet

$addinOut = "$root\src\Optimus.AddIn\bin\$cfg\$tfm"
$resOut   = "$root\src\Optimus.Resources\bin\$cfg\$tfm"

# 3. Assemble the payload (everything that lands in <Corel>\Programs64\Addons\Optimus\).
$payload = "$root\dist\payload"
Step "Montando payload em $payload"
if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
New-Item -ItemType Directory -Force -Path $payload | Out-Null
Get-ChildItem "$addinOut\*.dll" | Copy-Item -Destination $payload -Force        # AddIn + all managed deps
Copy-Item "$resOut\Optimus.Resources.dll" $payload -Force                        # icon DLL
Copy-Item "$addinOut\runtimes\win-x64\native\WebView2Loader.dll" $payload -Force # native WebView2 loader

# Voice command (F11): whisper.cpp's native DLLs. Whisper.net.Runtime ships them under
# build/win-x64/ in the NuGet cache (NOT runtimes/win-x64/, so the SDK's normal build-output copy
# never picks them up for a class library with no RuntimeIdentifier) — grabbed straight from the
# package cache, same fix WebView2Loader.dll already needed above.
#
# CRITICAL: unlike WebView2Loader.dll (loaded by OUR OWN SetDllDirectory call, so a flat folder is
# fine), Whisper.net's OWN NativeLibraryLoader looks for these under "runtimes/win-x64/" relative to
# WHERE Whisper.net.dll ITSELF WAS LOADED FROM (our addon folder — confirmed by reproducing the
# exact host/plugin split locally: a separate "host" exe with no Whisper files, loading Whisper.net.dll
# via AssemblyResolve from a sibling "plugin" folder, succeeded once the natives sat directly under
# plugin/runtimes/win-x64/). An EARLIER attempt added an extra "native" segment
# (runtimes/win-x64/native/) copying a generic .NET native-asset convention that does NOT apply to
# Whisper.net's own loader — that extra folder was the whole bug ("Native Library not found in
# default paths", measured on Davi's machine, 2026-08-14/15). No "native" subfolder — flat DLLs
# directly under runtimes/win-x64/.
$whisperRuntimeVersion = (Select-String -Path "$root\src\Optimus.Windows\Optimus.Windows.csproj" -Pattern 'Whisper\.net\.Runtime"\s+Version="([^"]+)"').Matches[0].Groups[1].Value
$whisperNative = "$env:USERPROFILE\.nuget\packages\whisper.net.runtime\$whisperRuntimeVersion\build\win-x64"
if (-not (Test-Path $whisperNative)) { throw "DLLs nativas do Whisper.net nao encontradas em $whisperNative (rode dotnet restore primeiro)" }
$whisperNativeOut = "$payload\runtimes\win-x64"
New-Item -ItemType Directory -Force -Path $whisperNativeOut | Out-Null
Get-ChildItem "$whisperNative\*.dll" | Copy-Item -Destination $whisperNativeOut -Force
# The ggml model — the actual "brain" of the offline voice command, renamed generically so a future
# model swap (accuracy/size tuning) never touches code (LocalVoiceTranscriber.ModelFileName).
$voiceModel = "$root\assets\voice\ggml-model.bin"
if (-not (Test-Path $voiceModel)) { throw "Modelo de voz ausente em $voiceModel" }

Copy-Item $voiceModel $payload -Force

# Voice command CONFIRMATION AUDIO: fixed WAV files rendered ONCE (Piper, offline) and bundled as
# plain audio — same spoken confirmation on every machine, never the operator's own installed TTS
# (Davi rejected relying on window.speechSynthesis outright, 2026-08-15: quality varies per PC).
$voiceSounds = "$root\assets\voice\sounds\pt-BR"
if (-not (Test-Path $voiceSounds)) { throw "Audios de voz ausentes em $voiceSounds" }
$voiceSoundsOut = "$payload\voice-sounds\pt-BR"
New-Item -ItemType Directory -Force -Path $voiceSoundsOut | Out-Null
Get-ChildItem "$voiceSounds\*.wav" | Copy-Item -Destination $voiceSoundsOut -Force

Write-Host ("    {0} arquivo(s) no payload" -f (Get-ChildItem $payload).Count)

# 3b. Assemble the MAINTENANCE APP payload (Module 1) — the standalone desktop app the installer
#     deploys to %ProgramFiles%\Optimus\Manutencao\ plus a desktop shortcut. Separate folder because
#     it is a different destination, not part of the CorelDRAW addon.
Step 'Compilando Optimus.Maintenance'
& $dotnet build "$root\src\Optimus.Maintenance\Optimus.Maintenance.csproj" -c $cfg --no-incremental -v quiet

$appOut = "$root\src\Optimus.Maintenance\bin\$cfg\$tfm"
$appPayload = "$root\dist\payload-app"
Step "Montando payload do app de manutenção em $appPayload"
if (Test-Path $appPayload) { Remove-Item $appPayload -Recurse -Force }
New-Item -ItemType Directory -Force -Path $appPayload | Out-Null
Get-ChildItem "$appOut\*.dll" | Copy-Item -Destination $appPayload -Force
Copy-Item "$appOut\Optimus.Manutencao.exe" $appPayload -Force
if (Test-Path "$appOut\Optimus.Manutencao.exe.config") {
  Copy-Item "$appOut\Optimus.Manutencao.exe.config" $appPayload -Force
}
Copy-Item "$appOut\runtimes\win-x64\native\WebView2Loader.dll" $appPayload -Force
Write-Host ("    {0} arquivo(s) no payload do app" -f (Get-ChildItem $appPayload).Count)

# 4. Build the installer (embeds dist/payload/*.dll + dist/payload-app/* + the addon manifest).
#    The version is stamped into the assembly so the "Apps & features" entry shows the build the
#    customer actually received — InstallerEngine.Version reads it back at runtime.
$versionTag = (Select-String -Path "$root\src\Optimus.AddIn\Build.cs" -Pattern 'Tag\s*=\s*"([^"]+)"').Matches[0].Groups[1].Value
Step "Compilando Optimus.Installer (v$versionTag)"
& $dotnet build "$root\installer\Optimus.Installer.csproj" -c $cfg --no-incremental -v quiet `
    -p:Version=$versionTag -p:AssemblyVersion="$versionTag.0" -p:FileVersion="$versionTag.0"

# 5. Grab the single merged EXE (ILRepack output). Plug and play — no loose DLLs.
$setup = "$root\dist\Optimus_Setup"
Step "Montando distribuição (EXE único) em $setup"
if (Test-Path $setup) { Remove-Item $setup -Recurse -Force }
New-Item -ItemType Directory -Force -Path $setup | Out-Null
$packed = "$root\installer\bin\$cfg\$tfm\packed\Optimus_Setup.exe"
if (-not (Test-Path $packed)) { throw "ILRepack não gerou $packed" }
Copy-Item $packed $setup -Force

# 6. Publish to a VERSIONED client folder. Previous versions are NEVER deleted — each release
#    keeps its own folder so a client can always be rolled back to the build they were given.
$version = (Select-String -Path "$root\src\Optimus.AddIn\Build.cs" -Pattern 'Tag\s*=\s*"([^"]+)"').Matches[0].Groups[1].Value
if (-not $version) { throw "Não foi possível ler a versão de Build.cs" }
$clientes = "$root\shared\bin\redistributables\Clientes\$version"
Step "Publicando versão $version em $clientes"
New-Item -ItemType Directory -Force -Path $clientes | Out-Null   # never Remove-Item the parent
Copy-Item "$setup\Optimus_Setup.exe" $clientes -Force
# Licence attribution travels with the delivery, not only inside the EXE.
Copy-Item "$root\THIRD-PARTY-NOTICES.txt" $clientes -Force
Copy-Item "$root\THIRD-PARTY-NOTICES.txt" $setup -Force
Compress-Archive -Path "$setup\Optimus_Setup.exe", "$setup\THIRD-PARTY-NOTICES.txt" `
    -DestinationPath "$clientes\Optimus_Setup.zip" -Force

$exeMb = [math]::Round((Get-Item $packed).Length/1MB, 1)
Write-Host ""
Write-Host "PRONTO." -ForegroundColor Green
Write-Host "Versão $version ($exeMb MB): $clientes\Optimus_Setup.exe  (+ .zip)"
Write-Host "Versões disponíveis:"
Get-ChildItem "$root\shared\bin\redistributables\Clientes" -Directory | ForEach-Object { Write-Host ("  - " + $_.Name) }
Write-Host "Rode Optimus_Setup.exe como Administrador, com o CorelDRAW fechado."

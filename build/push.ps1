# Pushes to GitHub, retrying while the network intercepts HTTPS (git rejects untrusted certificates)
Set-Location (Split-Path $PSScriptRoot)
for ($i = 1; $i -le 40; $i++) {
    git pull --rebase -q origin main   # picks up web uploads and the bot's setup builds
    if ($LASTEXITCODE -eq 0) { git push origin main }
    if ($LASTEXITCODE -eq 0) { Write-Host 'PUSH OK'; exit 0 }
    Write-Host "Versuch $i fehlgeschlagen, neuer Versuch in 5 s ..."
    Start-Sleep 5
}
Write-Host 'PUSH FEHLGESCHLAGEN'
exit 1

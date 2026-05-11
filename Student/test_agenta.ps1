$basePath = "C:\MonitoringTests"
$pcs = @("PC01", "PC02", "PC03", "PC04")

while($true) {
    foreach ($pc in $pcs) {
        $path = "$basePath\$pc"
        if (!(Test-Path $path)) { New-Item -ItemType Directory -Path $path }
        $time = Get-Date -Format "HH:mm:ss"
        Add-Content -Path "$path\log.txt" -Value "Zmiana: $time na $pc"
        Write-Host "Zapisano w $pc o $time" -ForegroundColor Green
    }
    Start-Sleep -Seconds 10
}
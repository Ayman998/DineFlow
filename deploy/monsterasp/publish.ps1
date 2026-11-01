# Publishes helloZen to MonsterASP (https://app.hello-zen.com) with the MonsterASP publish profile:
# it builds both Angular apps and the API and copies them to the website over Web Deploy. From the
# repository root:
#   .\deploy\monsterasp\publish.ps1
# It asks for the WebDeploy password (website > Overview > Visual Studio access > Password) without
# echoing it. Visual Studio's Publish > MonsterASP does the same thing.
$secure = Read-Host 'WebDeploy password' -AsSecureString
# MSBuild reads environment variables as properties, so the password needs no escaping and stays off
# the command line.
$env:Password = [System.Net.NetworkCredential]::new('', $secure).Password
try {
    dotnet publish "$PSScriptRoot\..\..\HelloZen\HelloZen.Api" -c Release -p:PublishProfile=MonsterASP
    if ($LASTEXITCODE -eq 0) { Write-Host 'Published. Check https://app.hello-zen.com/health/ready' }
    exit $LASTEXITCODE
}
finally {
    Remove-Item Env:Password -ErrorAction SilentlyContinue
}

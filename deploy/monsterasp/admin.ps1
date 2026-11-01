# Runs a helloZen admin command (migrate, provision ...) from this PC against the MonsterASP database.
# MonsterASP has no shell, so these run here with the API's own code. From the repository root:
#   .\deploy\monsterasp\admin.ps1 migrate
#   .\deploy\monsterasp\admin.ps1 provision --business "Hashem Kitchen" --venue "Abdoun, Amman" --owner-email owner@example.jo --owner-name "Lina Haddad"
# It asks for the database's Remote access connection string (Databases > db77252 > Remote access,
# switched on while you run this) without echoing it, and forgets it when the command ends.
param([Parameter(ValueFromRemainingArguments = $true)] [string[]] $Command)

if (-not $Command) {
    Write-Host 'Usage: .\deploy\monsterasp\admin.ps1 migrate | provision --business ... (see deploy/monsterasp/README.md)'
    exit 2
}

function Read-Secret([string] $Prompt) {
    [System.Net.NetworkCredential]::new('', (Read-Host $Prompt -AsSecureString)).Password
}

$env:ConnectionStrings__HelloZen = Read-Secret 'MonsterASP connection string'
if ($Command[0] -eq 'provision') {
    # Provisioning stores a hash of each table's QR token, and the token is signed with Qr:TokenKey.
    # With any key but the server's, the printed codes wouldn't open.
    $env:Qr__TokenKey = Read-Secret 'Qr__TokenKey (the same value as on MonsterASP)'
}
try {
    # The http launch profile runs as Development, so the server's production checks don't apply here;
    # the commands only touch the database.
    dotnet run --project "$PSScriptRoot\..\..\HelloZen\HelloZen.Api" --launch-profile http -- @Command
    exit $LASTEXITCODE
}
finally {
    Remove-Item Env:ConnectionStrings__HelloZen, Env:Qr__TokenKey -ErrorAction SilentlyContinue
}

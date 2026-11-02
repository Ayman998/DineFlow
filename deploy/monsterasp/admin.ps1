# Runs a helloZen admin command (migrate, provision ...) from this PC against the MonsterASP database.
# MonsterASP has no shell, so these run here with the API's own code. From the repository root:
#   .\deploy\monsterasp\admin.ps1 migrate
#   .\deploy\monsterasp\admin.ps1 provision --business "Hashem Kitchen" --venue "Abdoun, Amman" --owner-email owner@example.jo --owner-name "Lina Haddad"
#   .\deploy\monsterasp\admin.ps1 reset-password --email owner@example.jo
# It asks for the database's password (Databases > db77252 > Remote access > Password, with Remote
# access switched on while you run this) without echoing it, and forgets it when the command ends.
# A whole Remote access connection string works too.
param([Parameter(ValueFromRemainingArguments = $true)] [string[]] $Command)

if (-not $Command) {
    Write-Host 'Usage: .\deploy\monsterasp\admin.ps1 migrate | provision --business ... | reset-password --email ... (see deploy/monsterasp/README.md)'
    exit 2
}

function Read-Secret([string] $Prompt) {
    [System.Net.NetworkCredential]::new('', (Read-Host $Prompt -AsSecureString)).Password
}

if ($Command[0] -in 'provision', 'reset-password') {
    # The password the owner (or whoever is reset) will sign in with, so nothing is printed that could be
    # lost with the window. Asked first and twice, so a typo is caught before anything touches the
    # database. The API reads it from HELLOZEN_NEW_PASSWORD (see AdminCommands), never the command line.
    $password = Read-Secret 'Portal password to sign in with (at least 10 characters)'
    if ($password.Length -lt 10) { Write-Host 'That is shorter than 10 characters. Nothing was changed.'; exit 2 }
    if ($password -ne (Read-Secret 'The same password again')) { Write-Host 'The two passwords differ. Nothing was changed.'; exit 2 }
    $env:HELLOZEN_NEW_PASSWORD = $password
}

$secret = Read-Secret 'Database password (db77252 > Remote access > Password)'
if ($secret -match '^\s*Server=') {
    # A whole connection string was pasted. The Local access one (db77252.databaseasp.net) only works from
    # websites hosted at MonsterASP, and one copied before "Show with password" still says ********.
    if ($secret -notmatch '\.public\.') { Write-Host 'That is the Local access string. Paste the password from Remote access instead.'; exit 2 }
    if ($secret -match 'Password=\*+') { Write-Host 'The password in that string is still hidden (****). Paste the password itself instead.'; exit 2 }
    $env:ConnectionStrings__HelloZen = $secret
}
else {
    # Only the password changes between runs, so the rest of db77252's Remote access string is built here.
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder['Data Source'] = 'db77252.public.databaseasp.net'
    $builder['Initial Catalog'] = 'db77252'
    $builder['User ID'] = 'db77252'
    $builder['Password'] = $secret
    $builder['Encrypt'] = $true
    $builder['TrustServerCertificate'] = $true
    $builder['MultipleActiveResultSets'] = $true
    $env:ConnectionStrings__HelloZen = $builder.ConnectionString
}
Remove-Variable secret
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
    Remove-Item Env:ConnectionStrings__HelloZen, Env:Qr__TokenKey, Env:HELLOZEN_NEW_PASSWORD -ErrorAction SilentlyContinue
}

# Puts a new random key (48 bytes, base64) on the clipboard without showing it, for Jwt__SigningKey,
# Qr__TokenKey or Payments__Sandbox__SecretKey. From the repository root:
#   .\deploy\monsterasp\new-key.ps1
# Paste it into MonsterASP, and keep a copy of Qr__TokenKey somewhere safe (a password manager):
# without it the printed QR codes stop working.
$bytes = New-Object byte[] 48
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
[Convert]::ToBase64String($bytes) | Set-Clipboard
Write-Host 'A new key is on the clipboard. Paste it into MonsterASP.'

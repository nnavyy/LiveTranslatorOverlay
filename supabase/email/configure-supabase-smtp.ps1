[CmdletBinding()]
param(
    [string]$ProjectRef = $env:SUPABASE_PROJECT_REF,
    [string]$AccessToken = $env:SUPABASE_ACCESS_TOKEN,
    [string]$ResendApiKey = $env:RESEND_API_KEY,
    [string]$Domain = 'livetranslator.app',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$templatePath = Join-Path $PSScriptRoot 'templates\otp-login.html'
if (!(Test-Path -LiteralPath $templatePath)) {
    throw "Missing OTP template: $templatePath"
}

$otpHtml = [IO.File]::ReadAllText($templatePath, [Text.Encoding]::UTF8)
if ($otpHtml -notmatch '\{\{\s*\.Token\s*\}\}') {
    throw 'OTP template is missing {{ .Token }} placeholder.'
}

$senderEmail = "noreply@$Domain"
$payloadObj = [ordered]@{
    site_url = "https://$Domain"
    smtp_admin_email = $senderEmail
    smtp_sender_name = 'Live Translator Overlay'
    smtp_host = 'smtp.resend.com'
    smtp_port = '465'
    smtp_user = 'resend'
    smtp_max_frequency = 60
    mailer_subjects_magic_link = 'Your Live Translator 6-digit login code'
    mailer_subjects_confirmation = 'Your Live Translator verification code'
    mailer_templates_magic_link_content = $otpHtml
    mailer_templates_confirmation_content = $otpHtml
}

if ($DryRun) {
    $sanitized = [ordered]@{}
    foreach ($k in $payloadObj.Keys) { $sanitized[$k] = $payloadObj[$k] }
    $sanitized['smtp_pass'] = '<REDACTED_RESEND_API_KEY>'
    Write-Output "DRY RUN: Ready to patch Supabase Auth SMTP for domain '$Domain' (sender '$senderEmail', template chars=$($otpHtml.Length))."
    Write-Output ($sanitized | ConvertTo-Json -Depth 4)
    return
}

if ([string]::IsNullOrWhiteSpace($ProjectRef)) { throw 'Set SUPABASE_PROJECT_REF or pass -ProjectRef.' }
if ([string]::IsNullOrWhiteSpace($AccessToken)) { throw 'Set SUPABASE_ACCESS_TOKEN or pass -AccessToken.' }
if ([string]::IsNullOrWhiteSpace($ResendApiKey)) { throw 'Set RESEND_API_KEY or pass -ResendApiKey.' }

$payloadObj['smtp_pass'] = $ResendApiKey
$body = $payloadObj | ConvertTo-Json -Depth 4
$uri = "https://api.supabase.com/v1/projects/$ProjectRef/config/auth"
$headers = @{
    Authorization = "Bearer $AccessToken"
    'Content-Type' = 'application/json'
}

Invoke-RestMethod -Method Patch -Uri $uri -Headers $headers -Body $body | Out-Null
Write-Output "Configured Supabase Auth custom SMTP for '$senderEmail' on project '$ProjectRef'."

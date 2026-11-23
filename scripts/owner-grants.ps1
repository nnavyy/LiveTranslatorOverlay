[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('list', 'grant', 'revoke', 'grant-guest', 'set-beta', 'publish-beta')]
    [string]$Action,
    [ValidateSet('all', 'pending', 'verified', 'cancelled')]
    [string]$Status = 'all',
    [string]$RequestId = '',
    [string]$UserId = '',
    [switch]$GrantBeta,
    [bool]$Active = $true,
    [string]$Note = '',
    [string]$ZipPath = '',
    [ValidateSet('win-x64', 'osx-arm64', 'osx-x64')]
    [string]$Runtime = 'win-x64',
    [string]$Version = '',
    [string]$SupabaseUrl = $env:SUPABASE_URL,
    [string]$ServiceRoleKey = $env:SUPABASE_SERVICE_ROLE_KEY,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

function Invoke-OwnerRpc([string]$RpcName, [System.Collections.Specialized.OrderedDictionary]$Body) {
    $baseUrl = if ([string]::IsNullOrWhiteSpace($SupabaseUrl)) { 'https://YOUR-PROJECT.supabase.co' } else { $SupabaseUrl.TrimEnd('/') }
    $uri = "$baseUrl/rest/v1/rpc/$RpcName"
    $json = $Body | ConvertTo-Json -Depth 6

    if ($DryRun) {
        $preview = [ordered]@{
            action = $Action
            rpc = $RpcName
            endpoint = $uri
            body = $Body
            serviceRoleHeader = '<REDACTED_SUPABASE_SERVICE_ROLE_KEY>'
        }
        Write-Output "DRY RUN: $Action -> $RpcName"
        Write-Output ($preview | ConvertTo-Json -Depth 6)
        return $null
    }

    if ([string]::IsNullOrWhiteSpace($SupabaseUrl)) { throw 'Set SUPABASE_URL or pass -SupabaseUrl.' }
    if ([string]::IsNullOrWhiteSpace($ServiceRoleKey)) { throw 'Set SUPABASE_SERVICE_ROLE_KEY or pass -ServiceRoleKey.' }

    $headers = @{
        apikey = $ServiceRoleKey
        Authorization = "Bearer $ServiceRoleKey"
        'Content-Type' = 'application/json'
    }
    return Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -Body $json
}

switch ($Action) {
    'list' {
        $payload = [ordered]@{
            p_status = $Status
            p_limit = 100
        }
        $res = Invoke-OwnerRpc 'lt_admin_list_requests' $payload
        if ($res -ne $null) { Write-Output ($res | ConvertTo-Json -Depth 8) }
    }
    'grant' {
        if ([string]::IsNullOrWhiteSpace($RequestId)) { throw 'Pass -RequestId <uuid> for -Action grant.' }
        $payload = [ordered]@{
            p_request_id = $RequestId.Trim()
            p_grant_beta = if ($PSBoundParameters.ContainsKey('GrantBeta')) { [bool]$GrantBeta } else { $null }
            p_admin_note = $Note
        }
        $res = Invoke-OwnerRpc 'lt_admin_grant_support' $payload
        if ($res -ne $null) { Write-Output ($res | ConvertTo-Json -Depth 6) }
    }
    'revoke' {
        if ([string]::IsNullOrWhiteSpace($RequestId)) { throw 'Pass -RequestId <uuid> for -Action revoke.' }
        $payload = [ordered]@{
            p_request_id = $RequestId.Trim()
            p_reason = $Note
        }
        $res = Invoke-OwnerRpc 'lt_admin_revoke_support' $payload
        if ($res -ne $null) { Write-Output ($res | ConvertTo-Json -Depth 6) }
    }
    'grant-guest' {
        if ([string]::IsNullOrWhiteSpace($RequestId)) { throw 'Pass -RequestId <uuid> for -Action grant-guest.' }
        $payload = [ordered]@{
            p_request_id = $RequestId.Trim()
            p_admin_note = $Note
        }
        $res = Invoke-OwnerRpc 'lt_admin_grant_guest_donation' $payload
        if ($res -ne $null) { Write-Output ($res | ConvertTo-Json -Depth 6) }
    }
    'set-beta' {
        if ([string]::IsNullOrWhiteSpace($UserId)) { throw 'Pass -UserId <uuid> for -Action set-beta.' }
        $payload = [ordered]@{
            p_user_id = $UserId.Trim()
            p_active = [bool]$Active
            p_note = $Note
        }
        $res = Invoke-OwnerRpc 'lt_admin_set_beta_access' $payload
        if ($res -ne $null) { Write-Output ($res | ConvertTo-Json -Depth 6) }
    }
    'publish-beta' {
        if ([string]::IsNullOrWhiteSpace($Version)) { throw 'Pass -Version X.Y.Z for -Action publish-beta.' }
        if ($Version.Trim() -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must match X.Y.Z.' }
        if ([string]::IsNullOrWhiteSpace($ZipPath)) { throw 'Pass -ZipPath <archive.zip> for -Action publish-beta.' }
        if (!(Test-Path -LiteralPath $ZipPath -PathType Leaf)) { throw "Missing ZipPath: $ZipPath" }

        $zipItem = Get-Item -LiteralPath $ZipPath
        $sha256 = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash
        $archiveName = switch ($Runtime) {
            'osx-arm64' { 'LiveTranslatorOverlay-Mac-osx-arm64.zip' }
            'osx-x64' { 'LiveTranslatorOverlay-Mac-osx-x64.zip' }
            default { 'LiveTranslatorOverlay-Windows-x64.zip' }
        }
        $storagePath = "v$($Version.Trim())/$Runtime/$archiveName"

        if (-not $DryRun) {
            if ([string]::IsNullOrWhiteSpace($SupabaseUrl)) { throw 'Set SUPABASE_URL or pass -SupabaseUrl.' }
            if ([string]::IsNullOrWhiteSpace($ServiceRoleKey)) { throw 'Set SUPABASE_SERVICE_ROLE_KEY or pass -ServiceRoleKey.' }
            $uploadUrl = "$($SupabaseUrl.TrimEnd('/'))/storage/v1/object/beta-releases/$storagePath"
            $uploadHeaders = @{
                apikey = $ServiceRoleKey
                Authorization = "Bearer $ServiceRoleKey"
                'x-upsert' = 'true'
                'Content-Type' = 'application/zip'
            }
            Invoke-RestMethod -Method Post -Uri $uploadUrl -Headers $uploadHeaders -InFile $zipItem.FullName | Out-Null
            Write-Output "Uploaded private beta archive -> $storagePath ($($zipItem.Length) bytes, sha256=$sha256)"
        }

        $payload = [ordered]@{
            p_version = $Version.Trim()
            p_runtime = $Runtime
            p_storage_path = $storagePath
            p_size = [int64]$zipItem.Length
            p_sha256 = $sha256
            p_notes = $Note
        }
        $res = Invoke-OwnerRpc 'lt_admin_register_beta_release' $payload
        if ($res -ne $null) { Write-Output ($res | ConvertTo-Json -Depth 6) }
    }
}

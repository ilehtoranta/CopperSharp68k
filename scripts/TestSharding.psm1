Set-StrictMode -Version Latest

function Get-ExecutionTestCases {
    param([string[]]$Lines)
    $className = 'CopperSharp.Compiler.Tests.CompilerExecutionTests'
    $pattern = '^' + [regex]::Escape($className) + '\.[^(]+'
    foreach ($line in $Lines) {
        $displayName = $line.Trim()
        if ($displayName -notmatch $pattern) { continue }
        $method = $Matches[0]
        $cpu = ''
        if ($displayName -match '(?:\(|, )(?<parameter>target|cpu|cpuTarget): (?<cpu>M68000|M68020|M68040|M68060)(?=,|\))') {
            $cpu = "$($Matches.parameter): $($Matches.cpu)"
        }
        [pscustomobject]@{ DisplayName = $displayName; Method = $method; Cpu = $cpu }
    }
}

function ConvertTo-ExecutionFilterValue {
    param([string]$Value)
    # VSTest FilterHelper.Escape: escape operators, grouping and backslashes.
    [regex]::Replace($Value, '[\\()&|=!~]', '\$0')
}

function Get-ExecutionTestGroups {
    param([object[]]$Cases)
    foreach ($method in ($Cases | Group-Object Method)) {
        $shortName = $method.Name.Substring('CopperSharp.Compiler.Tests.CompilerExecutionTests.'.Length)
        $buckets = if (@($method.Group | Where-Object { -not $_.Cpu }).Count -eq 0) {
            @($method.Group | Group-Object Cpu)
        } else { @([pscustomobject]@{ Name = ''; Group = $method.Group }) }
        foreach ($bucket in $buckets) {
            $baseKey = if ($bucket.Name) { "$shortName|$($bucket.Name)" } else { $shortName }
            if ($bucket.Group.Count -eq 1) {
                $filter = if ($bucket.Name) { "(FullyQualifiedName=$($method.Name)&DisplayName~$($bucket.Name))" }
                    else { "FullyQualifiedName=$($method.Name)" }
                [pscustomobject]@{ Key = $baseKey; TimingFallbackKey = $baseKey; Filter = $filter
                    Cases = @($bucket.Group) }
                continue
            }
            # VSTest compares names with OrdinalIgnoreCase. Keep indistinguishable
            # names together (including abbreviated theory arguments), otherwise
            # an equality filter would select the same rows on multiple workers.
            $byName = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($case in $bucket.Group) {
                if (-not $byName.ContainsKey($case.DisplayName)) {
                    $byName[$case.DisplayName] = [System.Collections.Generic.List[object]]::new()
                }
                $byName[$case.DisplayName].Add($case)
            }
            foreach ($rows in $byName.Values) {
                $ordered = @($rows | Sort-Object DisplayName -Culture en-US -CaseSensitive)
                $name = $ordered[0].DisplayName
                $bytes = [Text.Encoding]::UTF8.GetBytes($name.ToUpperInvariant())
                $id = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
                [pscustomobject]@{
                    Key = "$baseKey|case:$id"; TimingFallbackKey = $baseKey
                    Filter = "(FullyQualifiedName=$($method.Name)&DisplayName=$(ConvertTo-ExecutionFilterValue $name))"
                    Cases = $ordered
                }
            }
        }
    }
}

function New-ExecutionTestPlan {
    param(
        [object[]]$Cases,
        [ValidateRange(1, 64)][int]$ShardCount,
        [ValidateRange(1, 8)][int]$Workers = 1,
        [System.Collections.IDictionary]$TimingProfile
    )
    if ($Cases.Count -eq 0) { throw 'No compiler execution tests were discovered.' }
    $fallback = 1.0
    if ($TimingProfile) {
        if ($TimingProfile.schemaVersion -notin @(1, 2)) { throw 'Unsupported execution timing profile schema.' }
        $fallback = [double]$TimingProfile.fallbackSecondsPerCase
        if (-not [double]::IsFinite($fallback) -or $fallback -le 0) { throw 'Invalid fallback timing.' }
    }
    $groups = @(foreach ($group in (Get-ExecutionTestGroups $Cases)) {
        $seconds = $fallback * $group.Cases.Count
        $timingKey = $group.Key
        if ($TimingProfile -and -not $TimingProfile.groups.Contains($timingKey)) { $timingKey = $group.TimingFallbackKey }
        if ($TimingProfile -and $TimingProfile.groups.Contains($timingKey)) {
            $timing = $TimingProfile.groups[$timingKey]
            if ($timing.Count -ne 2 -or $timing[0] -le 0 -or
                -not [double]::IsFinite([double]$timing[1]) -or $timing[1] -le 0) {
                throw "Invalid timing for $($group.Key)."
            }
            $seconds = [double]$timing[1] / [int]$timing[0] * $group.Cases.Count
        }
        [pscustomobject]@{ Key = $group.Key; Filter = $group.Filter; Cases = $group.Cases; Seconds = $seconds }
    })
    $bins = @(for ($shard = 1; $shard -le $ShardCount; $shard++) {
        for ($worker = 1; $worker -le $Workers; $worker++) {
            [pscustomobject]@{ Shard = $shard; Worker = $worker; EstimatedSeconds = 0.0; CaseCount = 0
                Groups = [System.Collections.Generic.List[object]]::new() }
        }
    })
    # Longest-processing-time first distributes expensive theory rows separately.
    foreach ($group in ($groups | Sort-Object @{ Expression = 'Seconds'; Descending = $true }, Key -Culture en-US)) {
        $target = 0
        for ($index = 1; $index -lt $bins.Count; $index++) {
            if ($bins[$index].EstimatedSeconds -lt $bins[$target].EstimatedSeconds) { $target = $index }
        }
        $bins[$target].Groups.Add($group)
        $bins[$target].EstimatedSeconds += $group.Seconds
        $bins[$target].CaseCount += $group.Cases.Count
    }
    $bins
}

function Get-ExecutionTestFilters {
    param([object[]]$Groups, [int]$MaximumLength = 20000)
    $batch = ''
    foreach ($group in ($Groups | Sort-Object Key -Culture en-US)) {
        $selection = $group.Filter
        if ($selection.Length -gt $MaximumLength) { throw 'A test group exceeds the native filter length limit.' }
        if ($batch.Length -ne 0 -and $batch.Length + 1 + $selection.Length -gt $MaximumLength) {
            $batch
            $batch = ''
        }
        $batch = if ($batch.Length -eq 0) { $selection } else { "$batch|$selection" }
    }
    if ($batch.Length -ne 0) { $batch }
}

function Assert-ExecutionTestCoverage {
    param([string[]]$Expected, [object[]]$Results)
    # xUnit can abbreviate multiple long theory arguments to the same display
    # name. Compare multiplicities, not a set that silently drops duplicates.
    $remaining = [System.Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    foreach ($name in $Expected) {
        if (-not $remaining.ContainsKey($name)) { $remaining[$name] = 0 }
        $remaining[$name]++
    }
    foreach ($result in $Results) {
        if (-not $remaining.ContainsKey($result.Name) -or $remaining[$result.Name] -eq 0) {
            throw "Unexpected or duplicate execution result: $($result.Name)"
        }
        if ($result.Outcome -notin @('Passed', 'Failed')) {
            throw "Execution case did not run ($($result.Outcome)): $($result.Name)"
        }
        $remaining[$result.Name]--
    }
    $missing = @($remaining.Keys | Where-Object { $remaining[$_] -ne 0 })
    if ($missing.Count -ne 0) { throw "Missing execution results: $($missing -join '; ')" }
    if ($Results.Count -ne $Expected.Count) { throw 'Execution result count does not match discovery.' }
}

Export-ModuleMember -Function Get-ExecutionTestCases, Get-ExecutionTestGroups, New-ExecutionTestPlan, Get-ExecutionTestFilters, Assert-ExecutionTestCoverage, ConvertTo-ExecutionFilterValue

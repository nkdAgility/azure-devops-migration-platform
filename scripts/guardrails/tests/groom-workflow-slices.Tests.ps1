#Requires -Modules Pester
# Martin, 2026-11-16: "Delivery slices are only valid when they are complete vertical slices of work
# that can be shipped to production and produce value." #169's first pre-start proposal split one
# piece of work into three phase slices. The groom workflow must refuse to create such slices.

BeforeAll {
    $script:workflow = Resolve-Path (Join-Path $PSScriptRoot '..\..\..\.agents\workflows\nkda-backlog-groom-workflow.js')
    function Invoke-Groom([object[]]$slices) {
        $harness = Join-Path $TestDrive "harness-$([guid]::NewGuid()).mjs"
        $assessed = @{ issue = 169; verdict = 'too-big'; reasons = @('too big'); areas = @(); applied = $false; slices = $slices } | ConvertTo-Json -Depth 10 -Compress
        $src = (Get-Content $workflow -Raw) -replace '(?s)^export const meta = \{.*?\n\}\r?\n', ''
        @"
const calls = []
globalThis.args = { mode: 'apply', issues: [169] }
globalThis.phase = () => {}
globalThis.log = (m) => calls.push('log:' + m)
globalThis.agent = async (prompt, o) => { calls.push(o.label); return o.label.startsWith('apply:') ? { issue: 169, verdict: 'too-big', reasons: [], areas: [], applied: true, createdIssues: [] } : (o.label.startsWith('assess:') ? $assessed : null) }
globalThis.pipeline = async (items, ...stages) => Promise.all(items.map(async i => { let v = i; for (const s of stages) v = await s(v); return v }))
globalThis.parallel = async (fns) => Promise.all(fns.map(f => f()))
const result = await (async () => {
$src
})()
console.log(JSON.stringify({ calls, result }))
"@ | Set-Content $harness -Encoding utf8
        $out = & node $harness 2>&1
        if ($LASTEXITCODE -ne 0) { throw "node failed: $out" }
        ($out | Select-Object -Last 1) | ConvertFrom-Json
    }
    $script:good = @{
        title = 'Export work items from TFS to the package, then import them to Azure DevOps'; acceptanceCriteria = @('a')
        operatorValue = 'An operator migrates TFS work items to Azure DevOps with one config'; shipsAlone = $true; dependsOnSlices = @()
        liveScenarios = @{ restToRest = 'scenarios/a.json'; objectModelToRest = 'scenarios/b.json' }
    }
}

Describe 'Groom workflow slice check' {
    It 'refuses #169-style phase slices and creates nothing' {
        $phaseSlices = @(
            @{ title = 'CLI splits the job into Export and Import children'; acceptanceCriteria = @('a'); shipsAlone = $false; dependsOnSlices = @() }
            @{ title = 'Control Plane holds the Import child'; acceptanceCriteria = @('b'); dependsOnSlices = @('CLI splits the job into Export and Import children') }
            @{ title = 'Live E2E for the parent job'; acceptanceCriteria = @('c'); operatorValue = ''; dependsOnSlices = @('Control Plane holds the Import child') }
        )
        $r = Invoke-Groom $phaseSlices
        ($r.calls | Where-Object { $_ -like 'apply:*' }) | Should -BeNullOrEmpty
        ($r.calls -join "`n") | Should -Match 'not shippable alone'
    }
    It 'applies a breakdown whose every slice ships alone with value and two scenarios' {
        $r = Invoke-Groom @($good, ($good.Clone()))
        ($r.calls | Where-Object { $_ -like 'apply:*' }) | Should -Not -BeNullOrEmpty
    }
    It 'refuses a slice missing either live scenario' {
        $bad = $good.Clone(); $bad.liveScenarios = @{ restToRest = 'scenarios/a.json' }
        $r = Invoke-Groom @($good, $bad)
        ($r.calls | Where-Object { $_ -like 'apply:*' }) | Should -BeNullOrEmpty
    }
}

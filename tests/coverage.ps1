#!/usr/bin/env pwsh
# Run the test suite with coverage and open an HTML report.
# Prereq (one-time): dotnet tool install --global dotnet-reportgenerator-globaltool
# Note: stop the AppHost first — running services lock their build output.

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo

Remove-Item -Recurse -Force coverage -ErrorAction SilentlyContinue

dotnet test ExamApp.slnx --nologo `
  --collect:"XPlat Code Coverage" `
  --settings tests/coverage.runsettings `
  --results-directory coverage

reportgenerator `
  -reports:"coverage/**/coverage.cobertura.xml" `
  -targetdir:coverage/report `
  -reporttypes:"TextSummary;Html" `
  -settings:MergeExecutionRuns=true

Write-Host ""
Get-Content coverage/report/Summary.txt
Write-Host ""
Write-Host "HTML report: coverage/report/index.html"
Invoke-Item coverage/report/index.html

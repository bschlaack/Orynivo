# Check every supported desktop, website and mobile locale.
& node "$PSScriptRoot/verify-localization.cjs"
exit $LASTEXITCODE

# Windows PowerShell 5.1+. Separate GPU research environment; does not start training.
[CmdletBinding()]
param([string]$ResearchRoot)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ResearchRoot)) {
    $ResearchRoot = Join-Path $PSScriptRoot '..\..\..\offline-research'
}
$r = (Resolve-Path -LiteralPath $ResearchRoot).Path
$bootstrap = Join-Path $r 'training-venv\Scripts\python.exe'
$environment = Join-Path $r 'training-cuda-venv'
$py = Join-Path $environment 'Scripts\python.exe'
if (!(Test-Path -LiteralPath $py)) {
    & $bootstrap -m venv $environment
    if ($LASTEXITCODE) { throw 'GPU environment creation failed.' }
}
& $py -m pip install torch==2.10.0 --index-url https://download.pytorch.org/whl/cu126
if ($LASTEXITCODE) { throw 'CUDA PyTorch installation failed.' }
& $py -m pip install -r "$PSScriptRoot/requirements-cuda.txt"
if ($LASTEXITCODE) { throw 'Training dependency installation failed.' }
& $py -c "import torch; assert torch.cuda.is_available(), 'CUDA unavailable: check the NVIDIA driver and this Python environment'; print('GPU:',torch.cuda.get_device_name(0)); print('Torch:',torch.__version__); x=torch.ones(16,device='cuda'); print('GPU calculation:',(x*x).sum().item())"
if ($LASTEXITCODE) { throw 'GPU verification failed. Training has not started.' }
Write-Host 'GPU environment ready. Start train-small-refine.ps1 when ready.'

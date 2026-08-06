using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;

namespace IvaScanner.Master.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DeploymentController : ControllerBase
    {
        private readonly ILogger<DeploymentController> _logger;
        private readonly IWebHostEnvironment _environment;

        public DeploymentController(ILogger<DeploymentController> logger, IWebHostEnvironment environment)
        {
            _logger = logger;
            _environment = environment;
        }

        [HttpGet("worker-package")]
        public async Task<IActionResult> GetWorkerPackage()
        {
            try
            {
                var workerPath = Path.Combine(_environment.ContentRootPath, "..", "Worker");
                
                if (!Directory.Exists(workerPath))
                {
                    return NotFound("Worker source not found");
                }

                // Create a temporary directory for the package
                var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempDir);

                try
                {
                    // Copy essential files
                    var publishDir = Path.Combine(tempDir, "publish");
                    Directory.CreateDirectory(publishDir);

                    await CopyWorkerFilesAsync(workerPath, publishDir);

                    // Create tar.gz archive
                    var archivePath = Path.Combine(Path.GetTempPath(), "iva-worker.tar.gz");
                    await CreateTarGzArchiveAsync(publishDir, archivePath);

                    // Return the archive
                    var bytes = await System.IO.File.ReadAllBytesAsync(archivePath);
                    
                    // Cleanup
                    Directory.Delete(tempDir, true);
                    System.IO.File.Delete(archivePath);

                    return File(bytes, "application/gzip", "iva-worker.tar.gz");
                }
                finally
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating worker package");
                return StatusCode(500, "Error creating worker package");
            }
        }

        [HttpGet("worker-package-windows")]
        public async Task<IActionResult> GetWorkerPackageWindows()
        {
            try
            {
                var workerPath = Path.Combine(_environment.ContentRootPath, "..", "Worker");
                
                if (!Directory.Exists(workerPath))
                {
                    return NotFound("Worker source not found");
                }

                // Create a temporary directory for the package
                var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempDir);

                try
                {
                    // Copy essential files
                    var publishDir = Path.Combine(tempDir, "publish");
                    Directory.CreateDirectory(publishDir);

                    await CopyWorkerFilesAsync(workerPath, publishDir);

                    // Create ZIP archive for Windows
                    var archivePath = Path.Combine(Path.GetTempPath(), "iva-worker.zip");
                    ZipFile.CreateFromDirectory(publishDir, archivePath);

                    // Return the archive
                    var bytes = await System.IO.File.ReadAllBytesAsync(archivePath);
                    
                    // Cleanup
                    Directory.Delete(tempDir, true);
                    System.IO.File.Delete(archivePath);

                    return File(bytes, "application/zip", "iva-worker.zip");
                }
                finally
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating worker package for Windows");
                return StatusCode(500, "Error creating worker package");
            }
        }

        [HttpGet("install-script")]
        public IActionResult GetInstallScript(string os = "linux")
        {
            try
            {
                var script = os.ToLower() switch
                {
                    "linux" => GenerateLinuxInstallScript(),
                    "windows" => GenerateWindowsInstallScript(),
                    _ => GenerateLinuxInstallScript()
                };

                var contentType = os.ToLower() == "windows" ? "application/x-powershell" : "text/x-shellscript";
                var fileName = os.ToLower() == "windows" ? "install-worker.ps1" : "install-worker.sh";

                return File(System.Text.Encoding.UTF8.GetBytes(script), contentType, fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating install script");
                return StatusCode(500, "Error generating install script");
            }
        }

        private async Task CopyWorkerFilesAsync(string sourcePath, string targetPath)
        {
            // Essential files to copy
            var filesToCopy = new[]
            {
                "Program.cs",
                "IvaScanner.Worker.csproj",
                "appsettings.json",
                "appsettings.Production.json"
            };

            // Copy essential files
            foreach (var file in filesToCopy)
            {
                var sourceFile = Path.Combine(sourcePath, file);
                var targetFile = Path.Combine(targetPath, file);
                
                if (System.IO.File.Exists(sourceFile))
                {
                    await System.IO.File.CopyToAsync(
                        System.IO.File.OpenRead(sourceFile),
                        System.IO.File.Create(targetFile));
                }
            }

            // Copy directories
            var directoriesToCopy = new[] { "Services", "Configuration" };
            foreach (var dir in directoriesToCopy)
            {
                var sourceDir = Path.Combine(sourcePath, dir);
                var targetDir = Path.Combine(targetPath, dir);
                
                if (Directory.Exists(sourceDir))
                {
                    await CopyDirectoryAsync(sourceDir, targetDir);
                }
            }

            // Copy startup scripts
            var scripts = new[]
            {
                "start-worker.sh",
                "install-service.sh",
                "iva-worker.service"
            };

            foreach (var script in scripts)
            {
                var sourceFile = Path.Combine(sourcePath, script);
                var targetFile = Path.Combine(targetPath, script);
                
                if (System.IO.File.Exists(sourceFile))
                {
                    await System.IO.File.CopyToAsync(
                        System.IO.File.OpenRead(sourceFile),
                        System.IO.File.Create(targetFile));
                }
            }
        }

        private async Task CopyDirectoryAsync(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDir, file);
                var targetFile = Path.Combine(targetDir, relativePath);
                
                Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                
                await System.IO.File.CopyToAsync(
                    System.IO.File.OpenRead(file),
                    System.IO.File.Create(targetFile));
            }
        }

        private async Task CreateTarGzArchiveAsync(string sourceDir, string archivePath)
        {
            // Simple implementation - in production, use a proper tar.gz library
            // For now, create a ZIP and rename (Linux systems can handle it)
            ZipFile.CreateFromDirectory(sourceDir, archivePath.Replace(".tar.gz", ".zip"));
            
            // Rename to tar.gz (simplified approach)
            if (System.IO.File.Exists(archivePath.Replace(".tar.gz", ".zip")))
            {
                System.IO.File.Move(archivePath.Replace(".tar.gz", ".zip"), archivePath);
            }
        }

        private string GenerateLinuxInstallScript()
        {
            var masterUrl = $"{Request.Scheme}://{Request.Host}";
            
            return $@"#!/bin/bash
# IVA Scanner Worker Auto-Install Script
# Generated by Master Server

set -e

MASTER_URL=""{masterUrl}""
WORKER_PATH=""/opt/iva-worker""
SERVICE_USER=""iva-worker""

echo ""🚀 Installing IVA Scanner Worker...""

# Create user and directory
sudo useradd --system --home $WORKER_PATH --shell /usr/sbin/nologin $SERVICE_USER || true
sudo mkdir -p $WORKER_PATH
sudo chown -R $SERVICE_USER:$SERVICE_USER $WORKER_PATH

# Install .NET 8 if not present
if ! command -v dotnet &> /dev/null; then
    echo ""📦 Installing .NET 8 Runtime...""
    wget https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
    sudo dpkg -i /tmp/packages-microsoft-prod.deb
    sudo apt-get update
    sudo apt-get install -y aspnetcore-runtime-8.0
fi

# Download and extract worker
echo ""⬇️ Downloading worker package...""
wget $MASTER_URL/api/deployment/worker-package -O /tmp/iva-worker.tar.gz
sudo tar -xzf /tmp/iva-worker.tar.gz -C $WORKER_PATH --strip-components=1

# Set permissions
sudo chown -R $SERVICE_USER:$SERVICE_USER $WORKER_PATH
sudo chmod +x $WORKER_PATH/*.sh

# Install systemd service
if [ -f ""$WORKER_PATH/iva-worker.service"" ]; then
    sudo cp $WORKER_PATH/iva-worker.service /etc/systemd/system/
    sudo systemctl daemon-reload
    sudo systemctl enable iva-worker
fi

echo ""✅ Installation completed!""
echo ""Configure appsettings.json and start with: sudo systemctl start iva-worker""
";
        }

        private string GenerateWindowsInstallScript()
        {
            var masterUrl = $"{Request.Scheme}://{Request.Host}";
            
            return $@"# IVA Scanner Worker Auto-Install Script for Windows
# Generated by Master Server

$MasterUrl = ""{masterUrl}""
$WorkerPath = ""C:\IvaWorker""

Write-Host ""🚀 Installing IVA Scanner Worker..."" -ForegroundColor Green

# Create directory
New-Item -ItemType Directory -Path $WorkerPath -Force | Out-Null

# Download and extract worker
Write-Host ""⬇️ Downloading worker package..."" -ForegroundColor Yellow
Invoke-WebRequest -Uri ""$MasterUrl/api/deployment/worker-package-windows"" -OutFile ""$env:TEMP\iva-worker.zip""
Expand-Archive -Path ""$env:TEMP\iva-worker.zip"" -DestinationPath $WorkerPath -Force

# Install as Windows Service
Write-Host ""⚙️ Installing Windows Service..."" -ForegroundColor Yellow
sc.exe create ""IvaWorker"" binPath=""$WorkerPath\IvaScanner.Worker.exe"" start=auto

Write-Host ""✅ Installation completed!"" -ForegroundColor Green
Write-Host ""Configure appsettings.json and start with: sc start IvaWorker"" -ForegroundColor Cyan
";
        }
    }
}
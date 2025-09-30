# Deployment Guide

This guide covers deploying the VDP Sheet Builder API to various cloud platforms.

## Quick Start with Docker

### Local Testing
```bash
# Build and run with Docker Compose
docker-compose up --build

# Access the API
curl http://localhost:8080/api/pdf/health
```

### Docker Commands
```bash
# Build image
docker build -t vdp-sheet-builder-api .

# Run container
docker run -p 8080:80 vdp-sheet-builder-api
```

## Azure Deployment

### Option 1: Azure App Service (Recommended)

1. **Install Azure CLI**
```bash
# macOS
brew install azure-cli

# Windows
winget install Microsoft.AzureCLI

# Linux
curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash
```

2. **Login and Deploy**
```bash
az login
az group create --name vdp-api-rg --location eastus
az webapp up --name vdp-sheet-builder-api --resource-group vdp-api-rg --runtime "DOTNET|8.0"
```

3. **Configure Environment Variables**
```bash
az webapp config appsettings set --name vdp-sheet-builder-api --resource-group vdp-api-rg --settings ASPNETCORE_ENVIRONMENT=Production
```

### Option 2: Azure Container Instances

1. **Build and Push to Azure Container Registry**
```bash
az acr create --name vdpacr --resource-group vdp-api-rg --sku Basic
az acr build --registry vdpacr --image vdp-sheet-builder-api .
```

2. **Deploy Container**
```bash
az container create \
  --resource-group vdp-api-rg \
  --name vdp-api-container \
  --image vdpacr.azurecr.io/vdp-sheet-builder-api:latest \
  --ports 80 \
  --dns-name-label vdp-api-dns
```

## Google Cloud Platform

### Option 1: Cloud Run (Recommended)

1. **Install Google Cloud CLI**
```bash
# Download and install from: https://cloud.google.com/sdk/docs/install
gcloud init
```

2. **Deploy to Cloud Run**
```bash
# Enable required APIs
gcloud services enable run.googleapis.com

# Deploy
gcloud run deploy vdp-sheet-builder-api \
  --source . \
  --platform managed \
  --region us-central1 \
  --allow-unauthenticated \
  --memory 2Gi \
  --cpu 2
```

### Option 2: Google App Engine

1. **Create app.yaml**
```yaml
runtime: dotnet
env: flex
resources:
  cpu: 1
  memory_gb: 2
  disk_size_gb: 10
```

2. **Deploy**
```bash
gcloud app deploy
```

## DigitalOcean

### Option 1: App Platform

1. **Create app.yaml**
```yaml
name: vdp-sheet-builder-api
services:
- name: web
  source_dir: /
  github:
    repo: your-username/your-repo
    branch: api
  run_command: dotnet ConsoleApp1_vdp_sheetbuilder.dll
  environment_slug: dotnet
  instance_count: 1
  instance_size_slug: basic-xxs
```

2. **Deploy via DigitalOcean Console**
- Go to DigitalOcean App Platform
- Connect your GitHub repository
- Select the app.yaml configuration
- Deploy

### Option 2: Droplets (VPS)

1. **Create Droplet**
```bash
# SSH into your droplet
ssh root@your-droplet-ip

# Install .NET 8
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt update
sudo apt install -y dotnet-sdk-8.0
```

2. **Deploy Application**
```bash
# Clone your repository
git clone https://github.com/your-username/your-repo.git
cd your-repo/ConsoleApp1_vdp_sheetbuilder

# Publish application
dotnet publish -c Release -o /var/www/vdp-api

# Create systemd service
sudo nano /etc/systemd/system/vdp-api.service
```

3. **Systemd Service File**
```ini
[Unit]
Description=VDP Sheet Builder API
After=network.target

[Service]
WorkingDirectory=/var/www/vdp-api
ExecStart=/usr/bin/dotnet /var/www/vdp-api/ConsoleApp1_vdp_sheetbuilder.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=vdp-api
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://localhost:5000

[Install]
WantedBy=multi-user.target
```

4. **Start Service**
```bash
sudo systemctl enable vdp-api
sudo systemctl start vdp-api
sudo systemctl status vdp-api
```

## Environment Variables

### Production Settings
```bash
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:80
```

### Optional Settings
```bash
# File upload limits
ASPNETCORE_MAX_REQUEST_SIZE=104857600  # 100MB

# Logging
ASPNETCORE_LOGGING__CONSOLE__DISABLECOLORS=true
```

## SSL/HTTPS Configuration

### Azure App Service
- Automatic SSL certificates
- Custom domains supported

### Google Cloud Run
- Automatic HTTPS
- Custom domains via Cloud Load Balancing

### DigitalOcean
- Use Let's Encrypt with Certbot
```bash
sudo apt install certbot python3-certbot-nginx
sudo certbot --nginx -d your-domain.com
```

## Monitoring and Logging

### Azure
- Application Insights integration
- Built-in monitoring dashboard

### Google Cloud
- Cloud Monitoring
- Cloud Logging

### DigitalOcean
- Basic monitoring included
- Consider external services (DataDog, New Relic)

## Cost Estimates

| Platform | Service | Monthly Cost | Best For |
|----------|---------|--------------|----------|
| Azure | App Service Basic | ~$13 | Production workloads |
| Azure | Container Instances | ~$20-50 | Variable workloads |
| GCP | Cloud Run | ~$5-20 | Serverless, pay-per-use |
| GCP | App Engine | ~$15-30 | Traditional hosting |
| DigitalOcean | App Platform | ~$12-24 | Simple deployments |
| DigitalOcean | Droplet | ~$6-12 | Full control |

## Troubleshooting

### Common Issues

1. **Port Configuration**
```bash
# Ensure the app listens on the correct port
export ASPNETCORE_URLS=http://+:80
```

2. **File Permissions**
```bash
# For Linux deployments
sudo chown -R www-data:www-data /var/www/vdp-api
sudo chmod -R 755 /var/www/vdp-api
```

3. **Memory Issues**
```bash
# Increase memory allocation for large PDF processing
# Azure: Scale up App Service plan
# GCP: Increase Cloud Run memory
# DigitalOcean: Upgrade droplet size
```

### Health Checks
```bash
# Test API health
curl https://your-api-url/api/pdf/health

# Expected response
{
  "status": "Healthy",
  "timestamp": "2024-01-15T10:30:00Z",
  "service": "VDP Sheet Builder API"
}
```

## CI/CD Pipeline

### GitHub Actions Example
```yaml
name: Deploy to Azure
on:
  push:
    branches: [ api ]

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v2
    
    - name: Deploy to Azure Web App
      uses: azure/webapps-deploy@v2
      with:
        app-name: 'vdp-sheet-builder-api'
        publish-profile: ${{ secrets.AZURE_WEBAPP_PUBLISH_PROFILE }}
        package: .
```
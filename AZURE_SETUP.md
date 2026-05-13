# Azure Setup Guide — Pokémon Draft App

This guide walks you through setting up everything in Azure so the GitHub Actions
workflow can build and deploy your app automatically.

---

## Prerequisites

- An Azure account with credits (portal.azure.com)
- Azure CLI installed: https://learn.microsoft.com/en-us/cli/azure/install-azure-cli-windows
- Your code pushed to GitHub

---

## Step 1 — Find your Subscription ID

1. Go to **portal.azure.com**
2. Search for **"Subscriptions"** in the top search bar
3. Click your subscription
4. Copy the **Subscription ID** — you'll need it later

---

## Step 2 — Create a Resource Group

A resource group is a folder that holds all your Azure resources together.

**Via portal:**
1. Search **"Resource groups"** → click **+ Create**
2. Fill in:
   - **Subscription**: your subscription
   - **Resource group name**: `pokemon-draft-rg`
   - **Region**: `East US` (or closest to you)
3. Click **Review + create** → **Create**

---

## Step 3 — Create a Container Registry (ACR)

This is where your Docker image gets stored.

**Via portal:**
1. Search **"Container registries"** → click **+ Create**
2. Fill in:
   - **Resource group**: `pokemon-draft-rg`
   - **Registry name**: something globally unique, e.g. `pokemondraftacr`
   - **Location**: same as your resource group
   - **Pricing plan**: `Basic`
3. Click **Review + create** → **Create**

**Enable Admin access (needed for GitHub Actions):**
1. Open your new registry
2. Go to **Settings → Access keys**
3. Toggle **Admin user** to **Enabled**
4. Copy the **Login server**, **Username**, and one of the **Passwords** — you'll need these for GitHub secrets

---

## Step 4 — Create an Azure SQL Database

**Via portal:**
1. Search **"SQL databases"** → click **+ Create**
2. Fill in:
   - **Resource group**: `pokemon-draft-rg`
   - **Database name**: `pokemon-draft`
   - **Server**: click **Create new**
     - Server name: something unique, e.g. `pokemon-draft-sql`
     - Location: same region as above
     - Authentication: **SQL authentication**
     - Admin login: pick a username (e.g. `sqladmin`)
     - Password: pick a strong password and save it
   - **Compute + storage**: click **Configure** → choose **Basic** (cheapest, ~$5/month)
3. Click **Review + create** → **Create**

**Allow Azure services to connect:**
1. Open your new SQL server (not the database)
2. Go to **Security → Networking**
3. Under **Firewall rules**, enable **"Allow Azure services and resources to access this server"**
4. Click **Save**

**Get your connection string:**
1. Open the SQL **database** (not the server)
2. Go to **Settings → Connection strings**
3. Copy the **ADO.NET** string — it looks like:
   ```
   Server=tcp:pokemon-draft-sql.database.windows.net,1433;Initial Catalog=pokemon-draft;Persist Security Info=False;User ID=sqladmin;Password=<your-password>;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;
   ```
4. Replace `<your-password>` with the password you chose — keep this safe, you'll add it as a GitHub secret.

---

## Step 5 — Create a Container Apps Environment

This is the hosting platform that runs your container.

**Via portal:**
1. Search **"Container Apps Environments"** → click **+ Create**
2. Fill in:
   - **Resource group**: `pokemon-draft-rg`
   - **Name**: `pokemon-draft-env`
   - **Region**: same as above
3. Click **Review + create** → **Create**

---

## Step 6 — Create the Container App

**Via portal:**
1. Search **"Container Apps"** → click **+ Create**
2. **Basics tab:**
   - **Resource group**: `pokemon-draft-rg`
   - **Container app name**: `pokemon-draft`
   - **Region**: same as above
   - **Container Apps Environment**: `pokemon-draft-env`
3. **Container tab:**
   - Uncheck **"Use quickstart image"**
   - **Name**: `pokemon-draft`
   - **Image source**: Azure Container Registry
   - **Registry**: your ACR (`pokemondraftacr`)
   - **Image**: `pokemon-draft` (you can put `latest` for now — GitHub Actions will update it)
   - **Tag**: `latest`
   - **CPU and Memory**: `0.25 CPU, 0.5 Gi` (cheapest)
4. **Environment variables** (click + Add for each):
   | Name | Value |
   |---|---|
   | `ASPNETCORE_ENVIRONMENT` | `Production` |
   | `ConnectionStrings__DefaultConnection` | your Azure SQL connection string from Step 4 |
5. **Ingress tab:**
   - **Ingress**: Enabled
   - **Ingress traffic**: Accepting traffic from anywhere
   - **Target port**: `8080`
6. Click **Review + create** → **Create**

---

## Step 7 — Set Up GitHub Secrets

Go to your GitHub repo → **Settings → Secrets and variables → Actions → New repository secret**

Add these secrets:

| Secret name | Where to get the value |
|---|---|
| `AZURE_CREDENTIALS` | See below |
| `ACR_NAME` | Your registry name, e.g. `pokemondraftacr` |
| `ACR_PASSWORD` | From Step 3 → Access keys → Password |
| `RESOURCE_GROUP` | `pokemon-draft-rg` |

### Generating `AZURE_CREDENTIALS`

Run this in your terminal (replace `<SUBSCRIPTION_ID>` with the ID from Step 1):

```powershell
az ad sp create-for-rbac `
  --name "pokemon-draft-deploy" `
  --role contributor `
  --scopes /subscriptions/<SUBSCRIPTION_ID>/resourceGroups/pokemon-draft-rg `
  --sdk-auth
```

It outputs a block of JSON like this — paste the **entire thing** as the `AZURE_CREDENTIALS` secret:

```json
{
  "clientId": "...",
  "clientSecret": "...",
  "subscriptionId": "...",
  "tenantId": "...",
  ...
}
```

---

## Step 8 — Deploy

Push to `main` (or trigger manually in GitHub → Actions tab) and the workflow will:

1. Build your Docker image in Azure (no local Docker needed)
2. Push it to your container registry
3. Deploy it to your Container App

Your app URL appears in the Azure portal under your Container App → **Overview → Application URL**.

---

## Cost Summary

| Resource | Tier | Est. monthly cost |
|---|---|---|
| Container App | Consumption (scale to zero) | $0–5 |
| Container Registry | Basic | ~$5 |
| Azure SQL Database | Basic (5 DTU) | ~$5 |
| **Total** | | **~$10–15/month** |

Well within your $50 credit budget.

---

## Useful CLI Commands

```powershell
# View live logs
az containerapp logs show --name pokemon-draft --resource-group pokemon-draft-rg --follow

# Manually redeploy latest image
az containerapp update --name pokemon-draft --resource-group pokemon-draft-rg --image pokemondraftacr.azurecr.io/pokemon-draft:latest

# Get your app URL
az containerapp show --name pokemon-draft --resource-group pokemon-draft-rg --query properties.configuration.ingress.fqdn -o tsv
```

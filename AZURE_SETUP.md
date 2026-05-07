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

## Step 4 — Create a Storage Account (for SQLite)

SQLite needs a persistent disk so data survives restarts.

**Via portal:**
1. Search **"Storage accounts"** → click **+ Create**
2. Fill in:
   - **Resource group**: `pokemon-draft-rg`
   - **Storage account name**: something unique, e.g. `pokemondraftsa`
   - **Region**: same as above
   - **Performance**: `Standard`
   - **Redundancy**: `LRS` (cheapest)
3. Click **Review + create** → **Create**

**Create a file share:**
1. Open your storage account
2. Go to **Data storage → File shares** → **+ File share**
3. Name it `draftsqlite`, quota `1` GB → **Create**

**Copy your storage key:**
1. Go to **Security + networking → Access keys**
2. Click **Show** next to key1 and copy it

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

**Mount the file share (so SQLite has persistent storage):**

This part requires the CLI. Open a terminal and run:

```powershell
az login

az containerapp env storage set `
  --name pokemon-draft-env `
  --resource-group pokemon-draft-rg `
  --storage-name sqlitedata `
  --azure-file-account-name <YOUR_STORAGE_ACCOUNT_NAME> `
  --azure-file-account-key <YOUR_STORAGE_KEY> `
  --azure-file-share-name draftsqlite `
  --access-mode ReadWrite
```

Replace `<YOUR_STORAGE_ACCOUNT_NAME>` and `<YOUR_STORAGE_KEY>` with the values from Step 4.

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
   | `ConnectionStrings__DefaultConnection` | `Data Source=/data/draft.db` |
5. **Volumes tab:**
   - Click **+ Add** → Volume type: **Azure file volume**
   - Storage name: `sqlitedata`
   - Mount path: `/data`
6. **Ingress tab:**
   - **Ingress**: Enabled
   - **Ingress traffic**: Accepting traffic from anywhere
   - **Target port**: `8080`
7. Click **Review + create** → **Create**

---

## Step 7 — Set Up GitHub Secrets

Go to your GitHub repo → **Settings → Secrets and variables → Actions → New repository secret**

Add these 4 secrets:

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
| Storage Account | Standard LRS | ~$1 |
| **Total** | | **~$6–11/month** |

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

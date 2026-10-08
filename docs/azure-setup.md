# Azure setup for Bookmarks (Phase 3)

This guide walks you through the one-time Azure setup, click by click. **Everything is done in a web browser**, so there's no Azure CLI to install. Plan on about an hour. One step (creating the user tenant) runs in the background for up to 30 minutes, and the order below lets you keep working while it does.

**Nothing you create here is a password or secret.** You'll copy some IDs (long strings like `3f2a…-…`) and send them to Claude. They're safe to share and will end up in the repo's config files.

> **If anything on screen doesn't match this guide** (a button has a different name, a page looks different, an error appears): stop, take a screenshot and send it to Claude. Microsoft moves things around in the portal fairly often. Guessing is how mistakes happen, and asking is quick.

---

## Before you start: five words you'll see

| Word | What it means here |
|---|---|
| **Tenant** (or "directory") | A separate, self-contained set of user accounts and app registrations. You'll work in **two**: your existing one, and a new one just for Bookmarks users. |
| **Subscription** | Your Azure billing account. Everything that costs money belongs to it. |
| **Resource group** | A folder for Azure resources. Deleting the folder deletes everything in it. |
| **App registration** | Tells Microsoft's sign-in service that an app exists and what it's allowed to do. It holds no code. |
| **Client ID / Application ID** | The ID of an app registration (a GUID). You'll copy several of these. |

## The one rule that prevents most mistakes: check which tenant you're in

You'll work in two tenants:

- **Your normal directory** (probably named *Default Directory*): used for **Part A**.
- **The new *Bookmarks* tenant**, which you create in Part A: used for **Part B**.

**How to check:** in the [Entra admin center](https://entra.microsoft.com), look at the **top-right corner** under your name. The small grey text is the tenant you're in right now.

**How to switch:** click the **gear icon ⚙ (Settings)** at the top → **Directories + subscriptions** → find the tenant in the list → **Switch**.

Every step below starts with a 📍 line saying which tenant it needs. **Check the top-right corner before you start each step.**

## Keep a notes file open

Open Notepad and paste this in. You'll fill it in as you go, then send the finished block to Claude:

```
EXT_TENANT_ID         =
EXT_SUBDOMAIN         =
API_CLIENT_ID         =
SPA_CLIENT_ID         =
AZURE_TENANT_ID       =
AZURE_SUBSCRIPTION_ID =
AZURE_CLIENT_ID       =
SQL_ADMIN_GROUP_ID    =
```

Tip: every ID in the portal has a small **copy icon** 📋 next to it. Use it instead of selecting text by hand, because it's easy to miss a character.

---

# Part A: your normal directory

## Step 1. Start creating the Bookmarks tenant (it runs in the background)

📍 **Your normal directory**

This creates the separate tenant where Bookmarks users' accounts will live. It takes up to 30 minutes, so start it first.

1. Open the [Entra admin center](https://entra.microsoft.com) and sign in with your usual Microsoft account.
2. In the left menu: **Entra ID → Overview**. At the top of the page, click **Manage tenants**.
3. Click **+ Create**.
4. Choose **External** → **Continue**.
5. If asked, choose **Use Azure Subscription**. **Not** the 30-day free trial, which expires and gets deleted.
6. **Basics** tab:
   - **Tenant name:** `Bookmarks`
   - **Domain name:** type something like `bookmarksapp`. If it says it's taken, try `bookmarkslibrary`, `mybookmarksapp`, and so on.
     ⚠️ **This can never be changed.** It becomes part of the sign-in web address (`bookmarksapp.ciamlogin.com`), so pick something you're happy for users to see.
   - **Location / Country:** United States (also permanent).
7. **Add a subscription** tab (you may need to click **Next**):
   - **Subscription:** your subscription.
   - **Resource group:** click **Create new** → name it `rg-bookmarks-identity` → OK.
   - **Resource group location:** any US region (e.g. West US 3).
8. **Review + create** → **Create**.

✅ **You'll know it worked when:** a notification (bell icon 🔔, top right) says the tenant is being created. **Don't wait for it.** Carry on with Step 2.

❌ **Common mistakes**
- Picking the free trial. It gets deleted after 30 days.
- A typo in the domain name. It's permanent, so read it twice before clicking Create.

## Step 2. Write down your subscription and normal tenant IDs

📍 **Your normal directory**

1. Open the [Azure portal](https://portal.azure.com), type **Subscriptions** in the top search bar, and open it.
2. Click your subscription. Copy **Subscription ID** into Notepad as `AZURE_SUBSCRIPTION_ID`.
3. Back in the [Entra admin center](https://entra.microsoft.com): **Entra ID → Overview**. Copy **Tenant ID** into Notepad as `AZURE_TENANT_ID`.

✅ Both are GUIDs like `1a2b3c4d-....`, and they're **different** from each other.

## Step 3. Create the resource group for the app

📍 **Your normal directory**

This is the folder the automatic deployment will put the database, API and website into.

1. [Azure portal](https://portal.azure.com) → search **Resource groups** → **+ Create**.
2. **Subscription:** yours. **Resource group:** `rg-bookmarks-dev`. **Region:** **West US 3**.
3. **Review + create** → **Create**.

✅ `rg-bookmarks-dev` shows in the Resource groups list (empty for now, which is expected).

**Then switch on the Azure services this subscription will use.** A brand-new subscription hasn't enabled them,
and the deploy identity isn't allowed to do it (that's a subscription-level action, and it only has rights on the
resource group). Open **Cloud Shell** — the `>_` icon in the portal's top bar, choose **Bash** — and run:

```
az provider register --namespace Microsoft.Web --wait
az provider register --namespace Microsoft.Sql --wait
az provider register --namespace Microsoft.OperationalInsights --wait
az provider register --namespace Microsoft.Insights --wait
az provider register --namespace Microsoft.ManagedIdentity --wait
```

✅ **Subscriptions → your subscription → Resource providers** shows all five as **Registered**. Skip this and the
first deploy fails with *MissingSubscriptionRegistration*.

## Step 4. Create the identity GitHub will use to deploy

📍 **Your normal directory**

GitHub Actions needs permission to deploy into Azure. Instead of storing a password in GitHub, we tell Azure: *"trust GitHub when it's running the `dev` environment of `ConorWHart/Bookmarks`."*

**4a. Register the deploy identity**
1. [Entra admin center](https://entra.microsoft.com) → **Entra ID → App registrations → + New registration**.
2. **Name:** `github-bookmarks-deploy`
3. **Supported account types:** the first option (*this organizational directory only*).
4. **Redirect URI:** leave empty.
5. **Register**.
6. On the page that opens, copy **Application (client) ID** into Notepad as `AZURE_CLIENT_ID`.
   (Also check that **Directory (tenant) ID** matches your `AZURE_TENANT_ID`. If it doesn't, you're in the wrong tenant.)

**4b. Trust GitHub (federated credential)**
1. Still on `github-bookmarks-deploy`: left menu **Certificates & secrets** → **Federated credentials** tab → **+ Add credential**.
2. **Federated credential scenario:** *GitHub Actions deploying Azure resources*.
3. Fill in **exactly** as follows. These fields are **case-sensitive**:
   - **Organization:** `ConorWHart`
   - **Organization ID:** `16828358` (the `"id"` field at `https://api.github.com/users/ConorWHart`)
   - **Repository:** `Bookmarks`
   - **Repository ID:** `1376737362` (the `"id"` field at `https://api.github.com/repos/ConorWHart/Bookmarks`)
   - **Entity type:** `Environment`
   - **Based on selection / environment name:** `dev`
   - **Name:** `bookmarks-dev`
4. **Add**.

⚠️ **Don't skip the two ID fields.** GitHub now includes those numbers in the identity it presents. Leave them out and the form still saves, but every deploy fails with *AADSTS700213: No matching federated identity record*.

✅ The Federated credentials tab lists `bookmarks-dev`, and opening it shows the generated **Subject identifier** as `repo:ConorWHart@16828358/Bookmarks@1376737362:environment:dev` — with real numbers, not `{Organization ID}` placeholders.

❌ **Common mistakes**
- Creating a **client secret** on the *Client secrets* tab. We don't want one; only the *Federated credentials* tab.
- Wrong capitalization (`conorwhart`, `bookmarks`). Deploys then fail with "no matching federated identity record".

## Step 5. Let the deploy identity deploy into `rg-bookmarks-dev`

📍 **Your normal directory**

1. [Azure portal](https://portal.azure.com) → **Resource groups** → **rg-bookmarks-dev**.
2. Left menu **Access control (IAM)** → **+ Add** → **Add role assignment**.
3. **Role** tab: click the **Privileged administrator roles** sub-tab → select **Contributor** → **Next**.
4. **Members** tab: *Assign access to* = **User, group, or service principal** → **+ Select members** → search `github-bookmarks-deploy` → click it → **Select** → **Next**.
5. If there's a **Conditions** tab, leave it as is → **Review + assign** → **Review + assign**.

✅ Under **Access control (IAM) → Role assignments**, `github-bookmarks-deploy` appears as **Contributor**.

❌ **Common mistakes**
- Assigning the role on the **subscription** instead of the **resource group**. Make sure you started from `rg-bookmarks-dev`.
- Choosing **Owner**. Contributor is enough, and safer.

## Step 6. Create the database admin group

📍 **Your normal directory**

The database only accepts Microsoft sign-ins (no SQL passwords). This group is its admin. You're in it so you can look at the data, and the deploy identity is in it so deployments can update the tables.

1. [Entra admin center](https://entra.microsoft.com) → **Entra ID → Groups → All groups → + New group**.
2. **Group type:** Security. **Group name:** `Bookmarks SQL Admins`. **Microsoft Entra roles can be assigned to the group:** No.
3. **Members:** click **No members selected** → search and tick **your own account** → search and tick **github-bookmarks-deploy** → **Select**.
4. **Create**.
5. Open the new group and copy **Object ID** into Notepad as `SQL_ADMIN_GROUP_ID`.

✅ The group's **Members** page lists two members: you and `github-bookmarks-deploy`.

❌ **If `github-bookmarks-deploy` doesn't show up in the member search**, create the group with just yourself and tell Claude. There's a one-line command to add it.

## Step 7. Create the `dev` environment on GitHub

📍 **github.com** (not Azure)

1. Open `https://github.com/ConorWHart/Bookmarks` → **Settings** (repo settings, top of the page) → left menu **Environments** → **New environment**.
2. **Name:** `dev` (lowercase, matching Step 4b) → **Configure environment**.
3. Optional but recommended: tick **Required reviewers** and add yourself. Then each deploy waits for you to click Approve.
4. Scroll to **Environment variables** → **Add environment variable**. Add these four. Use **variables**, *not* secrets:

   | Name | Value |
   |---|---|
   | `AZURE_CLIENT_ID` | from Notepad |
   | `AZURE_TENANT_ID` | from Notepad |
   | `AZURE_SUBSCRIPTION_ID` | from Notepad |
   | `AZURE_RESOURCE_GROUP` | `rg-bookmarks-dev` |

✅ The `dev` environment page shows 4 variables and 0 secrets.

## Step 8. Check that the Bookmarks tenant has finished

📍 **Your normal directory**

1. Click the **bell icon 🔔** (top right) and look for the tenant creation notification. If it's still running, grab a coffee.
2. When it's done: **gear ⚙ → Directories + subscriptions**. `Bookmarks` should now be in the list.
3. Click **Switch** next to **Bookmarks**.
4. Check the **top-right corner**: it should now say **Bookmarks**.
5. **Entra ID → Overview**. Copy:
   - **Tenant ID** → Notepad `EXT_TENANT_ID` (it's **different** from `AZURE_TENANT_ID`, which is expected).
   - **Primary domain**, e.g. `bookmarksapp.onmicrosoft.com` → Notepad `EXT_SUBDOMAIN` = just the first part, `bookmarksapp`.

---

# Part B: the new Bookmarks tenant

📍 **Every step in Part B happens in the Bookmarks tenant.** Check the top-right corner says **Bookmarks** before each one. If you close the browser and come back later, you may be back in your normal directory, so switch again.

## Step 9. Register the API

This tells the sign-in service that the Bookmarks API exists and what permission ("scope") apps can ask for.

1. **Entra ID → App registrations → + New registration**.
2. **Name:** `Bookmarks API`. **Supported account types:** the first option. **Redirect URI:** leave empty. → **Register**.
3. Copy **Application (client) ID** → Notepad `API_CLIENT_ID`.
4. Left menu **Expose an API**:
   - Next to **Application ID URI**, click **Add** → leave the suggested value (`api://` followed by the client ID) → **Save**.
   - Click **+ Add a scope**:
     - **Scope name:** `access_as_user`
     - **Who can consent?** Admins only
     - **Admin consent display name:** `Access Bookmarks`
     - **Admin consent description:** `Read and write your Bookmarks library`
     - **State:** Enabled → **Add scope**.
5. Left menu **Manifest**. Press **Ctrl+F** in the editor and search for `requestedAccessTokenVersion` (older view: `accessTokenAcceptedVersion`).
   - If it says `null` or `1`, change it to `2` (just the number, no quotes) → **Save**.
   - If it's already `2`, nothing to do.

✅ **Expose an API** shows the scope `api://<API_CLIENT_ID>/access_as_user`, and the manifest shows `2`.

## Step 10. Register the Bookmarks web app

This is the registration the Blazor app uses to sign people in. Its name is **shown to users** on the sign-in page.

1. **Entra ID → App registrations → + New registration**.
2. **Name:** `Bookmarks`. **Supported account types:** the first option.
3. **Redirect URI:** set the dropdown to **Single-page application (SPA)** (**not** *Web*), then enter:
   `https://localhost:7101/authentication/login-callback` → **Register**.
4. Copy **Application (client) ID** → Notepad `SPA_CLIENT_ID`.
5. Left menu **Authentication** → under the **Single-page application** box → **Add URI** → enter
   `http://localhost:5080/authentication/login-callback` → **Save** at the bottom.
   (Leave the *Implicit grant* checkboxes **unticked**.)
6. Left menu **API permissions** → **+ Add a permission** → **APIs my organization uses** tab → click **Bookmarks API** → **Delegated permissions** → tick **access_as_user** → **Add permissions**.
7. **+ Add a permission** again → **Microsoft Graph** → **Delegated permissions** → tick **openid** and **offline_access** (use the search box) → **Add permissions**. (These let the app sign people in and keep them signed in.)
8. Still on **API permissions**, click **Grant admin consent for Bookmarks** → **Yes**.

✅ **API permissions** lists `access_as_user` (Bookmarks API) plus `offline_access`, `openid` and `User.Read` (Microsoft Graph), each with a **green check** under *Status*.

❌ **Common mistakes**
- Choosing **Web** instead of **Single-page application**. Sign-in then fails with an error mentioning "cross-origin" or "client_secret". To fix it, delete that URI and add it again under a Single-page application platform.
- Typos in the redirect URI (`login-callback`, not `login_callback`; `7101`, `https`). It must match exactly.
- Forgetting **Grant admin consent**. Users would then see a permission error.

## Step 11. Turn on password reset

1. **Entra ID → Authentication methods → Policies** → click **Email OTP** → **Enable and Target** tab → **Enable** on → **Include: All users** → **Save**.
   *(Password-reset codes are sent by email, so this has to be on.)*
2. Search the top bar for **Company branding** → **Default sign-in** → **Edit** → **Sign-in form** tab → scroll to **Self-service password reset** → tick **Show self-service password reset** → **Review + save** → **Save**.

✅ Both saved without errors. (While you're in Company branding, you can optionally add a background color and a logo so the sign-in page looks like Bookmarks.)

## Step 12. Create the sign-up / sign-in flow

The "user flow" is the actual sign-in page users see: sign up, verify email, sign in, forgot password.

1. **Entra ID → External Identities → User flows → + New user flow**.
2. **Name:** `SignUpSignIn`.
3. **Identity providers:** under *Email Accounts*, choose **Email with password**.
4. **User attributes:** tick **Display Name** only → **OK** (if there's a *Show more* list, you don't need anything from it).
5. **Create**.
6. Click the new **SignUpSignIn** flow → left menu **Applications** → **+ Add application** → tick **Bookmarks** (the web app from Step 10, **not** *Bookmarks API*) → **Select**.

✅ **SignUpSignIn → Applications** lists **Bookmarks**.

❌ **Common mistake:** forgetting step 6. The flow does nothing until the app is added to it, and sign-in shows a confusing error.

---

# Part C: your PC

## Step 13. Check that LocalDB is installed

The API uses a small local SQL Server (LocalDB) while you develop.

1. Start menu → **Developer PowerShell for VS 2026**.
2. Run `sqllocaldb info`.
   - ✅ Prints `MSSQLLocalDB`: you're done.
   - ❌ "not recognized": open **Visual Studio Installer** → **Modify** → **Individual components** → search **LocalDB** → tick **SQL Server Express LocalDB** → **Modify**. Then try again.

Optional: **Docker Desktop**. With it running, the API's database tests run too. Without it they're skipped, which is fine.

---

# Done: send the values to Claude

Paste your filled-in Notepad block into the chat. It should have **8 values**, and a quick sanity check:

- `EXT_TENANT_ID` ≠ `AZURE_TENANT_ID` (two different tenants).
- `API_CLIENT_ID`, `SPA_CLIENT_ID` and `AZURE_CLIENT_ID` are all different.
- `EXT_SUBDOMAIN` is just a word like `bookmarksapp`, with no `.onmicrosoft.com`.

Claude then updates the config files (`appsettings`, `infra/main.parameters.json`) and the deployment pipeline. After the first deployment there's **one more small step**: adding the live website's address to the web app registration (the same kind of step as Step 10, item 5). Claude will give you the exact URL.

## Checklist

- [ ] 1 Bookmarks tenant creation started (free trial **not** selected)
- [ ] 2 `AZURE_SUBSCRIPTION_ID`, `AZURE_TENANT_ID` copied
- [ ] 3 `rg-bookmarks-dev` created + the five resource providers registered
- [ ] 4 `github-bookmarks-deploy` registered + federated credential for environment `dev`
- [ ] 5 Contributor on `rg-bookmarks-dev`
- [ ] 6 `Bookmarks SQL Admins` group (you + deploy identity)
- [ ] 7 GitHub environment `dev` with 4 variables
- [ ] 8 Switched to Bookmarks tenant; `EXT_TENANT_ID`, `EXT_SUBDOMAIN` copied
- [ ] 9 `Bookmarks API` + `access_as_user` scope + token version 2
- [ ] 10 `Bookmarks` SPA + 2 redirect URIs + permission + admin consent
- [ ] 11 Email OTP on + password reset link shown
- [ ] 12 `SignUpSignIn` flow with the Bookmarks app added
- [ ] 13 LocalDB works

## How this fits together (for later reference)

**Your normal directory** (the Azure side)
- Subscription
  - `rg-bookmarks-identity`: pays for the Bookmarks tenant
  - `rg-bookmarks-dev`: Azure SQL database, App Service (the API), Static Web App (the website)
- `Bookmarks SQL Admins`: the database's admins (you + the deploy identity)
- `github-bookmarks-deploy`: the identity GitHub Actions uses to deploy (trusted for environment `dev`)

**Bookmarks tenant** (the users' side)
- User accounts (email + password)
- `SignUpSignIn`: the sign-in / sign-up pages
- `Bookmarks API` registration: defines the `access_as_user` permission
- `Bookmarks` registration: the website, which asks for `access_as_user`

When someone signs in, the website sends them to the Bookmarks tenant's sign-in page, gets back a token that says *"this is user X and they may use access_as_user"*, and sends that token to the API. The API checks the token and only ever shows user X their own data.

References (Microsoft Learn): [create an external tenant](https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-create-external-tenant-portal) · [user flows](https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-user-flow-sign-up-sign-in-customers) · [password reset](https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-enable-password-reset-customers) · [Google](https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-google-federation-customers)

---

## Appendix: deferred Google sign-in (Phase 7)

**Don't do this now.** It's kept here for Phase 7 (Polish / nice-to-haves). It needs the Bookmarks tenant from Step 1. When it's enabled, also add Google to the `SignUpSignIn` user flow (**User flows → SignUpSignIn → Identity providers → Other identity providers → Google**). The app code doesn't change. Note that signing up with `x@gmail.com` + password and later using *Continue with Google* for the same address creates **two separate accounts** (there's no automatic linking). The Google client secret lives only in Entra.

### G.1 Google Cloud Console
1. [console.cloud.google.com](https://console.cloud.google.com) → project picker → **New Project** `Bookmarks` → select it.
2. **APIs & services → OAuth consent screen** (Google Auth Platform → Branding/Audience in the newer UI):
   - User type **External**, app name `Bookmarks`, support email = yours.
   - **Authorized domains:** `ciamlogin.com` and `microsoftonline.com`.
   - Developer contact: your email.
   - Scopes: the defaults (`openid`, `email`, `profile`) are enough. Don't add sensitive scopes, because they trigger Google's verification.
3. **Credentials → Create credentials → OAuth client ID → Web application**, name `Entra External ID`. Under **Authorized redirect URIs** add all seven (replace `<EXT_TENANT_ID>` and `<EXT_SUBDOMAIN>`, lowercase):
   ```
   https://login.microsoftonline.com
   https://login.microsoftonline.com/te/<EXT_TENANT_ID>/oauth2/authresp
   https://login.microsoftonline.com/te/<EXT_SUBDOMAIN>.onmicrosoft.com/oauth2/authresp
   https://<EXT_TENANT_ID>.ciamlogin.com/<EXT_TENANT_ID>/federation/oidc/accounts.google.com
   https://<EXT_TENANT_ID>.ciamlogin.com/<EXT_SUBDOMAIN>.onmicrosoft.com/federation/oidc/accounts.google.com
   https://<EXT_SUBDOMAIN>.ciamlogin.com/<EXT_TENANT_ID>/federation/oauth2
   https://<EXT_SUBDOMAIN>.ciamlogin.com/<EXT_SUBDOMAIN>.onmicrosoft.com/federation/oauth2
   ```
4. **Create** → copy the **Client ID** and **Client secret**. The secret goes straight into Entra (next step) and nowhere else.
5. **Publishing status:** while the app is in *Testing*, only listed test users can sign in. Add your own Gmail as a test user now. Before real users arrive, click **Publish app → In production**. With only basic scopes this doesn't need Google review, though uploading a logo can trigger brand verification.

### G.2 Entra (Bookmarks tenant)
**Entra ID → External Identities → All identity providers → Built-in → Google → Configure**: name `Google`, paste the client ID and secret → **Save**.

# Google OAuth Setup Guide

**Project:** GameSwap (SportSch)
**Date:** 2026-01-17
**Purpose:** Enable Google Sign-In as an authentication provider alongside Azure AD

## Overview

This document provides step-by-step instructions for configuring Google OAuth 2.0 authentication for the GameSwap application. Users will be able to sign in with either Microsoft accounts (Azure AD) or Google accounts.

## Prerequisites

- Access to [Google Cloud Console](https://console.cloud.google.com/)
- Admin access to Azure Static Web Apps configuration
- Existing Azure Static Web App deployment

## Implementation Status

✅ **Frontend Changes**: Complete (3 files updated)
✅ **Configuration File**: Complete (staticwebapp.config.json created)
⏳ **Google OAuth Setup**: Requires manual configuration (follow this guide)
⏳ **Azure App Settings**: Requires manual configuration (follow this guide)

## Part 1: Google Cloud Console Configuration

### Step 1: Create or Select a Google Cloud Project

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Either:
   - **New Project**: Click "Select a project" → "New Project" → Enter project name (e.g., "SportSch Auth")
   - **Existing Project**: Select your existing project

### Step 2: Enable Google+ API

1. In the left sidebar, navigate to **APIs & Services** → **Library**
2. Search for **"Google+ API"**
3. Click on **Google+ API** and click **Enable**
4. (Alternative: Search for "People API" if Google+ API is deprecated)

### Step 3: Configure OAuth Consent Screen

1. Navigate to **APIs & Services** → **OAuth consent screen**
2. Choose **User Type**:
   - **External**: For public access (recommended)
   - **Internal**: Only if using Google Workspace organization
3. Click **Create**

4. Fill in **App Information**:
   - **App name**: `SportSch` (or your app name)
   - **User support email**: Your support email
   - **App logo**: (Optional) Upload your logo
   - **App domain**:
     - **Application home page**: `https://your-app.azurestaticapps.net`
     - **Application privacy policy**: (If available)
     - **Application terms of service**: (If available)
   - **Authorized domains**: `azurestaticapps.net`
   - **Developer contact email**: Your email

5. Click **Save and Continue**

6. **Scopes** (Step 2):
   - Click **Add or Remove Scopes**
   - Select the following scopes:
     - `openid`
     - `email`
     - `profile`
   - Click **Update** → **Save and Continue**

7. **Test Users** (Step 3):
   - If using "External" with "Testing" status, add test user emails
   - Click **Save and Continue**

8. **Summary** (Step 4):
   - Review your configuration
   - Click **Back to Dashboard**

### Step 4: Create OAuth 2.0 Credentials

1. Navigate to **APIs & Services** → **Credentials**
2. Click **Create Credentials** → **OAuth client ID**
3. Choose **Application type**: **Web application**
4. Enter **Name**: `SportSch Web Client`

5. Configure **Authorized JavaScript origins**:
   - Click **Add URI**
   - Add: `https://your-app.azurestaticapps.net`
   - Add: `http://localhost:4280` (for local testing with Azure Static Web Apps CLI)

6. Configure **Authorized redirect URIs**:
   - Click **Add URI**
   - Add: `https://your-app.azurestaticapps.net/.auth/login/google/callback`
   - Add: `http://localhost:4280/.auth/login/google/callback` (for local testing)

7. Click **Create**

8. **Save Your Credentials**:
   - A dialog will show your **Client ID** and **Client Secret**
   - **IMPORTANT**: Copy both values immediately
   - Store them securely (e.g., password manager)

   Example format:
   ```
   Client ID: 123456789012-abcdefghijklmnopqrstuvwxyz123456.apps.googleusercontent.com
   Client Secret: GOCSPX-AbCdEfGhIjKlMnOpQrStUvWxYz
   ```

## Part 2: Azure Static Web App Configuration

### Step 5: Add Application Settings in Azure Portal

1. Go to [Azure Portal](https://portal.azure.com/)
2. Navigate to your **Static Web App** resource
3. In the left sidebar, select **Configuration**
4. Click **Application settings** tab

5. Add the following settings:

   **For Google OAuth:**
   - Click **+ Add**
   - **Name**: `GOOGLE_CLIENT_ID`
   - **Value**: `[Your Google Client ID from Step 4]`
   - Click **OK**

   - Click **+ Add**
   - **Name**: `GOOGLE_CLIENT_SECRET`
   - **Value**: `[Your Google Client Secret from Step 4]`
   - Click **OK**

   **For Azure AD (if not already configured):**
   - Click **+ Add**
   - **Name**: `AAD_CLIENT_ID`
   - **Value**: `[Your Azure AD Application ID]`
   - Click **OK**

   - Click **+ Add**
   - **Name**: `AAD_CLIENT_SECRET`
   - **Value**: `[Your Azure AD Client Secret]`
   - Click **OK**

6. Click **Save** at the top

7. Wait for the configuration to deploy (usually 1-2 minutes)

### Step 6: Verify staticwebapp.config.json Deployment

The `staticwebapp.config.json` file in the project root should already be deployed with your app. Verify it contains:

```json
{
  "auth": {
    "identityProviders": {
      "azureActiveDirectory": {
        "registration": {
          "openIdIssuer": "https://login.microsoftonline.com/common/v2.0",
          "clientIdSettingName": "AAD_CLIENT_ID",
          "clientSecretSettingName": "AAD_CLIENT_SECRET"
        }
      },
      "google": {
        "registration": {
          "clientIdSettingName": "GOOGLE_CLIENT_ID",
          "clientSecretSettingName": "GOOGLE_CLIENT_SECRET"
        }
      }
    }
  }
}
```

If not present, ensure the file is committed to your repository and redeployed.

## Part 3: Testing

### Step 7: Test Google Sign-In

1. **Test on Deployed App**:
   - Navigate to your app: `https://your-app.azurestaticapps.net`
   - You should see two login buttons:
     - "Sign in with Microsoft"
     - "Sign in with Google"
   - Click **"Sign in with Google"**
   - You should be redirected to Google's sign-in page
   - Sign in with a Google account
   - After successful authentication, you should be redirected back to the app

2. **Verify User Identity**:
   - Check that your email is displayed correctly in the app
   - The backend `IdentityUtil.cs` will extract your userId and email from the `x-ms-client-principal` header
   - Open browser DevTools → Network tab → Check API requests → Headers → `x-ms-client-principal` should be present

3. **Test Access Request**:
   - Try requesting access to a league
   - Verify that the request is created with your Google account email
   - Verify that an admin can see your access request

### Step 8: Test Microsoft Sign-In (Regression Test)

1. Sign out (if signed in)
2. Click **"Sign in with Microsoft"**
3. Verify that Azure AD authentication still works
4. Verify that you can access the app with your Microsoft account

### Step 9: Local Testing (Optional)

If using Azure Static Web Apps CLI for local development:

1. Install the CLI:
   ```bash
   npm install -g @azure/static-web-apps-cli
   ```

2. Create a `swa-cli.config.json` file in project root:
   ```json
   {
     "configurations": {
       "app": {
         "appLocation": ".",
         "apiLocation": "api",
         "outputLocation": "dist",
         "appBuildCommand": "npm run build",
         "apiBuildCommand": "dotnet build api/GameSwap_Functions.csproj -c Release -o api/bin/publish"
       }
     }
   }
   ```

3. Start the local emulator:
   ```bash
   swa start
   ```

4. Navigate to `http://localhost:4280`
5. Test Google and Microsoft sign-in locally

**Note**: Local testing requires that your Google OAuth redirect URIs include `http://localhost:4280/.auth/login/google/callback` (configured in Step 4).

## Part 4: Publishing OAuth App (Optional)

### Step 10: Publish OAuth Consent Screen

If you want to allow any Google user to sign in (not just test users):

1. Go to **Google Cloud Console** → **APIs & Services** → **OAuth consent screen**
2. Click **Publish App**
3. Review the warning and click **Confirm**
4. Your app will be submitted for verification (this can take several days)
5. Until verified, users will see a warning screen but can still proceed

**For MVP**: You can keep the app in "Testing" status and add specific test users.

## Troubleshooting

### Issue: "Error 400: redirect_uri_mismatch"

**Cause**: The redirect URI in your Google OAuth credentials doesn't match the actual callback URL.

**Solution**:
- Check that your redirect URIs in Google Cloud Console include:
  - `https://your-app.azurestaticapps.net/.auth/login/google/callback`
- Ensure there are no typos or extra characters
- Wait a few minutes for Google's configuration to propagate

### Issue: "Error 401: invalid_client"

**Cause**: The client ID or client secret in Azure app settings is incorrect.

**Solution**:
- Verify that `GOOGLE_CLIENT_ID` and `GOOGLE_CLIENT_SECRET` in Azure are correct
- Ensure there are no leading/trailing spaces
- Regenerate credentials in Google Cloud Console if needed

### Issue: User sees "This app is blocked"

**Cause**: OAuth consent screen is not configured or published.

**Solution**:
- Complete Step 3 (OAuth Consent Screen configuration)
- Add your email as a test user (Step 3, substep 7)
- Or publish the app (Step 10)

### Issue: Backend doesn't recognize Google users

**Cause**: This should not happen as `IdentityUtil.cs` is provider-agnostic.

**Solution**:
- Check Azure Application Insights logs for errors
- Verify that the `x-ms-client-principal` header contains email claim
- Ensure `IdentityUtil.cs` checks for `"emails"` claim type (Google uses this)

### Issue: Can't see login buttons on frontend

**Cause**: Frontend changes not deployed.

**Solution**:
- Verify that commits to `src/App.jsx`, `src/pages/AccessPage.jsx`, and `src/pages/InviteAcceptPage.jsx` are pushed
- Check GitHub Actions or Azure deployment logs
- Hard refresh the browser (Ctrl+Shift+R)

## Security Considerations

1. **Client Secret Storage**: Never commit `GOOGLE_CLIENT_SECRET` to source control. Always store in Azure app settings.

2. **Redirect URI Validation**: Only add trusted redirect URIs. Never use wildcards.

3. **OAuth Scopes**: Only request necessary scopes (`openid`, `email`, `profile`). Avoid requesting additional permissions.

4. **User Identity Mapping**: Currently, users with the same email on different providers (e.g., john@example.com on Google and Microsoft) will be treated as different users because `userId` includes the provider. Consider implementing email-based identity mapping if cross-provider login is desired.

5. **Access Control**: The backend already enforces authorization based on league membership. Google users will go through the same access request workflow as Microsoft users.

## Architecture Notes

### Provider-Agnostic Backend

The backend (`api/Storage/IdentityUtil.cs`) is already provider-agnostic and works with any OAuth provider supported by Azure Static Web Apps. It extracts claims from the `x-ms-client-principal` header, which is populated by Azure's EasyAuth layer.

Key claim types checked:
- **User ID**: `"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"`, `"sub"`, `"oid"`, `"nameid"`
- **Email**: `"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress"`, `"emails"`, `"email"`, `"preferred_username"`, `"upn"`

Google typically uses:
- User ID: `"sub"` claim
- Email: `"emails"` claim (array) or `"email"` claim

### No Backend Changes Required

Because `IdentityUtil.cs` already checks for `"emails"` claim (used by Google), no backend code changes are needed. The backend will correctly extract email from Google-authenticated users.

## Support and Documentation

- **Azure Static Web Apps Authentication**: https://learn.microsoft.com/en-us/azure/static-web-apps/authentication-authorization
- **Google OAuth 2.0**: https://developers.google.com/identity/protocols/oauth2
- **Google Sign-In Best Practices**: https://developers.google.com/identity/sign-in/web/sign-in

## Maintenance

### Rotating Secrets

If you need to rotate your Google client secret:

1. Go to **Google Cloud Console** → **Credentials**
2. Click on your OAuth client ID
3. Click **Reset Secret**
4. Copy the new secret
5. Update `GOOGLE_CLIENT_SECRET` in Azure app settings
6. Save and wait for deployment

### Monitoring

Monitor authentication in Azure Application Insights:
- Check for 401/403 errors
- Track `/.auth/login/google` requests
- Monitor `IdentityUtil.GetMe()` calls

## Rollback Plan

If Google authentication causes issues:

1. Remove Google login buttons from frontend:
   - Revert changes to `src/App.jsx`, `src/pages/AccessPage.jsx`, `src/pages/InviteAcceptPage.jsx`
   - Push and deploy

2. Remove Google provider from `staticwebapp.config.json`:
   - Delete the `"google"` section from `identityProviders`
   - Push and deploy

3. Remove Azure app settings:
   - Delete `GOOGLE_CLIENT_ID` and `GOOGLE_CLIENT_SECRET` from Azure configuration

Microsoft authentication will continue to work as before.

---

**Document Version:** 1.0
**Last Updated:** 2026-01-17
**Author:** Development Team
**Next Review:** After initial deployment and testing

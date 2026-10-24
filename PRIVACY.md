# Privacy Policy for Karobar App

**Effective Date:** October 2026

## 1. Overview
Karobar App is an inventory, POS, and trade ERP application. This policy outlines our data handling practices, specifically regarding third-party cloud drive integrations such as Google Drive.

## 2. Google User Data Accessed & Stored
When you connect Google Drive to Karobar App for automated offsite backups:
- **Scope Used:** `https://www.googleapis.com/auth/drive.file`
- **Limited Access:** Karobar App only accesses, uploads, and manages files that are created by Karobar App itself (such as database backup archives). Karobar App **cannot** access, read, modify, or delete any other personal files, photos, or documents in your Google Drive.
- **Tokens:** Authentication tokens (OAuth refresh tokens) are stored locally in the user's private database instance to enable scheduled automated uploads.

## 3. Data Protection & Sharing
Karobar App does **not** sell, share, or transfer your Google user data or backup files to third parties or advertising platforms. All data transmission occurs directly between your Karobar App instance and Google's official API servers over secure SSL/TLS encryption.

## 4. Revoking Access
You can disconnect Karobar App at any time:
1. Within Karobar App: Navigate to **System > Backups & Storage** and click **Disconnect Account**.
2. Within Google: Visit [Google Account Permissions](https://myaccount.google.com/permissions) and remove Karobar App.

## 5. Contact
For questions regarding this policy, contact: [shzdasim@gmail.com](mailto:shzdasim@gmail.com)

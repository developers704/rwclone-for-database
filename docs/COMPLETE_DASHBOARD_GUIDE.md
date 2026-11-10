# Valliani Data Sync — Complete Dashboard Guide

This guide explains what is in the dashboard and what you should do step by step.

---

## 1) How to open the app

1. If you need Valliani server sync: run `START-API.bat`
2. Then run the desktop app: `START-DESKTOP.bat`

---

## 2) Login + dual OTP + emails (dynamic)

### First-time setup

1. Open the app (if OTP is off, the dashboard opens directly).
2. Left menu → **Security / OTP**
3. Enter **OTP Email 1** and **OTP Email 2** (two different emails).
4. (Optional) **Allowed IPs** — this PC’s IPv4 from `ipconfig`. Empty = allow all IPs.
5. Fill **SMTP** (Gmail / office mail) so OTPs arrive by email:
   - Host, Port (587), Username, Password, From
6. Check **Enable dual-email OTP login**
7. **Save**
8. Click **Send test OTP** — check both inboxes

> If SMTP is not configured yet, OTP is written to:  
> `%LocalAppData%\VallianiDataSync\otp-dev.txt`  
> For production, SMTP is required.

### Change emails later

On the Security page, edit the emails → **Save**. The next login uses the new emails.

### Every login (when OTP is on)

1. Open the app → Login screen  
2. **Send OTP**  
3. Enter the code from email 1 and email 2  
4. **Unlock**  
5. Use **Lock** (bottom of sidebar) to lock again  

Creating/deleting a destination also requires OTP when OTP is enabled: on Destinations click **Send OTP** → enter codes → then Add/Delete.

---

## 3) Database (source) — already synced earlier

1. **Database** → MySQL / host / `Vendor_portal` / user / password → Test → Save  
2. **Data Mapping** → table `products` → **Load all columns A→Z** → Save  
3. **Server / API** → Valliani API URL + key → Save  
4. **Dashboard** → **Sync Now** (products to Valliani API)

This is the **source** data. Destinations read from here.

---

## 4) Destinations — how do we know the format?

**The API does not tell us the format.** You **upload the format at the start**; we **save** it; sync sends **only those keys** — nothing else.

### Format upload (3 ways)

| File | Meaning |
|------|---------|
| `.csv` | First line = column names (e.g. `sku,title,price`) |
| `.json` sample object | Object keys = destination fields |
| `format-definition.json` | `{ "fields": [ { "destinationKey":"sku", "sourceField":"Sku" } ] }` |

Samples: `docs/samples/`

### Steps

1. **ADMIN → Destinations** → Send OTP → codes → **Add project**  
2. **Upload format** (OTP) → your Shopify / marketplace / AI file  
3. Map each **Destination key** to **Our source field** (Sku, Name, RetailPrice, or any DB column A→Z)  
4. Fields you do not want → **Delete** (e.g. no images for Shopify)  
5. API URL + key → **Enabled** → **Save** (OTP) → **Sync Now**  

The payload contains **only the saved format keys** — exact names.

### When is Admin OTP required?

| Action | OTP |
|--------|-----|
| Email / IP change (Security) | Yes (after first setup) |
| Add / Delete destination | Yes |
| Upload format / Save destination | Yes |
| Normal Dashboard Sync Now (Valliani API) | No |
| Destination Sync Now (format already saved) | No |

### Sync Now vs Full Sync

- **Sync Now** — immediate; only products that changed  
- **Full Sync** — immediate; all products again (first setup / force refresh)

### Auto sync

Enable **Auto sync** and set interval **120** (2 hours) or **180** (3 hours). The desktop app must stay running.

---

## 5) Recommended daily flow

| Step | Where |
|------|--------|
| Login (OTP) | Login screen |
| Source DB connected? | Database |
| Valliani API sync | Dashboard → Sync Now |
| External projects | Destinations → Sync Now / Auto |
| Emails / IP / SMTP | Security / OTP |
| Errors | Logs |

---

## 6) Checklist — what you should do now

1. [ ] Run `START-DESKTOP.bat` (new build)  
2. [ ] **Security / OTP** → 2 emails + SMTP (or test via `otp-dev.txt`) → Enable → Save  
3. [ ] Lock / restart app → Send OTP → Unlock test  
4. [ ] **Data Mapping** → Load all columns A→Z → Save  
5. [ ] **Destinations** → first project (API URL + format) → Enable → Sync Now  
6. [ ] Interval 120/180 + Auto sync on  
7. [ ] Add other projects (marketplace / AI) the same way  

---

## 7) Notes

- Destinations do not sync unless **Enabled**.  
- Deleting a format field means that column is not sent on the next sync.  
- API keys are stored with DPAPI (not plain text in settings).  
- If an IP allow-list is set, only listed PC IPs can log in.  
- Ignore old Swagger / dashboard errors; use the new build.

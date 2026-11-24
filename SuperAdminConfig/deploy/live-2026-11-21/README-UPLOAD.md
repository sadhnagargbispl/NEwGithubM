# Live upload – Super Admin (21 Nov 2026)

Is folder mein:

| File / folder | Kya hai |
|---|---|
| `LIVE_MASTER_SETUP.sql` | Live **master** database par chalne wali EK script (01 → 16 sab ek saath, sahi order mein) |
| `SuperAdmin-upload\` | Super Admin website – yahi poora folder server par copy karna hai |

---

## Step 1 – Master database ka backup

SSMS → master database par right-click → Tasks → Back Up. Backup ke bina aage mat badhiye.

## Step 2 – Script chalayein

1. SSMS mein server se connect karein, **master database** chunein.
2. `LIVE_MASTER_SETUP.sql` kholein.
3. Line 26 par `PUT_LIVE_MASTER_NAME_HERE` ki jagah master database ka **naam** likhein
   (jaise test ke liye `mlmmenumaster-test`).
4. **F5** dabayein.
5. Sabse neeche "=== DONE ===" aur ek chhoti table aani chahiye (FeatureRows ~2181, LabelRows ~29).

- Galat database khula ho ya naam na bhara ho to script **ruk jaati hai** ("STOPPED: wrong database") – kuch nahi badalta.
- Dobara chalana safe hai – jo pehle se hai woh chhod deti hai.
- Panels ki koi table nahi badalti; sirf Super Admin ki nayi tables (Cfg_*, SA_*) banti hain.

> Test master (`mlmmenumaster-test`) par ye sab **pehle se chal chuka hai** – wahan dobara chalane ki zaroorat nahi (chala bhi dein to kuch nahi bigadta).

## Step 3 – Super Admin upload (IIS)

1. Server par **ASP.NET Core 8 Hosting Bundle** install hona chahiye (ek baar).
2. `SuperAdmin-upload\` ka poora content server ke ek folder mein copy karein, jaise `D:\Sites\SuperAdmin`.
3. IIS mein nayi site banayein → wahi folder → **HTTPS** binding (login sirf https par chalta hai).
4. Application Pool: **.NET CLR version = No Managed Code**, Advanced Settings → **Load User Profile = True**
   (warna har restart par sabko dobara login karna padega).
5. Server par copy kiye `web.config` mein bharein:
   - `ConnectionStrings__Master` → server IP, master database naam, SQL login, password
   - `Setup__Key` → koi bhi lamba gupt shabd

## Step 4 – Pehla owner banayein

1. Browser mein `https://<super-admin-site>/account/setup` kholein.
2. Setup key (jo web.config mein likha) + apna naam, user name, password (kam se kam 10 akshar).
3. Owner ban jaane ke baad `web.config` se `Setup__Key` wali line **hata dein**.

> Test master par owner "sadhna" pehle se hai – wahan ye step nahi chahiye.

---

## Is upload mein jaan-boojh kar NAHI hai

| Kya | Kyon |
|---|---|
| Script 07 (`Proc_GetConnection1` badalna) | Ye live ki procedure ko **badal deti** hai. Pehle live wali procedure dekh kar milani hogi – alag se karenge. Iske bina Super Admin aur panels chalte hain; sirf "SQL server address" setting kaam nahi karegi. |
| Script 02 | React wala kaam band ho chuka hai |
| Scripts 04, 05 | Script 01 mein pehle se shaamil |
| Script 17 (Franchise / Shoppy / Web) | Ye panels abhi taiyaar nahi |
| Member cPanel + Admin panel ka naya code | Alag upload – iske liye pehle Step 2 zaroori hai |

## Kuch galat ho jaaye to

- Super Admin na khule: `web.config` mein `stdoutLogEnabled="true"` karein, `logs` folder mein error dekhein.
- Script ke baad wapas jaana ho: Step 1 ka backup restore karein.
  Dhyan: Member cPanel / Admin ka **naya** code upload ho chuka ho to ye tables **mat hatayein** – naya code
  company ki purani rules inhi tables se padhta hai; hatane par companies ka behaviour badal jaayega.

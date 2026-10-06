# Booking Platform — Staff Training Guide

This guide explains how the Booking Platform works for the people who use it every day: hosts, floor staff, managers and administrators. It also describes what guests see, so staff can help them. Labels in "quotes" are the exact texts shown in the app.

For developer documentation, see [`ARCHITECTURE.md`](ARCHITECTURE.md) and the other files in this folder.

---

## 1. What the app does

The Booking Platform takes reservations for any business that rents out space by time: restaurants, clubs, lounges, meeting rooms, spas, courts and so on. Staff work in a web app; guests book through a public web page and receive emails with a link to view or cancel their booking.

Everything is organised in this hierarchy:

- **Workspace (tenant)** — the business, e.g. "Demo Hospitality". A person can belong to several workspaces.
- **Venue** — a physical location, with its own address, time zone, opening hours and booking rules.
- **Section** — an area inside a venue, e.g. "Terrace", "Bar", "Floor 2".
- **Resource** — something bookable: a table, booth, room or court. It has a minimum and maximum party size.
- **Combination** — several resources booked together for a larger group, e.g. "Table 5 + 6".
- **Booking** — a customer (or a walk-in) holding one or more resources for a time slot.

**The golden rule:** the system never lets two active bookings use the same resource at the same time. The database enforces this, so even if two people click at the same moment, only one booking succeeds and the other person sees "That time was just taken".

All times are shown in the **venue's** time zone, not your computer's.

## 2. Roles — who can do what

Your role is set by an Owner or Admin on the Staff page. Menu items you have no permission for are not shown.

| Role | Typical person | Can do |
|---|---|---|
| **Owner / Admin** | Business owner, operations lead | Everything, including venue settings, staff, workspace settings, branding and the audit log |
| **Manager** | Venue manager | All booking actions, resources, floor plans, customers, reports, refunds. Not venue settings, staff or workspace settings |
| **Host** | Front-of-house host | Create, edit, move and cancel bookings; check in and seat guests; manage customers; view payments |
| **Staff** | Floor staff, waiters | View bookings; check in, seat and complete guests; view customers |
| **View only** | Observer, trainee | Look at bookings, resources and floor plans |

**Venue access:** Owners and Admins see every venue automatically. Everyone else only sees the venues assigned to them. Someone with no venues sees "Ask an administrator to assign you to a venue."

## 3. Getting started

### Signing in

1. Open the app and go to **"Sign in to your workspace"**.
2. Enter your email and password, then click **"Sign in"**.
3. If you belong to several workspaces, pick one on **"Choose a workspace"**.

After 5 wrong passwords the account is locked for a short time ("Too many failed attempts…"). The lock starts at one minute and grows with further failures, up to an hour. Wait and try again.

### Forgotten password

1. Click **"Forgot password?"**, enter your email, then **"Send reset link"**.
2. Open the email. The link works once and expires after 30 minutes.
3. Choose a new password. All your devices are signed out.

Administrators cannot reset another person's password; everyone uses this self-service flow.

**Password rules:** 12 to 128 characters, not a common password, and it must not contain the part of your email before the @. A passphrase such as "purple-giraffe-dinner-42" works well.

### Changing your password

Click your name (top right) → **"Account"** → **"Change password"**. Other devices are signed out.

### The header bar

- **Venue switcher** — shown when you have more than one venue. Everything on screen (board, reports, resources) is for the selected venue.
- **Workspace switcher** — shown when you belong to more than one business.
- **Live indicator:**
  - **"Live"** (green): changes made by colleagues appear automatically.
  - **"Reconnecting…"**: the connection dropped; the page refreshes when it reconnects.
  - **"Live updates paused"**: pages refresh every minute instead. Click the indicator to try again.

The live indicator is a convenience. The server is always the source of truth, so if in doubt, click a refresh button.

## 4. The Dashboard

The Dashboard is your start-of-shift view for the selected venue:

- **"Bookings"**, **"Covers"**, **"Seated now"** and **"Late arrivals"** for today.
- **"Upcoming arrivals"** — the next 8 guests due. Late ones get a red **"Late"** badge.
- **"By status"** — how many of today's bookings are in each status.
- **"Open board"** jumps to the booking board.

## 5. Booking statuses

Every booking moves through a set of statuses. Learning these is the most important part of the training.

| Status | Meaning | Holds the table? |
|---|---|---|
| **Pending** | Request received; staff still need to confirm it | Yes |
| **Held** | Online booking waiting for the guest to pay a deposit | Yes, until the payment deadline |
| **Confirmed** | The booking is on | Yes |
| **Checked in** | The guest has arrived but is not seated yet (e.g. waiting at the bar) | Yes |
| **Seated** | The guest is at the table | Yes |
| **Completed** | The guest has left | No |
| **Cancelled** | Cancelled by staff, by the guest, or automatically (unpaid deposit) | No |
| **No-show** | The guest never arrived | No |

### Status buttons in the booking drawer

| Button | Available when the booking is | Who can use it |
|---|---|---|
| **Confirm** | Pending or Held | Host and above |
| **Check in** | Confirmed | Staff and above |
| **Seat** | Confirmed or Checked in | Staff and above |
| **Complete** | Seated | Staff and above |
| **No-show** | Confirmed, and only after the start time | Host and above |
| **Cancel** | Pending, Held, Confirmed or Checked in | Host and above |

**Cancel** and **No-show** ask for confirmation and free the table immediately. **Complete** frees the table now, plus the venue's turnover (cleaning) buffer if one is set.

Bookings are never deleted. Cancelled bookings and no-shows stay in the history and in reports.

## 6. Daily workflows (hosts and floor staff)

### 6.1 The booking board

Open **Bookings** in the menu. Choose the day with the arrows, the date field or **"Today"**. There are three views.

**"Timeline"**

- Each row is a table, grouped by section. Each bar is a booking showing the party size and guest name.
- A red line marks the current time. Hatched areas are outside opening hours.
- Dashed bars are Pending or Held bookings; struck-through bars are cancelled.
- **Click a bar** to open the booking.
- **Drag a bar** sideways to change the time, or onto another row to change the table. You confirm the move in a dialog.
- **Double-click an empty space** to start a new booking for that table and time.
- Zoom with **S / M / L**.

**"Floor"**

- Shows the floor plan with tables coloured as Free, Confirmed, Checked in, Seated, Pending or Blocked.
- Move the time slider, or press **"Now"**, to see the room at any moment.
- Click a table to see who is on it, or **"New booking here"** if it is free.

**"List"**

- A table of the day's bookings: time, reference, customer, phone, guests, tables, status and source. Best for phone look-ups.

The **"Show cancelled & no-shows"** toggle adds cancelled bookings and no-shows back into the timeline and list.

### 6.2 Taking a booking (phone, or in person for later)

1. Click **"New booking"**, or double-click a free spot on the timeline.
2. **Step "When":**
   - Choose the date, party size and duration, then click **"Check availability"**.
   - Pick a time. Greyed-out times are full.
   - Under **"Table"**, keep **"Best available table"** (recommended) or choose a specific table or combination.
3. Click **"Continue"**. The table is now **held for you** for a few minutes (5 by default); the header shows "Held until HH:mm". This stops a colleague or an online guest taking it while you type.
4. **Step "Customer":**
   - Set the **Source**: Phone, Walk-in or Staff.
   - Returning guest: choose **"Existing customer"** and search by name, email or phone (at least 2 characters).
   - New guest: choose **"New customer"**. First name is required; add a phone and/or email. If the email already exists, that customer record is reused.
5. **Step "Details":**
   - Status: usually **"Confirmed"**.
   - **"Special requests"** — what the guest asked for.
   - **"Internal notes"** — staff only; guests never see these.
6. Click **"Create booking"**. You get a reference such as **BK-20261006-000123**. If the guest has an email address, they receive a confirmation email.

**Things that can go wrong:**

- **"That time was just taken…"** — someone booked it first. Pick another time.
- **"The table hold expired…"** — you took longer than the hold time. Go back and choose the time again.

Staff bookings are not limited by the online rules (online party-size range, minimum notice, how far ahead). Staff can take a group of 20 even if online booking stops at 12. Bookings must still fit within opening hours and the table's capacity.

### 6.3 Walk-ins

Same steps, but:

- Set the source to **"Walk-in"**.
- Choose **"No details"** if the guest does not want to give a name; the booking shows as "Walk-in".
- Set the status to **"Seated now"**.

### 6.4 From arrival to departure

1. The guest arrives: open the booking and click **"Check in"**, or go straight to **"Seat"** if the table is ready.
2. Seat them: **"Seat"**.
3. The guest leaves: **"Complete"**. The table becomes free on the board.

### 6.5 Changing a booking

- **Edit** (party size, special requests, internal notes): open the booking → **"Edit"** → **"Save"**. A bigger party must still fit the current table; otherwise move the booking.
- **Move** (date, time, duration or table): **"Move"** in the booking drawer, or drag the bar on the timeline.
- If a colleague changed the same booking at the same time, you see "This booking was changed by someone else. The latest version has been loaded." Review what changed and redo your change if it is still needed. Nothing is overwritten silently.

### 6.6 Cancellations and no-shows

- **Cancel** — click **"Cancel"** and optionally give a reason. The guest receives a cancellation email.
- **No-show** — only possible after the booking's start time. Use it so reports show the real no-show rate.

### 6.7 Blocking tables

For maintenance, private events, cleaning or staff use:

1. On the board, click **"Block"**.
2. Choose the tables, date and From/To times. An end time earlier than the start runs past midnight.
3. Choose a reason ("Maintenance", "Private event", "Cleaning", "Management", "Unavailable", "Staff use"), add optional notes, then click **"Block"**.

To remove a block, click it on the timeline → **"Remove block"**.

You cannot block over an existing booking. Move or cancel the booking first.

### 6.8 History and payments

- The **"History"** tab shows every status change, edit and move.
- The **"Payments"** tab shows deposits. Managers and above can **"Refund"** a deposit, in full or in part.
- The warning "Paid after the booking was cancelled; refund the guest or rebook." means money arrived too late, and someone must act on it.

## 7. Customers

Open **Customers** in the menu.

- Search by name, email or phone. Open a customer to see contact details, marketing consent, notes and booking history.
- **"Edit"** changes their details.
- **"Anonymise"** permanently removes the name, email, phone and notes, for example when a guest asks to be forgotten under privacy law. Their bookings are kept anonymously for reporting. This cannot be undone, and anonymised customers receive no more emails.

## 8. The guest experience (so you can help guests)

### Booking online

Guests open the venue's booking link, e.g. `…/book/demo-hospitality/demo-restaurant`. Owners and Admins find the link under **Venues → Settings**.

1. The guest picks a **date** and the **number of guests**.
2. Available **times** appear. Only resources marked "Bookable online" are used, and the system chooses the table.
3. The guest enters their details: first name and email are required; phone and special requests are optional.
4. They submit. The button says **"Confirm booking"**, **"Request booking"** or **"Continue to deposit"**, depending on the venue's settings.

| Venue setting | What the guest gets |
|---|---|
| Auto-confirm on (default) | "Your booking is confirmed" and a confirmation email |
| Auto-confirm off | "Request received" with status **Pending**. A host must click **Confirm**, which sends the confirmation email |
| Deposit required | "Your table is held" with status **Held**. The guest must pay within the payment window (30 minutes by default) or the booking is cancelled automatically |

**Online limits (defaults):** at least 60 minutes' notice, up to 90 days ahead, party size 1–12, and at most 3 upcoming bookings per guest at one venue. Guests who need something outside these limits should phone the venue, because staff can book outside them. Guests who see "Too many attempts" should wait a few minutes and try again.

### Viewing or cancelling (guest side)

- The confirmation screen and the email contain a private **"View my booking"** link. Anyone with the link can see or cancel the booking, so guests should keep it private.
- Guests can cancel online until the **cutoff** (2 hours before the start by default). After that they see "Contact the venue", and staff must cancel for them.
- Guests **cannot** change the time, party size or details online. Staff do that with Edit or Move.
- Emails for bookings made by staff do not include a manage link.

### Emails guests receive

| Email | When |
|---|---|
| "We received your booking request at {venue}" | The booking was created as Pending |
| "Your booking at {venue} is confirmed" | The booking was confirmed: immediately, by staff, or after the deposit was paid |
| "Your booking at {venue} has been cancelled" | Any cancellation, including an unpaid deposit |

Replies to these emails go to the venue's email address. Staff are **not** emailed about new online bookings, so keep an eye on the board; it updates live.

## 9. Setup and administration (Managers, Admins, Owners)

### Venues (Owner / Admin)

**Venues → Settings** has these tabs:

- **"Details"** — name, time zone (set it correctly; every displayed time depends on it), address and contact details, and the **booking rules**:
  - "Slot interval (min)" — how often start times are offered (15 by default).
  - "Default duration", "Min duration", "Max duration".
  - "Turnover buffer (min)" — cleaning time kept free after each booking.
  - "Min party size" / "Max party size" — the online party-size range.
  - "Min lead time (min)" — minimum notice for online bookings.
  - "Book ahead (days)" — how far ahead guests can book.
  - "Hold time (sec)" — how long a staff booking-in-progress holds a table.
  - "Online cancel cutoff (min)".
  - **"Accept online bookings"** and **"Auto-confirm public bookings"**.
- **"Sections"** — areas inside the venue. A section still used by resources is deactivated instead of deleted.
- **"Opening hours"** — one or more periods per day (e.g. lunch and dinner). A closing time earlier than the opening time runs past midnight and shows a "+1 day" badge. **"Copy to all days"** saves time; the **"Next 14 days"** preview shows the actual hours.
- **"Special days"** — public holidays or events that replace the normal hours for a date: closed all day, or custom hours. For several periods on one date, add one entry per period.
- **"Deposits"** — no deposit, a fixed amount per booking, or an amount per guest, plus the time allowed to pay. Deposits apply to online bookings only.

### Resources (Manager and above)

1. Create **"Types"** first, e.g. TABLE, BOOTH, ROOM.
2. **"Add resource"**: name, code (e.g. T12), type, section, minimum party, capacity and optional attributes (e.g. "window: yes").
3. Turn off **"Bookable online"** for resources kept for staff use; they show as **"Staff only"**.

**Combinations** are resources booked together (e.g. tables pushed together). Members can be required or optional, and the combination has its own party range.

A resource with booking history is deactivated instead of deleted.

### Floor plans (Manager and above)

1. **"New plan"** and set its size.
2. Click resources under **"Not on this plan"** to place them.
3. Drag to move, drag the corner to resize, use the arrow keys to nudge (Shift moves further), and set rotation and shape.
4. **"Save layout"**.

The floor plan powers the **Floor** view on the board.

### Staff (Owner / Admin)

1. **"Add staff"**: email, name, initial password and roles. No invitation email is sent, so give the person the password securely and ask them to change it under **Account**. If the email already has an account (from another workspace), the existing password is kept.
2. For everyone except Owners and Admins, use **"Assign venues"**; otherwise they see no venues.
3. **"Edit roles"** changes what someone can do. Only Owners can grant the Owner role, and there must always be at least one active Owner.
4. **"Deactivate"** removes access immediately and signs the person out.

### Workspace settings (Owner / Admin)

- **"Plan & usage"** shows the limits on venues, active staff and resources per venue:
  - Starter: 1 venue, 5 staff, 30 resources per venue.
  - Professional: 5 venues, 25 staff, 100 resources per venue, custom branding.
  - Enterprise: unlimited, custom branding.

  Reaching a limit only blocks new additions; nothing is removed.
- **"Booking page branding"** (Professional and above): brand colour, logo URL (https), welcome message and booking terms, shown on the public booking page.

### Audit log (Owner / Admin)

A record of who changed what and when, with before and after values. Filter by entity (Booking, Customer, Staff member…), action, or a full entity id.

### Reports (Manager and above)

- Choose a date range, or use the 7 / 30 / 90-day presets. The longest range is 366 days.
- Key figures: bookings, guests, cancellation rate and no-show rate.
- Charts: bookings per day and busiest hours.
- Breakdowns by status and by channel (Phone, Walk-in, Online…), plus deposits collected and refunded.
- **"CSV"** downloads the data for spreadsheets.

## 10. Troubleshooting and FAQ

| Problem | Explanation and fix |
|---|---|
| "That time was just taken" | Someone else booked it first. Choose another time. This is the double-booking protection working |
| "This booking was changed by someone else" | A colleague edited it. The latest version has been loaded; review it and redo your change |
| "The table hold expired" | You took longer than the hold time. Go back to "When" and pick the time again |
| The No-show button is missing | It only appears after the start time, and only for Confirmed bookings |
| I can't block a table | There is a booking in that slot. Move or cancel the booking first |
| I can't add a venue, staff member or resource | The plan limit is reached. Contact the platform team |
| I can't see a venue | You are not assigned to it. Ask an Owner or Admin |
| Guests see "not taking online bookings" | The venue is inactive, "Accept online bookings" is off, or a deposit is required but online payments are not set up |
| The board does not update by itself | Check the live indicator; click it to reconnect, or use the refresh button |
| A guest lost their manage link | Staff can change or cancel the booking for them; the link cannot be resent from the app |

## 11. Hands-on exercises (training environment)

Use the demo workspace with the `host@demo.local`, `staff@demo.local`, `manager@demo.local` and `admin@demo.local` accounts. Ask your administrator for the training password.

1. **Host:** take a phone booking for 4 guests tomorrow at 19:00 with a special request.
2. **Host:** take a walk-in with no details and seat them now.
3. **Staff:** check in, seat and complete a booking. Notice which buttons you do not have.
4. **Host:** drag a booking to another table on the timeline, then open its History tab.
5. **Two trainees:** open the same free slot and both try to book it. One succeeds; the other sees "just taken".
6. **Guest (private browser window):** book online, open "View my booking", and cancel it.
7. **Manager:** block a table for "Cleaning" for an hour, then remove the block.
8. **Admin:** add a staff member, assign a venue, then deactivate them.

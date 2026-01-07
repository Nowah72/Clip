# Clip — Clipboard Manager for Windows

Clip is a modern, lightweight clipboard manager for Windows that helps you **capture**, **store**, **organize**, and **reuse** what you copy — **text** and **images** — with minimal friction.

This README is both a **feature overview** and a **mini user guide**.

---

## Table of Contents
- [At a Glance](#at-a-glance)
- [Tabs](#tabs)
- [Quick Start](#quick-start)
- [Snip](#snip)
- [Groups](#groups)
- [Starred](#starred)
- [Drag & Drop](#drag--drop)
- [How Duplicate Prevention Works](#how-duplicate-prevention-works)
- [Image Handling & Memory Management](#image-handling--memory-management)
- [Tips](#tips)
- [License](#license)

---

## At a Glance

Clip automatically tracks clipboard changes and gives you:
- **History** of copied text and images
- **One-click re-copy** back into the clipboard
- **Groups** for organizing items
- **Starred** items for favorites
- **Snip** tool (screen capture) with quick editing and save options

---

## Tabs

### Text
- Shows all copied text entries
- **Click** an item to copy it back to the clipboard
- Great for snippets, commands, notes, and repeated text

### Images
- Shows copied images as thumbnails
- **Click** an image to copy it back to the clipboard
- Images are deduplicated automatically (no spam duplicates)

### Starred
- Shows all items (text or images) you marked as starred
- Use this for frequently reused content

### Groups
- Create and manage custom groups
- Drag text/images into groups to keep things organized

---

## Quick Start

1. Run the app
2. Copy anything in Windows (Ctrl+C):
   - Text → appears in **Text**
   - Image → appears in **Images**
3. Click an item inside Clip to copy it again
4. Use **Star** to favorite important items
5. Use **Groups** to organize with drag & drop

---

## Snip

Clip includes a built-in **Snip** feature (screen capture):

### Start a snip
- Click the **Snip** button in the top bar
- The main window hides while you capture an area of the screen

### Snip preview
After capturing, Clip shows a preview modal with actions like:
- **Save to File** (exports the snip as an image file)
- **Save to Clip** (adds the snip to your Images history so you can reuse it)
- **Copy** (copies the snip to clipboard immediately)

### Quick editing (before saving)
In the snip preview you can do lightweight edits such as:
- **Rotate** (90°)
- **Flip Horizontal**
- **Flip Vertical**

> Tip: If you close the snip preview without saving/copying, the temporary snip file is cleaned up automatically.

---

## Groups

### Create a group
- Open the **Groups** tab
- If there are no groups yet, you’ll see an empty-state overlay (plus icon)
- Click anywhere in that area to create a new group

### Add items to a group
- Drag an item from **Text** or **Images**
- Drop it into the target group

Groups are perfect for:
- Work / School / Personal separation
- Project-specific snippets
- Frequently reused assets

---

## Starred

- Star any text or image to keep it easily accessible
- The **Starred** tab acts like a “favorites shelf” for your best items

---

## Drag & Drop

Clip intentionally separates **click** vs **drag** behavior so it feels reliable:

- **Click** → copies the item
- **Drag** → organizes the item (to Groups)

To make dropping easier, when you start a real drag action the UI can switch you to the Groups tab so you can see where to drop.

---

## How Duplicate Prevention Works

Clip avoids duplicates by using **hashing**:
- When clipboard content changes, Clip computes a hash based on the content
- If an item with the same hash already exists, it won’t be added again
- This is applied to both text and images

This prevents “history spam” when you copy the same thing repeatedly.

---

## Image Handling & Memory Management

Images can be heavy, so Clip is designed to stay responsive:

### Thumbnails for the UI
- Clip generates and stores a **thumbnail** version for each image
- The UI lists thumbnails (fast to render)
- Full image data is only used when needed (e.g., copy back to clipboard)

### Snip temp files (safe + clean)
- Snips are saved to a temporary file at full quality so preview and export stay crisp
- If you **Save** or **Add to Clip**, the temp file is cleared afterwards
- If you close the preview without action, the temp file is still cleaned up

### Practical behavior
- Images are deduplicated to avoid unnecessary growth
- Clipboard copy-back for images is implemented in a way that avoids re-adding the same image again

---

## Tips

- Use **Starred** for your “top 10” reused items
- Use **Groups** to keep projects clean and separated
- Use **Snip** for quick captures, then “Save to Clip” to reuse later

---

## License

MIT License — free to use, modify, and distribute.

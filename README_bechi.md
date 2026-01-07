# Clip - Modern Clipboard Manager (C# WPF)

A beautiful, modern clipboard manager built with C# and WPF. Features a sleek gradient UI, automatic clipboard monitoring, and persistent storage.

## Features

### Core Features
- **Automatic Clipboard Monitoring** - Captures text and images automatically
- **System Tray Integration** - Runs in background, accessible on-demand
- **Persistent Storage** - Saves all clipboard history between sessions
- **Smart Deduplication** - Uses MD5 hashing to avoid duplicates
- **Three Categories** - Text, Images, and Starred items
- **Unlimited Storage** - Stores all clipboard history

### Modern UI Design
- **Gradient Background** - Beautiful slate theme with smooth gradients
- **Smooth Animations** - Polished transitions and hover effects
- **Modern Typography** - Clean, readable fonts with proper hierarchy
- **Responsive Cards** - Interactive item cards with hover states
- **Empty States** - Helpful messages when tabs are empty

### User Features
- **Search** - Filter text items in real-time
- **Manual Add** - Add text or images manually via file picker
- **Star Items** - Mark important items for quick access
- **Delete Mode** - Select and delete multiple items at once
- **View Metadata** - See detailed information about each item
- **Image Viewer** - Full-screen image preview
- **Copy Feedback** - Visual confirmation when items are copied

### Technical Features
- **Image Compression** - Images compressed to JPEG (75% quality), max 800px
- **Thumbnails** - 80x80 thumbnails for quick preview
- **Rich Metadata** - Tracks dimensions, size, timestamps, and more
- **Auto-hide** - Window hides when it loses focus
- **Bottom-right Positioning** - Opens in convenient screen location

## Requirements

- Windows 10/11
- .NET 6.0 SDK or later
- Visual Studio 2022 (recommended) or Visual Studio Code

## Installation & Build

### Option 1: Using Visual Studio 2022

1. Open Visual Studio 2022
2. Click "Open a project or solution"
3. Navigate to `ClipboardManagerCS` folder
4. Open `ClipboardManagerCS.csproj`
5. Press **F5** to build and run

### Option 2: Using Command Line

1. Open Command Prompt or PowerShell
2. Navigate to the `ClipboardManagerCS` folder:
   ```powershell
   cd C:\Users\David\Desktop\clipboards\ClipboardManagerCS
   ```

3. Restore dependencies:
   ```powershell
   dotnet restore
   ```

4. Build the project:
   ```powershell
   dotnet build
   ```

5. Run the application:
   ```powershell
   dotnet run
   ```

### Option 3: Build Release Version

Build an optimized release version:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

The executable will be in: `bin\Release\net6.0-windows\win-x64\publish\Clip.exe`

## Usage

### Basic Usage
1. **Launch** - Run `Clip.exe` - a window will appear in the bottom-right corner
2. **Copy Anything** - Copy text or images - they're automatically saved
3. **Click to Copy** - Click any item to copy it back to your clipboard
4. **Close** - Click the × button or click outside the window to hide it
5. **Reopen** - Click the system tray icon to show the window again

### Search
- Type in the search box to filter text items in real-time
- Search is case-insensitive
- Only searches text content, not images

### Manual Add
1. Click **"+ Add Item"** button
2. Choose **Text** or **Image**
3. For text: type or paste, then click "Add Text"
4. For images: select an image file from your computer

### Star Items
- Click the **☆** button on any item to star it
- Starred items appear in the **Starred** tab
- Click **⭐** again to unstar

### Delete Mode
1. Click the **🗑️** button in the header
2. Click items to select them (they turn red)
3. Click **"Delete Selected"** button
4. Click **×** to exit delete mode

### View Metadata
- Click the **📊** button on any item
- See detailed information:
  - **Text**: character count, word count, lines, size
  - **Images**: dimensions, format, compression details

### View Images
- Click the **👁** button on image items
- Opens a full-size image viewer window

## Data Storage

All clipboard data is stored in:
```
%USERPROFILE%\.clipboard_manager\data.json
```

You can delete this file to clear all history.

## Project Structure

```
ClipboardManagerCS/
├── App.xaml                    # Application definition
├── App.xaml.cs                 # Application logic
├── MainWindow.xaml             # Main UI (XAML)
├── MainWindow.xaml.cs          # Main window logic
├── Models/
│   └── ClipboardItem.cs        # Data models
├── ClipboardManagerCS.csproj   # Project file
└── README.md                   # This file
```

## Dependencies

- **Newtonsoft.Json** (13.0.3) - JSON serialization for data persistence
- **.NET 6.0 Windows Desktop** - WPF framework

## Color Scheme

The application uses a modern slate color palette:

- Primary Background: `#0F172A` (Slate 950)
- Secondary Background: `#1E293B` (Slate 900)
- Borders: `#334155` (Slate 700)
- Text Primary: `#FFFFFF` (White)
- Text Secondary: `#94A3B8` (Slate 400)
- Accent Blue: `#3B82F6`
- Accent Purple: `#8B5CF6`
- Accent Teal: `#0D7377`
- Success Green: `#10B981`
- Danger Red: `#DC2626`

## Keyboard Shortcuts

Currently, the application uses mouse/click interactions. Future versions may add:
- `Ctrl+Shift+V` - Show clipboard manager
- `Escape` - Hide window
- Arrow keys for navigation

## Known Limitations

- Windows-only (uses WPF)
- No global hotkey (requires system tray click)
- No clipboard sync across devices
- Maximum recommended items: ~1000 (performance may degrade with very large collections)

## Troubleshooting

### App won't start
- Ensure .NET 6.0 Runtime is installed
- Check Windows Event Viewer for errors

### Clipboard not monitoring
- Restart the application
- Check if another clipboard manager is running

### Images not saving
- Ensure sufficient disk space
- Check write permissions to `%USERPROFILE%\.clipboard_manager\`

### High memory usage
- Clear old items using delete mode
- Images are compressed but still use memory

## Future Enhancements

- Global hotkey support
- Cloud sync
- More export formats
- Text formatting preservation
- Clipboard history search improvements
- Tags and categories
- Keyboard navigation

## License

This is a personal project. Use freely for personal or commercial purposes.

## Credits

Built with:
- C# / .NET 6.0
- Windows Presentation Foundation (WPF)
- Newtonsoft.Json

Inspired by modern web design patterns and the Clipboard API.

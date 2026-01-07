using System;
using System.Collections.Generic;

namespace ClipboardManagerCS.Models
{
    public class ClipboardItem
    {
        public string Type { get; set; } = string.Empty; // "text" or "image"
        public string Hash { get; set; } = string.Empty;
        public string Preview { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string? Thumbnail { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsStarred { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
        public string? GroupId { get; set; } // For grouping items
    }

    public class ClipboardGroup
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "New Group";
        public string Icon { get; set; } = "\uE8B7"; // Default folder icon
        public string Color { get; set; } = "#A855F7"; // Default purple
        public List<string> ItemHashes { get; set; } = new(); // References to items by hash
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public bool IsExpanded { get; set; } = true;
    }

    public class ClipboardData
    {
        public List<ClipboardItem> Text { get; set; } = new();
        public List<ClipboardItem> Images { get; set; } = new();
        public List<ClipboardGroup> Groups { get; set; } = new();
    }
}

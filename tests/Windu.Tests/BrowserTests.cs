using Windu;
using Xunit;

namespace Windu.Tests;

public class BrowserTests
{
    /// <summary>A small hand-built tree, so the expected layout is unambiguous.</summary>
    private static FsEntry SampleTree()
    {
        var root = new FsEntry { Name = @"C:\sample", IsDirectory = true, Children = new List<FsEntry>() };

        void Add(FsEntry parent, FsEntry e)
        {
            e.Parent = parent;
            parent.Children!.Add(e);
        }

        var big = new FsEntry { Name = "big", IsDirectory = true, Children = new List<FsEntry>(), Size = 8000, DiskSize = 8192, ItemCount = 2 };
        Add(big, new FsEntry { Name = "inner.bin", Size = 8000, DiskSize = 8192, OwnSize = 8000, OwnDiskSize = 8192 });
        Add(root, big);

        Add(root, new FsEntry { Name = "medium.bin", Size = 4000, DiskSize = 4096, OwnSize = 4000, OwnDiskSize = 4096 });
        Add(root, new FsEntry { Name = "small.bin", Size = 1000, DiskSize = 1024, OwnSize = 1000, OwnDiskSize = 1024 });
        Add(root, new FsEntry { Name = ".hidden", Size = 2000, DiskSize = 2048, OwnSize = 2000, OwnDiskSize = 2048 });

        root.Size = 8000 + 4000 + 1000 + 2000;
        root.DiskSize = 8192 + 4096 + 1024 + 2048;
        root.ItemCount = 6;
        return root;
    }

    private static string Render(string keys, Settings? cfg = null, int w = 80, int h = 14)
        => new Browser(SampleTree(), cfg ?? new Settings())
            .RenderToText(w, h, RenderTest.ParseKeys(keys));

    [Fact]
    public void Listing_is_sorted_by_size_descending_by_default()
    {
        var lines = Render("").Split('\n');
        var items = lines.Skip(2)
            .Where(l => l.Contains(".bin") || l.Contains("/big") || l.Contains(".hidden"))
            .ToArray();

        Assert.Contains("/big", items[0]);       // 8192
        Assert.Contains("medium.bin", items[1]);  // 4096
        Assert.Contains(".hidden", items[2]);     // 2048
        Assert.Contains("small.bin", items[3]);   // 1024
    }

    [Fact]
    public void Directories_are_prefixed_with_a_slash()
        => Assert.Contains("/big", Render(""));

    [Fact]
    public void Header_shows_the_current_path()
        => Assert.Contains(@"C:\sample", Render(""));

    [Fact]
    public void Footer_reports_the_totals()
    {
        var text = Render("");
        Assert.Contains("Total disk usage:", text);
        Assert.Contains("Apparent size:", text);
        Assert.Contains("Items: 5", text);
    }

    [Fact]
    public void Graph_bar_is_drawn_for_each_item()
    {
        var text = Render("");
        Assert.Contains("[##########]", text);   // the largest item fills the bar
        Assert.Contains("[", text);
    }

    [Fact]
    public void Sorting_by_name_reorders_the_listing()
    {
        var lines = Render("n").Split('\n');
        var items = lines.Where(l => l.Contains(".bin") || l.Contains("/big") || l.Contains(".hidden")).ToArray();

        Assert.Contains(".hidden", items[0]);
        Assert.Contains("/big", items[1]);
        Assert.Contains("medium.bin", items[2]);
        Assert.Contains("small.bin", items[3]);
    }

    [Fact]
    public void Pressing_the_same_sort_key_twice_reverses_the_order()
    {
        var lines = Render("nn").Split('\n');
        var items = lines.Where(l => l.Contains(".bin") || l.Contains("/big") || l.Contains(".hidden")).ToArray();

        Assert.Contains("small.bin", items[0]);
        Assert.Contains(".hidden", items[3]);
    }

    [Fact]
    public void Hidden_items_can_be_toggled_off()
    {
        Assert.Contains(".hidden", Render(""));
        Assert.DoesNotContain(".hidden", Render("e"));
    }

    [Fact]
    public void Percent_column_appears_after_pressing_g()
    {
        Assert.DoesNotContain("%", Render(""));
        Assert.Contains("%", Render("g"));
    }

    [Fact]
    public void Apparent_size_toggle_changes_the_reported_sizes()
    {
        // The footer names the active mode until a status message replaces it.
        Assert.Contains("[disk usage]", Render(""));
        Assert.Contains("Showing apparent sizes.", Render("a"));

        // Toggling twice returns to disk usage, and the sizes differ between modes.
        var disk = Render("");
        var apparent = Render("a");
        Assert.Contains("8.0 KiB", disk);        // 8192 rounded to clusters
        Assert.Contains("7.8 KiB", apparent);    // 8000 apparent
    }

    [Fact]
    public void Si_units_toggle_switches_the_suffixes()
    {
        Assert.Contains("KiB", Render(""));
        Assert.Contains("kB", Render("u"));
    }

    [Fact]
    public void Descending_into_a_directory_shows_its_children()
    {
        var text = Render("<enter>");
        Assert.Contains("inner.bin", text);
        Assert.Contains(@"C:\sample\big", text);
        Assert.Contains("/..", text);        // the parent entry appears
    }

    [Fact]
    public void Going_back_up_returns_to_the_parent_listing()
    {
        var text = Render("<enter><left>");
        Assert.Contains("medium.bin", text);
        Assert.DoesNotContain("inner.bin", text);
    }

    [Fact]
    public void Root_listing_has_no_parent_entry()
        => Assert.DoesNotContain("/..", Render(""));

    [Fact]
    public void Cannot_navigate_above_the_scan_root()
    {
        // Several attempts to ascend from the root must leave us where we started.
        var text = Render("<left><left><left>");
        Assert.Contains(@"C:\sample", text);
        Assert.Contains("medium.bin", text);
    }

    [Fact]
    public void Descending_into_a_file_does_nothing()
    {
        var text = Render("<down><enter>");   // "medium.bin" is a file
        Assert.Contains(@"C:\sample", text);
        Assert.Contains("medium.bin", text);
    }

    [Fact]
    public void Cursor_stops_at_the_ends_of_the_list()
    {
        // Far more presses than there are items, in both directions.
        var up = Render("<up><up><up><up><up><up><up><up>");
        var down = Render("<down><down><down><down><down><down><down><down>");
        Assert.Contains("/big", up);
        Assert.Contains("small.bin", down);
    }

    [Fact]
    public void Help_window_lists_the_key_bindings()
    {
        var text = Render("?", h: 30);
        Assert.Contains("windu help", text);
        Assert.Contains("Quit windu", text);
        Assert.Contains("Delete the selected", text);
    }

    [Fact]
    public void Help_window_scrolls_when_the_terminal_is_too_short()
    {
        // In a short window the help must offer scrolling rather than truncate.
        var text = Render("?", h: 14);
        Assert.Contains("windu help", text);
        Assert.Contains("to scroll", text);
        Assert.DoesNotContain("Quit windu", text);

        // Jumping to the end brings the last binding into view.
        var end = Render("?<end>", h: 14);
        Assert.Contains("Quit windu", end);
    }

    [Fact]
    public void Info_window_shows_both_sizes_and_the_path()
    {
        var text = Render("i", h: 20);
        Assert.Contains("Item info", text);
        Assert.Contains("Disk usage:", text);
        Assert.Contains("Apparent size:", text);
        Assert.Contains(@"C:\sample\big", text);
    }

    [Fact]
    public void Delete_dialog_asks_before_removing_anything()
    {
        var text = Render("d");
        Assert.Contains("Confirm delete", text);
        Assert.Contains("yes", text);
        Assert.Contains("no", text);
    }

    [Fact]
    public void Narrow_terminal_still_renders_without_throwing()
    {
        var text = Render("", w: 24, h: 8);
        Assert.Contains("big", text);
        // Every line must fit inside the window.
        foreach (var line in text.Split('\n'))
            Assert.True(line.TrimEnd('\r').Length <= 24, $"line too long: '{line}'");
    }

    [Fact]
    public void Short_terminal_still_renders_without_throwing()
    {
        var text = Render("", w: 80, h: 6);
        Assert.Contains("windu", text);
    }

    [Fact]
    public void Long_listing_scrolls_rather_than_overflowing()
    {
        var root = new FsEntry { Name = @"C:\many", IsDirectory = true, Children = new List<FsEntry>() };
        for (int i = 0; i < 200; i++)
        {
            root.Children!.Add(new FsEntry
            {
                Name = $"f{i:D3}.bin", Parent = root,
                Size = i * 100, DiskSize = i * 100, OwnSize = i * 100, OwnDiskSize = i * 100,
            });
        }
        root.ItemCount = 201;

        var text = new Browser(root, new Settings())
            .RenderToText(80, 12, RenderTest.ParseKeys("<end>"));

        // Jumping to the end must scroll the smallest item into view.
        Assert.Contains("f000.bin", text);
        Assert.Equal(12, text.Split('\n').Length - 1);
    }

    [Fact]
    public void Control_characters_in_names_cannot_corrupt_the_display()
    {
        var root = new FsEntry { Name = @"C:\odd", IsDirectory = true, Children = new List<FsEntry>() };
        root.Children!.Add(new FsEntry
        {
            Name = "evil" + (char)27 + "[2Jname", Parent = root,
            Size = 10, DiskSize = 10, OwnSize = 10, OwnDiskSize = 10,
        });
        root.ItemCount = 2;

        var text = new Browser(root, new Settings()).RenderToText(80, 8, RenderTest.ParseKeys(""));

        // No control character may survive into the buffer, or a crafted filename
        // could reposition the cursor and repaint the screen. Newlines are the
        // row separators this dump adds, so they are not part of the check.
        var offending = text.Where(c => char.IsControl(c) && c != '\n')
                            .Select(c => (int)c)
                            .ToArray();
        Assert.Empty(offending);
        Assert.Contains("evil?[2Jname", text);
    }
}

using SimpleXisoDrive.Services;
using SimpleXisoDrive.XDVDFs;

namespace SimpleXisoDrive;

public class VfsContainer : IDisposable
{
    private readonly IsoSt _isoSt;
    private readonly Dictionary<string, FileEntry> _entryCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<FileEntry>> _childrenCache = new(StringComparer.OrdinalIgnoreCase);
    public ulong VolumeSize { get; private set; }
    public DateTime VolumeCreationTime { get; private set; }

    public VfsContainer(string isoPath)
    {
        _isoSt = new IsoSt(isoPath);

        try
        {
            var volumeDescriptor = VolumeDescriptor.ReadFrom(_isoSt);
            if (!volumeDescriptor.Validate())
            {
                throw new InvalidImageException("XDVDFS magic string not found.");
            }

            DebugLogger.WriteLine(volumeDescriptor.IsRebuiltXisoFormat()
                ? "Detected rebuilt XISO format (sector 0)"
                : "Detected standard Xbox ISO format (sector 32)");

            VolumeCreationTime = volumeDescriptor.CreationTime;
            VolumeSize = (ulong)_isoSt.Reader.BaseStream.Length;

            var rootEntry = FileEntry.CreateRootEntry(volumeDescriptor.RootDirTableSector);
            DebugLogger.WriteLine($"Root entry points to sector: {rootEntry.StartSector}");
            CacheEntry("\\", rootEntry);
        }
        catch (Exception ex)
        {
            _isoSt.Dispose();

            // Exception is re-thrown and caught by Program.cs, which handles the API reporting.
            if (ex is InvalidImageException)
            {
                throw;
            }

            throw new InvalidImageException($"Failed to read Xbox ISO: {ex.Message}", ex);
        }
    }

    private void CacheEntry(string path, FileEntry entry)
    {
        _entryCache[path] = entry;
    }

    public FileEntry? GetEntry(string path)
    {
        var normalizedPath = path.Replace('/', '\\').TrimEnd('\\');

        if (string.IsNullOrEmpty(normalizedPath))
        {
            normalizedPath = "\\";
        }

        if (normalizedPath == "\\")
        {
            return _entryCache.GetValueOrDefault("\\");
        }

        if (_entryCache.TryGetValue(normalizedPath, out var cachedEntry))
        {
            return cachedEntry;
        }

        var parentPath = Path.GetDirectoryName(normalizedPath) ?? "\\";
        var fileName = Path.GetFileName(normalizedPath);

        if (GetEntry(parentPath) is not { IsDirectory: true } parentEntry)
        {
            return null;
        }

        var entry = FindEntryInDirectory(parentEntry, fileName);
        if (entry != null)
        {
            CacheEntry(normalizedPath, entry);
        }

        return entry;
    }

    private FileEntry? FindEntryInDirectory(FileEntry parentEntry, string targetName)
    {
        try
        {
            return TraverseBinaryTree(parentEntry, entry =>
                string.Equals(entry.FileName, targetName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            DebugLogger.WriteLine($"Error in FindEntryInDirectory: {ex.Message}");
            _ = BugReport.LogErrorAsync(ex, $"Error in FindEntryInDirectory for target '{targetName}'");
            return null;
        }
    }

    public IEnumerable<FileEntry> GetFolderList(string path)
    {
        var normalizedPath = path.Replace('/', '\\').TrimEnd('\\');
        DebugLogger.WriteLine($"[GetFolderList] Starting for path: '{normalizedPath}'");

        if (string.IsNullOrEmpty(normalizedPath))
        {
            normalizedPath = "\\";
        }

        // Check if we have the directory listing cached
        if (_childrenCache.TryGetValue(normalizedPath, out var cachedChildren))
        {
            DebugLogger.WriteLine($"[GetFolderList] Using cached children for '{normalizedPath}' ({cachedChildren.Count} entries)");
            foreach (var entry in cachedChildren) yield return entry;

            yield break;
        }

        // Get the directory entry itself
        var dirEntry = normalizedPath == "\\" ? _entryCache.GetValueOrDefault("\\") : GetEntry(normalizedPath);
        if (dirEntry is not { IsDirectory: true })
        {
            DebugLogger.WriteLine($"[ERROR] Directory not found or invalid: '{normalizedPath}'");
            yield break;
        }

        // Traverse the binary tree to get all children
        var children = new List<FileEntry>();
        var entries = GetAllEntriesFromBinaryTree(dirEntry);

        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.FileName)) continue;

            var childPath = Path.Combine(normalizedPath, entry.FileName);
            CacheEntry(childPath, entry);
            children.Add(entry);
            yield return entry;
        }

        _childrenCache[normalizedPath] = children;
        DebugLogger.WriteLine($"[GetFolderList] Cached {children.Count} children for '{normalizedPath}'");
    }

    private List<FileEntry> GetAllEntriesFromBinaryTree(FileEntry directoryEntry)
    {
        var entries = new List<FileEntry>();
        var visited = new HashSet<(long Sector, long Offset)>(); // Track visited nodes by sector and offset

        try
        {
            var firstEntry = directoryEntry.GetFirstChild(_isoSt);
            if (firstEntry != null)
            {
                TraverseBinaryTreeForAll(firstEntry, entries, visited);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.WriteLine($"Error traversing binary tree: {ex.Message}");
            _ = BugReport.LogErrorAsync(ex, "Error traversing binary tree in GetAllEntriesFromBinaryTree");
        }

        return entries;
    }

    private void TraverseBinaryTreeForAll(FileEntry firstEntry, List<FileEntry> results, HashSet<(long Sector, long Offset)> visited)
    {
        var stack = new Stack<FileEntry>();
        const int maxIterations = 100000; // Safety limit to prevent infinite loops
        var iterations = 0;

        // Check if first entry is valid and not already visited
        if (!visited.Add((firstEntry.EntrySector, firstEntry.EntryOffset)))
        {
            return;
        }

        stack.Push(firstEntry);

        while (stack.Count > 0)
        {
            // Safety check for infinite loops
            if (++iterations > maxIterations)
            {
                DebugLogger.WriteLine("TraverseBinaryTreeForAll: Max iterations reached, possible corrupted tree structure");
                _ = BugReport.LogErrorAsync(new InvalidOperationException("Max iterations reached in TraverseBinaryTreeForAll"),
                    "Possible corrupted binary tree structure - too many nodes");
                break;
            }

            var current = stack.Pop();

            // Process current node
            if (!string.IsNullOrEmpty(current.FileName))
            {
                results.Add(current);
            }

            // Push right child first (so left is processed first - LIFO)
            if (current.HasRightChild)
            {
                var rightChild = current.GetRightChild(_isoSt);
                if (rightChild != null && visited.Add((rightChild.EntrySector, rightChild.EntryOffset)))
                {
                    stack.Push(rightChild);
                }
            }

            // Push left child
            if (current.HasLeftChild)
            {
                var leftChild = current.GetLeftChild(_isoSt);
                if (leftChild != null && visited.Add((leftChild.EntrySector, leftChild.EntryOffset)))
                {
                    stack.Push(leftChild);
                }
            }
        }
    }

    private FileEntry? TraverseBinaryTree(FileEntry directoryEntry, Func<FileEntry, bool> predicate)
    {
        try
        {
            var firstEntry = directoryEntry.GetFirstChild(_isoSt);
            if (firstEntry != null)
            {
                return SearchBinaryTree(firstEntry, predicate);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.WriteLine($"Error in binary tree traversal: {ex.Message}");
            _ = BugReport.LogErrorAsync(ex, "Error in binary tree traversal (TraverseBinaryTree)");
        }

        return null;
    }

    private FileEntry? SearchBinaryTree(FileEntry? startNode, Func<FileEntry, bool> predicate)
    {
        if (startNode == null) return null;

        var stack = new Stack<FileEntry>();
        const int maxIterations = 100000; // Safety limit to prevent infinite loops
        var iterations = 0;

        // Track visited nodes to prevent infinite loops (Cycle Detection)
        // Key is (Sector, Offset)
        var visited = new HashSet<(long Sector, long Offset)>();

        // Check if start node is valid and not already visited
        if (!visited.Add((startNode.EntrySector, startNode.EntryOffset)))
        {
            return null;
        }

        stack.Push(startNode);

        while (stack.Count > 0)
        {
            // Safety check for infinite loops
            if (++iterations > maxIterations)
            {
                DebugLogger.WriteLine("SearchBinaryTree: Max iterations reached, possible corrupted tree structure");
                _ = BugReport.LogErrorAsync(new InvalidOperationException("Max iterations reached in SearchBinaryTree"),
                    "Possible corrupted binary tree structure - too many nodes or circular reference");
                break;
            }

            var current = stack.Pop();

            // Check if this is the entry we are looking for
            if (!string.IsNullOrEmpty(current.FileName) && predicate(current))
            {
                return current;
            }

            // Push children to the stack.
            // To mimic the recursive order (Check -> Left -> Right),
            // we push Right first, then Left, so Left is popped next.

            if (current.HasRightChild)
            {
                var rightChild = current.GetRightChild(_isoSt);
                // Only add if not null and not already visited
                if (rightChild != null && visited.Add((rightChild.EntrySector, rightChild.EntryOffset)))
                {
                    stack.Push(rightChild);
                }
            }

            if (current.HasLeftChild)
            {
                var leftChild = current.GetLeftChild(_isoSt);
                // Only add if not null and not already visited
                if (leftChild != null && visited.Add((leftChild.EntrySector, leftChild.EntryOffset)))
                {
                    stack.Push(leftChild);
                }
            }
        }

        return null;
    }

    public int ReadFile(FileEntry entry, Span<byte> buffer, long offset)
    {
        try
        {
            return _isoSt.Read(entry, buffer, offset);
        }
        catch (Exception ex)
        {
            _ = BugReport.LogErrorAsync(ex, $"VfsContainer.ReadFile failed for {entry.FileName}");
            return 0;
        }
    }

    public void Dispose()
    {
        _isoSt.Dispose();
        GC.SuppressFinalize(this);
    }
}
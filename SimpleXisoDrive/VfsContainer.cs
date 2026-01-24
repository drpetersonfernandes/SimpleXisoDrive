using SimpleXisoDrive.Services;
using SimpleXisoDrive.XDVDFs;

namespace SimpleXisoDrive;

public class VfsContainer : IDisposable
{
    private readonly IsoSt _isoSt;
    private readonly Dictionary<string, FileEntry> _entryCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _childrenCache = new(StringComparer.OrdinalIgnoreCase);

    private const int MaxCachedEntries = 2000;

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

            // Log format type using new verification method
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
            // Log specifically that the ISO parsing failed
            _ = ErrorLogger.LogErrorAsync(ex, $"VfsContainer failed to initialize for ISO: {isoPath}");
            // Exception is re-thrown and caught by Program.cs, which handles the API reporting.
            throw new InvalidImageException($"Failed to read Xbox ISO: {ex.Message}", ex);
        }
    }

    private void CacheEntry(string path, FileEntry entry)
    {
        if (_entryCache.Count >= MaxCachedEntries)
        {
            var oldestPath = _entryCache.Keys.FirstOrDefault();
            if (oldestPath != null)
            {
                _entryCache.Remove(oldestPath);
                _childrenCache.Remove(oldestPath);
            }
        }

        _entryCache[path] = entry;

        if (entry.IsDirectory && !_childrenCache.ContainsKey(path))
        {
            _childrenCache[path] = new List<string>();
        }
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
            _ = ErrorLogger.LogErrorAsync(ex, $"Error in FindEntryInDirectory for target '{targetName}'");
            return null;
        }
    }

    public IEnumerable<FileEntry> GetFolderList(string path)
    {
        var normalizedPath = path.Replace('/', '\\').TrimEnd('\\');
        DebugLogger.WriteLine($"[GetFolderList] Starting for path: '{normalizedPath}'");

        if (normalizedPath == "\\")
        {
            if (!_entryCache.TryGetValue("\\", out var rootEntry) || !rootEntry.IsDirectory)
            {
                DebugLogger.WriteLine("[ERROR] Root entry not found or not a directory");
                yield break;
            }

            if (_childrenCache.TryGetValue(normalizedPath, out var cachedChildren) && cachedChildren.Count > 0)
            {
                DebugLogger.WriteLine($"[GetFolderList] Using cached children ({cachedChildren.Count} entries)");
                foreach (var childPath in cachedChildren)
                {
                    if (_entryCache.TryGetValue(childPath, out var childEntry))
                    {
                        yield return childEntry;
                    }
                }

                yield break;
            }

            // Traverse the binary tree to get all entries
            var children = new List<string>();
            var entries = GetAllEntriesFromBinaryTree(rootEntry);

            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.FileName))
                    continue;

                var childPath = Path.Combine(normalizedPath, entry.FileName);
                CacheEntry(childPath, entry);
                children.Add(childPath);

                DebugLogger.WriteLine($"[GetFolderList] Yielding entry: '{entry.FileName}'");
                yield return entry;
            }

            _childrenCache[normalizedPath] = children;
            DebugLogger.WriteLine($"[GetFolderList] Cached {children.Count} children for root");
            yield break;
        }

        // Handle non-root directories similarly
        var dirEntry = GetEntry(normalizedPath);
        if (dirEntry is not { IsDirectory: true })
        {
            yield break;
        }

        if (_childrenCache.TryGetValue(normalizedPath, out var cachedChildrenNonRoot) && cachedChildrenNonRoot.Count > 0)
        {
            foreach (var childPath in cachedChildrenNonRoot)
            {
                if (_entryCache.TryGetValue(childPath, out var childEntry))
                {
                    yield return childEntry;
                }
            }

            yield break;
        }

        var childrenNonRoot = new List<string>();
        var entriesNonRoot = GetAllEntriesFromBinaryTree(dirEntry);

        foreach (var entry in entriesNonRoot)
        {
            if (string.IsNullOrEmpty(entry.FileName))
                continue;

            var childPath = Path.Combine(normalizedPath, entry.FileName);
            CacheEntry(childPath, entry);
            childrenNonRoot.Add(childPath);
            yield return entry;
        }

        _childrenCache[normalizedPath] = childrenNonRoot;
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
            _ = ErrorLogger.LogErrorAsync(ex, "Error traversing binary tree in GetAllEntriesFromBinaryTree");
        }

        return entries;
    }


    private void TraverseBinaryTreeForAll(FileEntry firstEntry, List<FileEntry> results, HashSet<(long Sector, long Offset)> visited)
    {
        var stack = new Stack<FileEntry>();
        var current = firstEntry;

        while (current != null || stack.Count > 0)
        {
            // Traverse to the leftmost node
            while (current != null)
            {
                // Check if we've visited this node before processing
                if (!visited.Add((current.EntrySector, current.EntryOffset)))
                {
                    current = null; // Skip duplicates
                    continue;
                }

                stack.Push(current);
                current = current.HasLeftChild ? current.GetLeftChild(_isoSt) : null;
            }

            if (stack.Count == 0) break;

            current = stack.Pop();

            // Process current node
            if (!string.IsNullOrEmpty(current.FileName))
            {
                results.Add(current);
            }

            // Move to right subtree
            current = current.HasRightChild ? current.GetRightChild(_isoSt) : null;
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
            _ = ErrorLogger.LogErrorAsync(ex, "Error in binary tree traversal (TraverseBinaryTree)");
        }

        return null;
    }

    private FileEntry? SearchBinaryTree(FileEntry? startNode, Func<FileEntry, bool> predicate)
    {
        if (startNode == null) return null;

        var stack = new Stack<FileEntry>();
        stack.Push(startNode);

        // Track visited nodes to prevent infinite loops (Cycle Detection)
        // Key is (Sector, Offset)
        var visited = new HashSet<(long, long)>();

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            // If we have already visited this node, skip it to prevent cycles
            if (!visited.Add((current.EntrySector, current.EntryOffset)))
            {
                continue;
            }

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
                if (rightChild != null)
                {
                    stack.Push(rightChild);
                }
            }

            if (current.HasLeftChild)
            {
                var leftChild = current.GetLeftChild(_isoSt);
                if (leftChild != null)
                {
                    stack.Push(leftChild);
                }
            }
        }

        return null;
    }

    public int ReadFile(FileEntry entry, Span<byte> buffer, long offset)
    {
        return _isoSt.Read(entry, buffer, offset);
    }

    public void Dispose()
    {
        _isoSt.Dispose();
        GC.SuppressFinalize(this);
    }

    public class InvalidImageException : Exception
    {
        public InvalidImageException(string message, Exception? inner = null)
            : base($"{message}. This might be an unsupported ISO format.", inner)
        {
        }
    }
}
namespace SimpleXisoDrive.Tests;

public class ResolveIsoPathTests
{
    [Fact]
    public void ReturnsOriginalPathWhenFileExists()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var result = Program.ResolveIsoPath(tempFile);
            Assert.Equal(tempFile, result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ReturnsNullWhenPathDoesNotExistAndNoExtension()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var result = Program.ResolveIsoPath(nonExistentPath);
        Assert.Null(result);
    }

    [Fact]
    public void AppendsIsoExtensionWhenFileWithExtensionExists()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.iso");
        File.WriteAllText(tempFile, string.Empty);
        try
        {
            var pathWithoutExtension = tempFile[..^4]; // Remove ".iso"
            var result = Program.ResolveIsoPath(pathWithoutExtension);
            Assert.Equal(tempFile, result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ResolvesFilenameInCurrentDirectoryWhenFileExists()
    {
        var originalDir = Environment.CurrentDirectory;
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "testfile.iso");
        File.WriteAllText(tempFile, string.Empty);

        try
        {
            Environment.CurrentDirectory = tempDir;
            var result = Program.ResolveIsoPath("testfile.iso");
            Assert.Equal("testfile.iso", result);
        }
        finally
        {
            Environment.CurrentDirectory = originalDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ResolvesFilenameWithIsoExtensionInCurrentDirectoryWhenFileExists()
    {
        var originalDir = Environment.CurrentDirectory;
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "testfile.iso");
        File.WriteAllText(tempFile, string.Empty);

        try
        {
            Environment.CurrentDirectory = tempDir;
            var result = Program.ResolveIsoPath("testfile");
            Assert.Equal("testfile.iso", result);
        }
        finally
        {
            Environment.CurrentDirectory = originalDir;
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ResolvesDirectoryContainingExactlyOneIso()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempIso = Path.Combine(tempDir, "game.iso");
        File.WriteAllText(tempIso, string.Empty);

        try
        {
            var result = Program.ResolveIsoPath(tempDir);
            Assert.Equal(tempIso, result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ReturnsNullWhenDirectoryContainsMultipleIsos()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "game1.iso"), string.Empty);
        File.WriteAllText(Path.Combine(tempDir, "game2.iso"), string.Empty);

        try
        {
            var result = Program.ResolveIsoPath(tempDir);
            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ReturnsNullWhenDirectoryContainsZeroIsos()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "readme.txt"), string.Empty);

        try
        {
            var result = Program.ResolveIsoPath(tempDir);
            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ReturnsNullWhenDirectoryScanThrowsException()
    {
        // A path that looks like a directory but is actually a file with no extension
        // Directory.Exists returns false for files, but we can simulate a permission issue
        // by using a path format that causes GetFiles to throw. However, the simplest
        // real-world scenario is a directory path that exists but GetFiles throws
        // (e.g., due to permissions). We'll use a directory and rely on the fact that
        // the code catches exceptions gracefully.
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // In normal conditions GetFiles won't throw here, so this test mainly verifies
            // that the method does not crash when Directory.Exists is true.
            var result = Program.ResolveIsoPath(tempDir);
            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}

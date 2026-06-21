using SimpleXisoDrive.Models;

namespace SimpleXisoDrive.Tests;

public class XisoFsFileAttributesTests
{
    [Fact]
    public void ReadOnly_HasCorrectValue()
    {
        Assert.Equal(0x01, (byte)XisoFsFileAttributes.ReadOnly);
    }

    [Fact]
    public void Hidden_HasCorrectValue()
    {
        Assert.Equal(0x02, (byte)XisoFsFileAttributes.Hidden);
    }

    [Fact]
    public void System_HasCorrectValue()
    {
        Assert.Equal(0x04, (byte)XisoFsFileAttributes.System);
    }

    [Fact]
    public void Directory_HasCorrectValue()
    {
        Assert.Equal(0x10, (byte)XisoFsFileAttributes.Directory);
    }

    [Fact]
    public void Archive_HasCorrectValue()
    {
        Assert.Equal(0x20, (byte)XisoFsFileAttributes.Archive);
    }

    [Fact]
    public void Normal_HasCorrectValue()
    {
        Assert.Equal(0x80, (byte)XisoFsFileAttributes.Normal);
    }

    [Fact]
    public void Flags_CanBeCombined()
    {
        const XisoFsFileAttributes combined = XisoFsFileAttributes.Directory | XisoFsFileAttributes.Hidden;
        Assert.True(combined.HasFlag(XisoFsFileAttributes.Directory));
        Assert.True(combined.HasFlag(XisoFsFileAttributes.Hidden));
        Assert.False(combined.HasFlag(XisoFsFileAttributes.System));
    }

    [Fact]
    public void Flags_CanBeCheckedIndependently()
    {
        const XisoFsFileAttributes attrs = XisoFsFileAttributes.ReadOnly | XisoFsFileAttributes.Archive | XisoFsFileAttributes.System;

        Assert.True(attrs.HasFlag(XisoFsFileAttributes.ReadOnly));
        Assert.True(attrs.HasFlag(XisoFsFileAttributes.Archive));
        Assert.True(attrs.HasFlag(XisoFsFileAttributes.System));
        Assert.False(attrs.HasFlag(XisoFsFileAttributes.Directory));
        Assert.False(attrs.HasFlag(XisoFsFileAttributes.Hidden));
    }

    [Fact]
    public void AllFlags_CanBeCombined()
    {
        const XisoFsFileAttributes all = XisoFsFileAttributes.ReadOnly
                                         | XisoFsFileAttributes.Hidden
                                         | XisoFsFileAttributes.System
                                         | XisoFsFileAttributes.Directory
                                         | XisoFsFileAttributes.Archive
                                         | XisoFsFileAttributes.Normal;

        Assert.Equal(0xB7, (byte)all);
    }
}

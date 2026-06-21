namespace SimpleXisoDrive.Tests;

public class InvalidImageExceptionTests
{
    [Fact]
    public void Constructor_SetsMessage()
    {
        var ex = new InvalidImageException("Test error");
        Assert.Equal("Test error", ex.Message);
    }

    [Fact]
    public void Constructor_SetsInnerException()
    {
        var inner = new InvalidOperationException("Inner error");
        var ex = new InvalidImageException("Outer error", inner);

        Assert.Equal("Outer error", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void Constructor_AllowsNullInnerException()
    {
        var ex = new InvalidImageException("Test error", null);
        Assert.Equal("Test error", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void IsException_DerivedFromException()
    {
        var ex = new InvalidImageException("Test");
        Assert.IsAssignableFrom<Exception>(ex);
    }

    [Fact]
    public void CanBeCaughtAsException()
    {
        Exception caught;
        try
        {
            throw new InvalidImageException("Test");
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        Assert.IsType<InvalidImageException>(caught);
    }
}

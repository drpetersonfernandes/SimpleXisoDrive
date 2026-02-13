namespace SimpleXisoDrive;

public class InvalidImageException : Exception
{
    public InvalidImageException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
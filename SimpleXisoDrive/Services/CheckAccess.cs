namespace SimpleXisoDrive.Services;

/// <summary>
/// Provides helper methods to check access permissions for the current user.
/// </summary>
public static class CheckAccess
{
    /// Determines whether the current user has administrator privileges.
    /// The method evaluates the current Windows identity and checks if it is
    /// assigned to the built-in administrator role in the operating system.
    /// <returns>
    /// True if the current user is in the administrator role; false otherwise or in case of an error.
    /// </returns>
    public static bool IsAdministrator()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
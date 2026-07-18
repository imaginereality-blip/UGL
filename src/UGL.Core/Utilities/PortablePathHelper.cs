namespace UGL.Core.Utilities;

/// <summary>
/// Converts between absolute paths and paths relative to the app's own base
/// directory, so anything stored in config keeps working regardless of which drive
/// letter — or which machine's USB port — the whole portable install ends up on.
///
/// File pickers always return absolute paths, and the app's own base directory can
/// end up on any drive letter depending on where the user installs/copies it, so
/// storing paths exactly as a Browse dialog returns them silently bakes in a
/// dependency on today's drive letter. This is only actually fixable for files that
/// live *inside* the app's own portable folder tree — a ROM kept on a separate,
/// deliberately-external drive can never be made drive-letter-independent, and that's
/// a real, unavoidable limitation, not a bug to paper over.
/// </summary>
public static class PortablePathHelper
{
    /// <summary>
    /// If <paramref name="absolutePath"/> is inside <paramref name="baseDirectory"/>
    /// (defaults to the app's own base directory), returns a path relative to it.
    /// Otherwise returns the original path unchanged — including when the two paths
    /// are on different drives, or when the target is outside the base directory's
    /// tree (which would otherwise need a "..\" prefix that isn't truly portable
    /// either, since it still depends on the relative position of some folder outside
    /// the part of the tree that actually moves together).
    /// </summary>
    public static string ToPortablePath(string absolutePath, string? baseDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(absolutePath)) return absolutePath;

        try
        {
            var root = Path.GetFullPath(
                (baseDirectory ?? AppContext.BaseDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var full = Path.GetFullPath(absolutePath);

            var relative = Path.GetRelativePath(root, full);

            // Path.GetRelativePath returns the path unchanged (still absolute) when
            // the two paths don't share a root (different drive), and returns a
            // "..\"-prefixed path when the target is outside the root's own tree but
            // on the same drive. Either case means this isn't really "inside the
            // portable folder," so keep the original absolute path rather than store
            // something that only happens to work today.
            if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal))
                return absolutePath;

            return relative;
        }
        catch
        {
            // Never let a path-normalization quirk (e.g. an invalid path character)
            // block saving — just fall back to storing it exactly as given.
            return absolutePath;
        }
    }

    /// <summary>
    /// Resolves a stored path back to an absolute one for actual use — accepts either
    /// a relative path (resolved against <paramref name="baseDirectory"/>, defaulting
    /// to the app's own base directory) or an absolute path (returned unchanged).
    /// </summary>
    public static string ToAbsolutePath(string storedPath, string? baseDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return storedPath;

        return Path.IsPathRooted(storedPath)
            ? storedPath
            : Path.GetFullPath(Path.Combine(baseDirectory ?? AppContext.BaseDirectory, storedPath));
    }
}

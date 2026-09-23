using System.Text.RegularExpressions;

namespace Metrado.Revit2027;

/// <summary>
/// Puts an export's files beside the model so that a name there holds a
/// complete export or nothing. Each file is written under a temporary name in
/// the same folder and renamed into place only when complete, never onto a
/// file already there: an estimator prices the workbook by hand, and a
/// half-written or replaced copy would cost them that work. An export
/// abandoned before its commit leaves nothing behind, save in a folder that
/// lets files be created but not deleted (see <see cref="Reserve"/>). Uses
/// no Revit type.
/// </summary>
public sealed partial class ExportFiles : IDisposable
{
    private readonly string _workbookPath;
    private readonly string _workbookTemp;
    private readonly FileStream _workbook;
    private string? _warningsTemp;

    private ExportFiles(string workbookPath, string workbookTemp, FileStream workbook)
    {
        _workbookPath = workbookPath;
        _workbookTemp = workbookTemp;
        _workbook = workbook;
    }

    /// <summary>Where the workbook's content goes; it reaches its name on <see cref="Commit"/>.</summary>
    public Stream Workbook => _workbook;

    /// <summary>
    /// Takes a temporary file in the workbook's folder now, before the model
    /// is read, and renames it once, so a folder that refuses what the commit
    /// will need stops the export at once rather than after the long read:
    /// creating a file, and renaming it, which deletes its old name. A folder
    /// that lets files be created but not deleted keeps that one empty file,
    /// which it lets no one remove.
    /// </summary>
    /// <exception cref="IOException">The folder is missing or cannot be reached.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder refuses new files, or refuses renaming them.</exception>
    public static ExportFiles Reserve(string workbookPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);

        string created = TemporaryBeside(workbookPath);
        new FileStream(created, FileMode.CreateNew, FileAccess.Write).Dispose();

        string temp = TemporaryBeside(workbookPath);
        try
        {
            File.Move(created, temp, overwrite: false);
            return new ExportFiles(workbookPath, temp, new FileStream(temp, FileMode.Open, FileAccess.ReadWrite));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Discard(created);
            Discard(temp);
            throw;
        }
    }

    /// <summary>
    /// Renames the workbook into place, with its warnings list beside it when
    /// there is one. The list goes first and is withdrawn if the workbook
    /// cannot follow, so neither name is ever left holding half an export.
    /// </summary>
    /// <param name="warnings">The warnings list's text; null when the run raised none.</param>
    /// <returns>The warnings list's path, or null when none was written.</returns>
    /// <exception cref="IOException">A file took either name meanwhile, or the disk refused the content.</exception>
    public string? Commit(string? warnings)
    {
        // Through to the disk before any name is taken: the rename is
        // journaled and the data is not, so after a power loss the name could
        // otherwise hold an empty file. A full disk fails here too.
        _workbook.Flush(flushToDisk: true);
        _workbook.Dispose();

        string? warningsPath = null;
        if (warnings is not null)
        {
            _warningsTemp = TemporaryBeside(_workbookPath);
            using (FileStream stream = new(_warningsTemp, FileMode.CreateNew, FileAccess.Write))
            using (StreamWriter writer = new(stream))
            {
                writer.Write(warnings);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            warningsPath = WorkbookPath.WarningsFor(_workbookPath);
            File.Move(_warningsTemp, warningsPath, overwrite: false);
            _warningsTemp = null;
        }

        try
        {
            File.Move(_workbookTemp, _workbookPath, overwrite: false);
        }
        catch
        {
            // The list describes a workbook that never arrived. Failing to
            // withdraw it must not replace the reason the workbook failed.
            if (warningsPath is not null)
            {
                Discard(warningsPath);
            }

            throw;
        }

        return warningsPath;
    }

    public void Dispose()
    {
        // A write that failed for good (a full disk) fails again as the
        // stream closes; the reservation goes all the same. After a commit
        // the temporary names are gone and this finds nothing.
        try
        {
            _workbook.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        Discard(_workbookTemp);

        if (_warningsTemp is not null)
        {
            Discard(_warningsTemp);
        }
    }

    /// <summary>
    /// What the estimator is told when the files could not be written. The
    /// system's reason names the temporary file, which they never asked for
    /// and will not find; it is told under the workbook's name instead.
    /// </summary>
    public static string Explain(string workbookPath, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        // An evaluator, not a replacement pattern: the name is the model's,
        // and a '$' in it is text.
        string name = Path.GetFileName(workbookPath);
        string reason = Temporary().Replace(failure.Message, _ => name);
        return $"No workbook was written to {Path.GetDirectoryName(workbookPath)}. {reason}";
    }

    /// <summary>
    /// A temporary name no export gives and no estimator opens by mistake: the
    /// same folder, so the commit is a rename, never a copy across volumes.
    /// </summary>
    private static string TemporaryBeside(string workbookPath) =>
        Path.Combine(Path.GetDirectoryName(workbookPath)!, $"~metrado-{Guid.NewGuid():N}.partial");

    [GeneratedRegex(@"~metrado-[0-9a-f]{32}\.partial")]
    private static partial Regex Temporary();

    /// <summary>
    /// Cleanup runs while a failure is already on its way to the estimator;
    /// a temporary file that cannot be deleted must not replace that failure
    /// with its own. Its name says what it is.
    /// </summary>
    private static void Discard(string temporary)
    {
        try
        {
            File.Delete(temporary);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

using System.Security.AccessControl;

namespace Metrado.Revit2027.Tests;

/// <summary>
/// Pins how an export's files reach the model's folder. An estimator prices
/// the workbook by hand, so a name there holds a complete export or nothing:
/// each file is written under a temporary name in the same folder and renamed
/// into place only when complete, never onto a file already there, and an
/// export that fails leaves nothing behind (save one empty file in a folder
/// that lets no one delete it).
/// </summary>
public sealed class ExportFilesTests : IDisposable
{
    private readonly DirectoryInfo _project = Directory.CreateTempSubdirectory("metrado-project-");

    public void Dispose() => _project.Delete(recursive: true);

    private string Workbook => Path.Combine(_project.FullName, "Office Building - metrado 2026-09-23 1430.xlsx");

    private string Warnings => WorkbookPath.WarningsFor(Workbook);

    [Fact]
    public void TheWorkbookReachesItsNameOnlyWhenCommitted()
    {
        using ExportFiles files = ExportFiles.Reserve(Workbook);
        files.Workbook.Write([1, 2, 3]);

        Assert.False(File.Exists(Workbook));
        Assert.Null(files.Commit(warnings: null));

        Assert.Equal([1, 2, 3], File.ReadAllBytes(Workbook));
        Assert.Equal([Workbook], Files());
    }

    /// <summary>
    /// The reservation is a file in the model's own folder, so the commit is
    /// a rename there, and an export that stops short of it (the model read
    /// failed, the workbook writer threw) takes the reservation with it.
    /// </summary>
    [Fact]
    public void AnExportAbandonedBeforeItsCommitLeavesNothing()
    {
        using (ExportFiles files = ExportFiles.Reserve(Workbook))
        {
            files.Workbook.Write([1, 2, 3]);

            string reserved = Assert.Single(Files());
            Assert.NotEqual(Workbook, reserved);
        }

        Assert.Empty(Files());
    }

    [Fact]
    public void TheWarningsListIsWrittenBesideTheWorkbook()
    {
        using ExportFiles files = ExportFiles.Reserve(Workbook);

        string? written = files.Commit("- Walls w1 (Generic): Opening 7 could not be measured.");

        Assert.Equal(Warnings, written);
        Assert.Equal("- Walls w1 (Generic): Opening 7 could not be measured.", File.ReadAllText(Warnings));
        Assert.Equal([Warnings, Workbook], Files());
    }

    /// <summary>
    /// The name was free when chosen; if a file took it since, the commit
    /// fails rather than replace it, and the warnings list written for the
    /// workbook that never arrived is withdrawn with it.
    /// </summary>
    [Fact]
    public void AFileThatTookTheWorkbooksNameMeanwhileIsNeverReplaced()
    {
        using (ExportFiles files = ExportFiles.Reserve(Workbook))
        {
            files.Workbook.Write([1, 2, 3]);
            File.WriteAllText(Workbook, "priced by hand");

            Assert.Throws<IOException>(() => files.Commit("- a warning"));
        }

        Assert.Equal("priced by hand", File.ReadAllText(Workbook));
        Assert.Equal([Workbook], Files());
    }

    [Fact]
    public void AFileThatTookTheWarningsNameMeanwhileIsNeverReplaced()
    {
        using (ExportFiles files = ExportFiles.Reserve(Workbook))
        {
            File.WriteAllText(Warnings, "someone's notes");

            Assert.Throws<IOException>(() => files.Commit("- a warning"));
        }

        Assert.Equal("someone's notes", File.ReadAllText(Warnings));
        Assert.Equal([Warnings], Files());
    }

    /// <summary>
    /// The reservation touches the disk at once, so the command, which
    /// reserves before it reads the model, learns of a refusing folder before
    /// the long read rather than after it.
    /// </summary>
    [Fact]
    public void AFolderThatRefusesWritesFailsAtTheReservation()
    {
        using (Acl.Deny(_project.FullName, FileSystemRights.CreateFiles))
        {
            Assert.Throws<UnauthorizedAccessException>(() => ExportFiles.Reserve(Workbook));
        }

        Assert.Empty(Files());
    }

    /// <summary>
    /// A write that fails for good (a full disk, a share's quota) leaves
    /// bytes the stream will try again to write when it closes, and fail
    /// again. The reservation still goes. A lock taken by another handle
    /// stands in for the full disk: the same write keeps failing.
    /// </summary>
    [Fact]
    public void AWriteThatKeepsFailingStillLeavesNothingBehind()
    {
        ExportFiles files = ExportFiles.Reserve(Workbook);
        files.Workbook.Write(new byte[100]);
        string reserved = Assert.Single(Files());

        using (FileStream other = new(reserved, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            other.Lock(0, 1 << 20);
            files.Dispose();
            other.Unlock(0, 1 << 20);
        }

        Assert.Empty(Files());
    }

    /// <summary>
    /// The commit is a rename, which deletes the temporary name. A folder
    /// that lets files be created but not deleted must stop the export at
    /// the reservation, before the long read, not at the commit after it.
    /// It keeps at most one empty file, which it lets no one remove.
    /// </summary>
    [Fact]
    public void AFolderThatRefusesDeletionFailsAtTheReservation()
    {
        using (Acl.Deny(
            _project.FullName,
            new DeniedRight(FileSystemRights.DeleteSubdirectoriesAndFiles),
            new DeniedRight(FileSystemRights.Delete, InheritanceFlags.ObjectInherit, PropagationFlags.InheritOnly)))
        {
            Assert.Throws<UnauthorizedAccessException>(() => ExportFiles.Reserve(Workbook));
            Assert.InRange(Files().Length, 0, 1);
            Assert.All(Files(), left => Assert.Equal(0, new FileInfo(left).Length));
        }
    }

    /// <summary>A model's name is the workbook's; a '$' in it is text, never a substitution.</summary>
    [Theory]
    [InlineData("Lote $0")]
    [InlineData("US$'s bank")]
    [InlineData("A $_ B")]
    public void AnExplanationKeepsAWorkbookNameWithDollarSignsAsItIs(string model)
    {
        string workbook = Path.Combine(_project.FullName, $"{model} - metrado 2026-09-23 1430.xlsx");
        IOException failure = new($"Access to the path '{Path.Combine(_project.FullName, "~metrado-0123456789abcdef0123456789abcdef.partial")}' is denied.");

        string explained = ExportFiles.Explain(workbook, failure);

        Assert.Contains($"'{workbook}'", explained);
        Assert.DoesNotContain("~metrado-", explained);
    }

    /// <summary>
    /// The system's reason names the temporary file, which the estimator
    /// never asked for and will not find. The explanation names the workbook.
    /// </summary>
    [Fact]
    public void AFailureIsExplainedUnderTheWorkbooksNameNotTheTemporaryOne()
    {
        UnauthorizedAccessException refusal;
        using (Acl.Deny(_project.FullName, FileSystemRights.CreateFiles))
        {
            refusal = Assert.Throws<UnauthorizedAccessException>(() => ExportFiles.Reserve(Workbook));
        }

        string explained = ExportFiles.Explain(Workbook, refusal);

        Assert.Contains("~metrado-", refusal.Message);
        Assert.StartsWith($"No workbook was written to {_project.FullName}. ", explained);
        Assert.Contains(Workbook, explained);
        Assert.DoesNotContain("~metrado-", explained);
    }

    [Fact]
    public void AFolderThatIsGoneFailsAtTheReservation()
    {
        Assert.Throws<DirectoryNotFoundException>(() => ExportFiles.Reserve(Path.Combine(_project.FullName, "gone", "Office.xlsx")));
    }

    private string[] Files() => [.. Directory.GetFiles(_project.FullName).Order(StringComparer.Ordinal)];
}

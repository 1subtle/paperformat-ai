using System.Diagnostics;

namespace PaperFormat.Rendering;

public sealed record DocumentRendererOptions
{
    public string LibreOfficePath { get; init; } = "soffice";

    public string PdfToPpmPath { get; init; } = "pdftoppm";

    public int Dpi { get; init; } = 120;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(3);
}

public sealed record RenderedPage(int PageNumber, string ImagePath);

public sealed record RenderedDocument(
    string PdfPath,
    IReadOnlyList<RenderedPage> Pages);

public interface IDocumentRenderer
{
    bool IsAvailable { get; }

    Task<RenderedDocument> RenderAsync(
        string inputPath,
        string outputDirectory,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Headless LibreOffice renderer used as a compatibility and visual-AI input.
/// </summary>
public sealed class LibreOfficeDocumentRenderer : IDocumentRenderer
{
    private readonly DocumentRendererOptions _options;

    public LibreOfficeDocumentRenderer(DocumentRendererOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public bool IsAvailable =>
        ResolveExecutable(_options.LibreOfficePath) is not null
        && ResolveExecutable(_options.PdfToPpmPath) is not null;

    public async Task<RenderedDocument> RenderAsync(
        string inputPath,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        string input = Path.GetFullPath(inputPath);
        if (!File.Exists(input))
        {
            throw new FileNotFoundException(
                "The DOCX to render does not exist.",
                input);
        }

        string soffice = ResolveExecutable(_options.LibreOfficePath)
            ?? throw new InvalidOperationException(
                "LibreOffice is not available for rendered-page verification.");
        string pdftoppm = ResolveExecutable(_options.PdfToPpmPath)
            ?? throw new InvalidOperationException(
                "pdftoppm is not available for rendered-page verification.");
        string output = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(output)
            && Directory.EnumerateFileSystemEntries(output).Any())
        {
            throw new IOException(
                "The render output directory must be new or empty.");
        }

        Directory.CreateDirectory(output);
        string profile = Path.Combine(
            output,
            $".libreoffice-profile-{Guid.NewGuid():N}");
        Directory.CreateDirectory(profile);
        string profileUri = new Uri(profile + Path.DirectorySeparatorChar)
            .AbsoluteUri
            .TrimEnd('/');
        await RunAsync(
            soffice,
            [
                "--headless",
                $"-env:UserInstallation={profileUri}",
                "--convert-to",
                "pdf",
                "--outdir",
                output,
                input,
            ],
            output,
            cancellationToken);
        string expectedPdf = Path.Combine(
            output,
            Path.GetFileNameWithoutExtension(input) + ".pdf");
        string pdf = File.Exists(expectedPdf)
            ? expectedPdf
            : Directory.EnumerateFiles(output, "*.pdf")
                .SingleOrDefault()
                ?? throw new InvalidDataException(
                    "LibreOffice did not produce a PDF.");
        await RunAsync(
            pdftoppm,
            [
                "-png",
                "-r",
                _options.Dpi.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                pdf,
                Path.Combine(output, "page"),
            ],
            output,
            cancellationToken);
        RenderedPage[] pages = Directory
            .EnumerateFiles(output, "page-*.png")
            .Select(
                path => new RenderedPage(
                    PageNumber(path),
                    path))
            .OrderBy(page => page.PageNumber)
            .ToArray();
        if (pages.Length == 0)
        {
            throw new InvalidDataException(
                "Rendered-page conversion produced no PNG pages.");
        }

        return new RenderedDocument(pdf, pages);
    }

    private async Task RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["HOME"] = workingDirectory;
        start.Environment["TMPDIR"] = workingDirectory;
        using var process = new Process { StartInfo = start };
        if (!process.Start())
        {
            throw new InvalidOperationException(
                $"Unable to start '{Path.GetFileName(executable)}'.");
        }

        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(
            cancellationToken);
        Task<string> standardError = process.StandardError.ReadToEndAsync(
            cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(_options.Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"'{Path.GetFileName(executable)}' exceeded the render timeout.");
        }

        string output = await standardOutput;
        string error = await standardError;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'{Path.GetFileName(executable)}' exited with code {process.ExitCode}. " +
                SafeProcessDetail(error, output));
        }
    }

    private static string? ResolveExecutable(string configured)
    {
        if (Path.IsPathFullyQualified(configured))
        {
            return File.Exists(configured) ? configured : null;
        }

        string? path = Environment.GetEnvironmentVariable("PATH");
        return path?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, configured))
            .FirstOrDefault(File.Exists);
    }

    private static int PageNumber(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        int separator = name.LastIndexOf('-');
        if (separator < 0
            || !int.TryParse(
                name[(separator + 1)..],
                out int page)
            || page <= 0)
        {
            throw new InvalidDataException(
                $"Unexpected rendered page name '{name}'.");
        }

        return page;
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process already exited.
        }
    }

    private static string SafeProcessDetail(
        params string[] details)
    {
        string combined = string.Join(
            " ",
            details
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim()));
        return combined.Length switch
        {
            0 => "No diagnostic output was returned.",
            <= 500 => combined,
            _ => combined[..500],
        };
    }
}

public sealed class UnavailableDocumentRenderer : IDocumentRenderer
{
    public bool IsAvailable => false;

    public Task<RenderedDocument> RenderAsync(
        string inputPath,
        string outputDirectory,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "Rendered-page verification is not available.");
}

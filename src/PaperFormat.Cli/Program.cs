using PaperFormat.Cli;
using PaperFormat.Rendering;

return await new CliApplication(
    new LibreOfficeDocumentRenderer(new DocumentRendererOptions()))
    .RunAsync(args, Console.Out, Console.Error);

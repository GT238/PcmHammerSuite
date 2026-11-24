using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;

namespace PcmSuite;

/// <summary>
/// One place for the credits of the whole project: PcmSuite's own, followed by PCM Hammer's credits page,
/// which is taken straight from the PcmHammer source tree at build time (so it follows the submodule).
/// </summary>
public sealed class CreditsView : UserControl
{
    public CreditsView()
    {
        HtmlPageView page = new();
        page.Show(BuildHtml(), "credits");
        this.Content = page;
    }

    private static string BuildHtml()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "Credits");
        string ours = Read(Path.Combine(folder, "PcmSuite-credits.html"));
        string theirs = Read(Path.Combine(folder, "PcmHammer-credits.html"));

        // PCM Hammer's page is a whole HTML document; keep only what is inside its <body>.
        Match body = Regex.Match(theirs, "<body[^>]*>(.*)</body>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        string hammer = body.Success ? body.Groups[1].Value : theirs;

        return "<html><head><base target=\"_blank\"/><style>" +
               "* { font-family: sans-serif; } p, li { font-size: 0.875em; } " +
               "@media (prefers-color-scheme: dark) { body { background: #202020; color: #e6e6e6; } a { color: #8ab4f8; } }" +
               "</style></head><body>" + ours + hammer + "</body></html>";
    }

    private static string Read(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : $"<p>Credits file not found: {Path.GetFileName(path)}</p>";
        }
        catch (Exception ex)
        {
            return $"<p>Could not read {Path.GetFileName(path)}: {ex.Message}</p>";
        }
    }
}
